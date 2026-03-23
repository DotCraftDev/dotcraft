using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
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
    private readonly LocalWorkflowLoader _workflowLoader;
    private readonly IToolProfileRegistry _toolProfileRegistry;
    private readonly ILogger<AutomationOrchestrator> _logger;
    private readonly ConcurrentDictionary<string, IAutomationSource> _sources = new(StringComparer.OrdinalIgnoreCase);
    private readonly OrchestratorState _state = new();
    private readonly ConcurrentDictionary<string, AutomationTask> _allTasks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _concurrency;

    private AutomationSessionClient? _sessionClient;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private PeriodicTimer? _pollTimer;
    private int _stopped;

    public AutomationOrchestrator(
        AutomationsConfig config,
        AutomationWorkspaceManager workspaceManager,
        LocalWorkflowLoader workflowLoader,
        IToolProfileRegistry toolProfileRegistry,
        ILogger<AutomationOrchestrator> logger,
        IEnumerable<IAutomationSource> sources)
    {
        _config = config;
        _workspaceManager = workspaceManager;
        _workflowLoader = workflowLoader;
        _toolProfileRegistry = toolProfileRegistry;
        _logger = logger;
        foreach (var s in sources)
            _sources[s.Name] = s;
        _concurrency = new SemaphoreSlim(config.MaxConcurrentTasks, config.MaxConcurrentTasks);
    }

    /// <summary>
    /// Fired after every task status transition. Subscribers (e.g. <c>AutomationsEventDispatcher</c>)
    /// use this to push Wire Protocol notifications.
    /// </summary>
    public event Func<AutomationTask, AutomationTaskStatus, Task>? OnTaskStatusChanged;

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

    /// <summary>
    /// Returns a snapshot of all tasks across all sources, merging the in-memory cache
    /// (dispatched/completed tasks) with each source's full task list (includes pending).
    /// </summary>
    public async Task<IReadOnlyList<AutomationTask>> GetAllTasksAsync(CancellationToken ct)
    {
        var merged = new Dictionary<string, AutomationTask>(StringComparer.Ordinal);

        foreach (var task in _allTasks.Values)
            merged[TaskKey(task)] = task;

        foreach (var source in _sources.Values)
        {
            try
            {
                var sourceTasks = await source.GetAllTasksAsync(ct);
                foreach (var t in sourceTasks)
                    merged.TryAdd(TaskKey(t), t);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetAllTasksAsync failed for source {Source}", source.Name);
            }
        }

        return merged.Values.ToList();
    }

    /// <summary>
    /// Approves a task via its source. Transitions the task to <see cref="AutomationTaskStatus.Approved"/>.
    /// </summary>
    public async Task ApproveTaskAsync(string sourceName, string taskId, CancellationToken ct)
    {
        var source = ResolveSource(sourceName);
        await source.ApproveTaskAsync(taskId, ct);

        if (_allTasks.TryGetValue(TaskKey(sourceName, taskId), out var cached))
        {
            cached.Status = AutomationTaskStatus.Approved;
            await RaiseStatusChangedAsync(cached, AutomationTaskStatus.Approved);
        }
    }

    /// <summary>
    /// Rejects a task via its source. Transitions the task to <see cref="AutomationTaskStatus.Rejected"/>.
    /// </summary>
    public async Task RejectTaskAsync(string sourceName, string taskId, string? reason, CancellationToken ct)
    {
        var source = ResolveSource(sourceName);
        await source.RejectTaskAsync(taskId, reason, ct);

        if (_allTasks.TryGetValue(TaskKey(sourceName, taskId), out var cached))
        {
            cached.Status = AutomationTaskStatus.Rejected;
            await RaiseStatusChangedAsync(cached, AutomationTaskStatus.Rejected);
        }
    }

    /// <summary>
    /// Deletes the task via its source and removes it from the orchestrator cache.
    /// </summary>
    public async Task DeleteTaskAsync(string sourceName, string taskId, CancellationToken ct)
    {
        var source = ResolveSource(sourceName);
        await source.DeleteTaskAsync(taskId, ct);
        _allTasks.TryRemove(TaskKey(sourceName, taskId), out _);
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
        var sw = Stopwatch.StartNew();
        var sourceSummaries = new List<string>();
        var globalIds = new List<string>();
        const int maxGlobalIds = 10;
        var totalPendingEligible = 0;

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

            var pendingOnly = pending
                .Where(t => t.Status == AutomationTaskStatus.Pending)
                .ToList();
            totalPendingEligible += pendingOnly.Count;

            foreach (var t in pendingOnly)
            {
                if (globalIds.Count >= maxGlobalIds)
                    break;
                globalIds.Add(TruncateTaskIdForLog(t.Id));
            }

            sourceSummaries.Add($"{source.Name} pending={pendingOnly.Count}");

            foreach (var task in pending)
            {
                if (ct.IsCancellationRequested)
                {
                    sw.Stop();
                    LogPollSummary(sw.ElapsedMilliseconds, sourceSummaries, globalIds, totalPendingEligible);
                    return;
                }

                if (task.Status != AutomationTaskStatus.Pending)
                    continue;

                if (!_state.TryBeginTask(TaskKey(task)))
                {
                    _logger.LogDebug(
                        "Task {TaskId} skipped (already active) for source {SourceName}",
                        task.Id,
                        source.Name);
                    continue;
                }

                _ = Task.Run(() => RunDispatchAsync(source, task, ct), ct);
            }
        }

        sw.Stop();
        LogPollSummary(sw.ElapsedMilliseconds, sourceSummaries, globalIds, totalPendingEligible);
    }

    private void LogPollSummary(
        long elapsedMs,
        List<string> sourceSummaries,
        List<string> globalIds,
        int totalPendingEligible)
    {
        var sb = new StringBuilder();
        sb.AppendJoin("; ", sourceSummaries);
        if (globalIds.Count > 0)
        {
            sb.Append(" taskIds=[");
            sb.AppendJoin(',', globalIds);
            sb.Append(']');
            var more = totalPendingEligible - globalIds.Count;
            if (more > 0)
                sb.Append(" (+").Append(more).Append(" more)");
        }

        _logger.LogInformation(
            "Poll completed in {ElapsedMs}ms. {PollDetails}",
            elapsedMs,
            sb.ToString());
    }

    /// <summary>Truncates task ids for log lines (max 64 chars).</summary>
    private static string TruncateTaskIdForLog(string id)
    {
        if (string.IsNullOrEmpty(id))
            return id;
        return id.Length <= 64 ? id : id[..64];
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
            _logger.LogInformation(
                "Dispatch cancelled for task {TaskId} (source: {SourceName})",
                task.Id,
                source.Name);
            try
            {
                task.Status = AutomationTaskStatus.Failed;
                TrackTask(task);
                await source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, CancellationToken.None);
                await RaiseStatusChangedAsync(task, AutomationTaskStatus.Failed);
            }
            catch
            {
                // Best effort: original ct may be cancelled; source persistence may fail.
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dispatch failed for task {TaskId} (source: {SourceName})", task.Id, source.Name);
            try
            {
                task.Status = AutomationTaskStatus.Failed;
                TrackTask(task);
                await source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, ct);
                await RaiseStatusChangedAsync(task, AutomationTaskStatus.Failed);
            }
            catch (Exception ex2)
            {
                _logger.LogError(ex2, "OnStatusChangedAsync(Failed) failed for task {TaskId}", task.Id);
            }
        }
        finally
        {
            _state.EndTask(TaskKey(task));
            _concurrency.Release();
        }
    }

    private async Task DispatchTaskAsync(IAutomationSource source, AutomationTask task, CancellationToken ct)
    {
        var client = _sessionClient;
        if (client == null)
            throw new InvalidOperationException("Session client is not set.");

        _logger.LogInformation(
            "Dispatch starting for task {TaskId} (source: {SourceName})",
            task.Id,
            source.Name);

        task.Status = AutomationTaskStatus.Dispatched;
        TrackTask(task);
        await source.OnStatusChangedAsync(task, AutomationTaskStatus.Dispatched, ct);
        await RaiseStatusChangedAsync(task, AutomationTaskStatus.Dispatched);
        _logger.LogInformation(
            "Task {TaskId} status: Dispatched (source: {SourceName})",
            task.Id,
            source.Name);

        string workspacePath;
        string? automationTaskDirectory = null;

        if (task is LocalAutomationTask localTask)
        {
            var workspaceMode = await _workflowLoader.GetWorkspaceModeAsync(localTask, ct);
            if (workspaceMode == AutomationWorkspaceMode.Isolated)
            {
                workspacePath = Path.Combine(localTask.TaskDirectory, "workspace");
                Directory.CreateDirectory(workspacePath);
            }
            else
            {
                workspacePath = client.ProjectWorkspacePath;
                Directory.CreateDirectory(workspacePath);
            }

            localTask.AgentWorkspacePath = workspacePath;
            automationTaskDirectory = localTask.TaskDirectory;
        }
        else
        {
            workspacePath = await source.ProvisionWorkspaceAsync(task, ct)
                            ?? await _workspaceManager.ProvisionAsync(task, ct);
        }

        var threadConfig = new ThreadConfiguration
        {
            WorkspaceOverride = workspacePath,
            ToolProfile = task.ToolProfileOverride ?? source.ToolProfileName,
            ApprovalPolicy = ApprovalPolicy.AutoApprove,
            AutomationTaskDirectory = automationTaskDirectory,
            RequireApprovalOutsideWorkspace = ResolveRequireApprovalOutsideWorkspace(task)
        };

        var threadId = await client.CreateOrResumeThreadAsync(
            AutomationsChannelName,
            $"task-{task.SourceName}-{task.Id}",
            threadConfig,
            ct,
            displayName: task.Title);

        _logger.LogInformation(
            "Thread ready for task {TaskId} (source: {SourceName}, threadId: {ThreadId})",
            task.Id,
            source.Name,
            threadId);

        task.ThreadId = threadId;
        task.Status = AutomationTaskStatus.AgentRunning;
        TrackTask(task);
        await source.OnStatusChangedAsync(task, AutomationTaskStatus.AgentRunning, ct);
        await RaiseStatusChangedAsync(task, AutomationTaskStatus.AgentRunning);
        _logger.LogInformation(
            "Task {TaskId} status: AgentRunning (source: {SourceName})",
            task.Id,
            source.Name);

        var workflow = await source.GetWorkflowAsync(task, ct);
        if (workflow.Steps.Count == 0)
            throw new InvalidOperationException("Workflow has no steps.");

        string? summary = null;
        var round = 0;
        var stopWorkflow = false;
        var turnFailed = false;
        var turnCancelled = false;

        await source.OnBeforeAgentRunAsync(task, workspacePath, ct);
        try
        {
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

                _logger.LogInformation(
                    "Workflow round {Round} completed for task {TaskId} (source: {SourceName}, maxRounds: {MaxRounds})",
                    round,
                    task.Id,
                    source.Name,
                    workflow.MaxRounds);
            }
        }
        finally
        {
            try
            {
                await source.OnAfterAgentRunAsync(task, workspacePath, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OnAfterAgentRunAsync failed for task {TaskId}", task.Id);
            }
        }

        if (ct.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Task {TaskId} workflow stopped: cancellation requested (source: {SourceName}, threadId: {ThreadId})",
                task.Id,
                source.Name,
                threadId);
            task.Status = AutomationTaskStatus.Failed;
            TrackTask(task);
            try
            {
                await source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, CancellationToken.None);
                await RaiseStatusChangedAsync(task, AutomationTaskStatus.Failed);
            }
            catch
            {
                // Best effort
            }

            return;
        }

        if (turnFailed)
        {
            _logger.LogInformation(
                "Task {TaskId} workflow stopped: turn failed (source: {SourceName}, threadId: {ThreadId})",
                task.Id,
                source.Name,
                threadId);
            task.Status = AutomationTaskStatus.Failed;
            TrackTask(task);
            await source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, ct);
            await RaiseStatusChangedAsync(task, AutomationTaskStatus.Failed);
            return;
        }

        if (turnCancelled && task.Status != AutomationTaskStatus.AgentCompleted)
        {
            _logger.LogInformation(
                "Task {TaskId} workflow stopped: turn cancelled (source: {SourceName}, threadId: {ThreadId})",
                task.Id,
                source.Name,
                threadId);
            task.Status = AutomationTaskStatus.Failed;
            TrackTask(task);
            await source.OnStatusChangedAsync(task, AutomationTaskStatus.Failed, ct);
            await RaiseStatusChangedAsync(task, AutomationTaskStatus.Failed);
            return;
        }

        task.Status = AutomationTaskStatus.AgentCompleted;
        TrackTask(task);
        await source.OnAgentCompletedAsync(task, summary ?? string.Empty, ct);
        await RaiseStatusChangedAsync(task, AutomationTaskStatus.AgentCompleted);
        _logger.LogInformation(
            "Task {TaskId} status: AgentCompleted (source: {SourceName}, summaryLength: {SummaryLength})",
            task.Id,
            source.Name,
            summary?.Length ?? 0);

        task.Status = AutomationTaskStatus.AwaitingReview;
        TrackTask(task);
        await source.OnStatusChangedAsync(task, AutomationTaskStatus.AwaitingReview, ct);
        await RaiseStatusChangedAsync(task, AutomationTaskStatus.AwaitingReview);
        _logger.LogInformation(
            "Task {TaskId} status: AwaitingReview (source: {SourceName}, threadId: {ThreadId})",
            task.Id,
            source.Name,
            threadId);
    }

    /// <summary>
    /// Local automation tool policy: when <c>false</c>, file/shell operations outside the thread workspace are rejected
    /// without prompting; when <c>true</c>, outside-workspace operations are auto-approved (higher risk).
    /// Non-local tasks return null to use global AppConfig tool defaults.
    /// Legacy: <c>default</c> previously meant interactive prompts; it now maps to workspace scope (false), not full auto.
    /// </summary>
    private static bool? ResolveRequireApprovalOutsideWorkspace(AutomationTask task)
    {
        if (task is not LocalAutomationTask local)
            return null;

        var p = local.ApprovalPolicy?.Trim();
        if (string.IsNullOrEmpty(p))
            return false;

        if (string.Equals(p, "fullAuto", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(p, "autoApprove", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(p, "workspaceScope", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(p, "default", StringComparison.OrdinalIgnoreCase))
            return false;

        return false;
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

    private void TrackTask(AutomationTask task) =>
        _allTasks[TaskKey(task)] = task;

    private async Task RaiseStatusChangedAsync(AutomationTask task, AutomationTaskStatus newStatus)
    {
        var handler = OnTaskStatusChanged;
        if (handler != null)
        {
            try
            {
                await handler(task, newStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnTaskStatusChanged handler failed for task {TaskId}", task.Id);
            }
        }
    }

    private IAutomationSource ResolveSource(string sourceName)
    {
        if (!_sources.TryGetValue(sourceName, out var source))
            throw new KeyNotFoundException($"Automation source '{sourceName}' not found.");
        return source;
    }

    private static string TaskKey(AutomationTask task) => TaskKey(task.SourceName, task.Id);
    private static string TaskKey(string sourceName, string taskId) => $"{sourceName}::{taskId}";
}
