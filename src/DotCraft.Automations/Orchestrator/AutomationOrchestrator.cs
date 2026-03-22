using System.Collections.Concurrent;
using System.Threading;
using DotCraft.Agents;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Local;
using DotCraft.Automations.Protocol;
using DotCraft.Automations.Workspace;
using DotCraft.Protocol;
using Microsoft.Extensions.Logging;

namespace DotCraft.Automations.Orchestrator;

/// <summary>
/// Polls registered sources and dispatches automation tasks to the shared session service.
/// </summary>
public sealed class AutomationOrchestrator
{
    private const string AutomationsChannelName = "automations";

    private readonly AutomationsConfig _config;
    private readonly AutomationWorkspaceManager _workspaceManager;
    private readonly IToolProfileRegistry _toolProfileRegistry;
    private readonly ILogger<AutomationOrchestrator> _logger;
    private readonly ConcurrentDictionary<string, IAutomationSource> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly OrchestratorState _state = new();
    private readonly SemaphoreSlim _concurrency;

    private AutomationSessionClient? _sessionClient;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private PeriodicTimer? _pollTimer;
    private int _stopped;

    public AutomationOrchestrator(
        AutomationsConfig config,
        AutomationWorkspaceManager workspaceManager,
        IToolProfileRegistry toolProfileRegistry,
        ILogger<AutomationOrchestrator> logger,
        IEnumerable<IAutomationSource> sources)
    {
        _config = config;
        _workspaceManager = workspaceManager;
        _toolProfileRegistry = toolProfileRegistry;
        _logger = logger;
        foreach (var s in sources)
            _sources[s.Name] = s;
        _concurrency = new SemaphoreSlim(config.MaxConcurrentTasks, config.MaxConcurrentTasks);
    }

    /// <summary>
    /// Registers an additional source (e.g. from tests). If the orchestrator is already running,
    /// call <see cref="RegisterToolProfilesForAllSources"/> to register profiles.
    /// </summary>
    public void RegisterSource(IAutomationSource source) => _sources[source.Name] = source;

    /// <summary>
    /// Supplies the session client created with the host's shared <see cref="ISessionService"/>.
    /// </summary>
    public void SetSessionClient(AutomationSessionClient client) => _sessionClient = client;

    /// <summary>
    /// Calls <see cref="IAutomationSource.RegisterToolProfile"/> on every registered source.
    /// </summary>
    public void RegisterToolProfilesForAllSources()
    {
        foreach (var source in _sources.Values)
            source.RegisterToolProfile(_toolProfileRegistry);
    }

    public Task StartAsync(CancellationToken ct)
    {
        if (_sessionClient == null)
            throw new InvalidOperationException("Session client must be set before starting the orchestrator.");

        RegisterToolProfilesForAllSources();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pollTimer = new PeriodicTimer(_config.PollingInterval);
        _pollTask = PollLoopAsync(_cts.Token);
        _logger.LogInformation(
            "Automations orchestrator started (interval: {Interval}, max concurrent: {Max})",
            _config.PollingInterval,
            _config.MaxConcurrentTasks);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        if (_cts != null)
            await _cts.CancelAsync();

        _pollTimer?.Dispose();

        if (_pollTask != null)
        {
            try
            {
                await _pollTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _logger.LogInformation("Automations orchestrator stopped");
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Automations poll loop running");
        try
        {
            // Immediate first poll (do not wait one interval at startup).
            try
            {
                await PollOnceAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Automations initial poll failed");
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await _pollTimer!.WaitForNextTickAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    await PollOnceAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Automations poll tick failed");
                }
            }
        }
        finally
        {
            _logger.LogDebug("Automations poll loop exited");
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        foreach (var source in _sources.Values)
        {
            IReadOnlyList<AutomationTask> pending;
            try
            {
                pending = await source.GetPendingTasksAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetPendingTasksAsync failed for source {Source}", source.Name);
                continue;
            }

            foreach (var task in pending)
            {
                if (ct.IsCancellationRequested)
                    return;

                if (task.Status != AutomationTaskStatus.Pending)
                    continue;

                if (!_state.TryBeginTask(task.Id))
                    continue;

                _ = Task.Run(() => RunDispatchAsync(source, task, ct), ct);
            }
        }
    }

    private async Task RunDispatchAsync(IAutomationSource source, AutomationTask task, CancellationToken ct)
    {
        await _concurrency.WaitAsync(ct);
        try
        {
            await DispatchTaskAsync(source, task, ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Dispatch cancelled for task {TaskId}", task.Id);
            try
            {
                task.Status = AutomationTaskStatus.Failed;
                await source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, CancellationToken.None);
            }
            catch
            {
                // Best effort: original ct may be cancelled; source persistence may fail.
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dispatch failed for task {TaskId}", task.Id);
            try
            {
                task.Status = AutomationTaskStatus.Failed;
                await source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, ct);
            }
            catch (Exception ex2)
            {
                _logger.LogError(ex2, "OnStatusChangedAsync(Failed) failed for task {TaskId}", task.Id);
            }
        }
        finally
        {
            _state.EndTask(task.Id);
            _concurrency.Release();
        }
    }

    private async Task DispatchTaskAsync(IAutomationSource source, AutomationTask task, CancellationToken ct)
    {
        var client = _sessionClient;
        if (client == null)
            throw new InvalidOperationException("Session client is not set.");

        task.Status = AutomationTaskStatus.Dispatched;
        await source.OnStatusChangedAsync(task, AutomationTaskStatus.Dispatched, ct);

        string workspacePath;
        if (task is LocalAutomationTask localTask)
        {
            workspacePath = Path.Combine(localTask.TaskDirectory, "workspace");
            Directory.CreateDirectory(workspacePath);
            localTask.AgentWorkspacePath = workspacePath;
        }
        else
        {
            workspacePath = await _workspaceManager.ProvisionAsync(task, ct);
        }

        var threadConfig = new ThreadConfiguration
        {
            WorkspaceOverride = workspacePath,
            ToolProfile = source.ToolProfileName,
            ApprovalPolicy = ApprovalPolicy.AutoApprove
        };

        var threadId = await client.CreateOrResumeThreadAsync(
            AutomationsChannelName,
            $"task-{task.Id}",
            threadConfig,
            ct,
            displayName: task.Title);

        task.ThreadId = threadId;
        task.Status = AutomationTaskStatus.AgentRunning;
        await source.OnStatusChangedAsync(task, AutomationTaskStatus.AgentRunning, ct);

        var workflow = await source.GetWorkflowAsync(task, ct);
        if (workflow.Steps.Count == 0)
            throw new InvalidOperationException("Workflow has no steps.");

        string? summary = null;
        var round = 0;
        var stopWorkflow = false;
        var turnFailed = false;
        var turnCancelled = false;

        while (round < workflow.MaxRounds && !ct.IsCancellationRequested && !stopWorkflow)
        {
            round++;
            foreach (var step in workflow.Steps)
            {
                await foreach (var evt in client.SubmitTurnAsync(threadId, step.Prompt, ct))
                {
                    if (evt.EventType == SessionEventType.TurnCompleted && evt.TurnPayload is { } turn)
                        summary = ExtractSummaryFromTurn(turn);

                    if (evt.EventType == SessionEventType.TurnFailed)
                    {
                        turnFailed = true;
                        stopWorkflow = true;
                        break;
                    }

                    if (evt.EventType == SessionEventType.TurnCancelled)
                    {
                        turnCancelled = true;
                        stopWorkflow = true;
                        break;
                    }
                }

                if (await source.ShouldStopWorkflowAfterTurnAsync(task, ct))
                {
                    stopWorkflow = true;
                    break;
                }

                if (stopWorkflow || ct.IsCancellationRequested)
                    break;
            }
        }

        if (ct.IsCancellationRequested)
        {
            task.Status = AutomationTaskStatus.Failed;
            try
            {
                await source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, CancellationToken.None);
            }
            catch
            {
                // Best effort
            }

            return;
        }

        if (turnFailed)
        {
            task.Status = AutomationTaskStatus.Failed;
            await source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, ct);
            return;
        }

        if (turnCancelled && task.Status != AutomationTaskStatus.AgentCompleted)
        {
            task.Status = AutomationTaskStatus.Failed;
            await source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, ct);
            return;
        }

        task.Status = AutomationTaskStatus.AgentCompleted;
        await source.OnAgentCompletedAsync(task, summary ?? string.Empty, ct);

        task.Status = AutomationTaskStatus.AwaitingReview;
        await source.OnStatusChangedAsync(task, AutomationTaskStatus.AwaitingReview, ct);
    }

    private static string ExtractSummaryFromTurn(SessionTurn turn)
    {
        for (var i = turn.Items.Count - 1; i >= 0; i--)
        {
            var item = turn.Items[i];
            if (item.Type != ItemType.AgentMessage)
                continue;
            var text = item.AsAgentMessage?.Text;
            if (!string.IsNullOrWhiteSpace(text))
                return text!;
        }

        return string.Empty;
    }
}
