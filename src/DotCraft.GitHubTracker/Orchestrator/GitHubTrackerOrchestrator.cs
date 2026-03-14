using DotCraft.Configuration;
using DotCraft.DashBoard;
using DotCraft.GitHubTracker.Execution;
using DotCraft.GitHubTracker.Tracker;
using DotCraft.GitHubTracker.Workflow;
using DotCraft.GitHubTracker.Workspace;
using Microsoft.Extensions.Logging;

namespace DotCraft.GitHubTracker.Orchestrator;

/// <summary>
/// Core orchestration state machine per SPEC.md Sections 7-8.
/// Owns the poll tick, in-memory state, and all dispatch/retry/reconciliation decisions.
/// Implements <see cref="IOrchestratorSnapshotProvider"/> so the dashboard can query state.
/// </summary>
public sealed class GitHubTrackerOrchestrator : IAsyncDisposable, IOrchestratorSnapshotProvider
{
    private readonly IWorkItemTracker _tracker;
    private readonly WorkflowLoader _workflowLoader;
    private readonly WorkflowLoader _prWorkflowLoader;
    private readonly WorkItemWorkspaceManager _workspaceManager;
    private readonly WorkItemAgentRunnerFactory _agentRunnerFactory;
    private readonly GitHubTrackerConfig _config;
    private readonly ILogger<GitHubTrackerOrchestrator> _logger;
    private readonly OrchestratorState _state = new();
    private readonly Lock _stateLock = new();
    private readonly List<Task> _pendingCleanups = [];
    private readonly string? _issuesWorkflowPath;
    private readonly string? _pullRequestWorkflowPath;

    private PeriodicTimer? _pollTimer;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;

    public GitHubTrackerOrchestrator(
        IWorkItemTracker tracker,
        WorkflowLoader workflowLoader,
        WorkflowLoader prWorkflowLoader,
        WorkItemWorkspaceManager workspaceManager,
        WorkItemAgentRunnerFactory agentRunnerFactory,
        GitHubTrackerConfig config,
        string workspacePath,
        ILogger<GitHubTrackerOrchestrator> logger)
    {
        _tracker = tracker;
        _workflowLoader = workflowLoader;
        _prWorkflowLoader = prWorkflowLoader;
        _workspaceManager = workspaceManager;
        _agentRunnerFactory = agentRunnerFactory;
        _config = config;
        _logger = logger;
        _issuesWorkflowPath = ResolveWorkflowPath(config.IssuesWorkflowPath, workspacePath);
        _pullRequestWorkflowPath = ResolveWorkflowPath(config.PullRequestWorkflowPath, workspacePath);

        _state.PollIntervalMs = config.Polling.IntervalMs;
        _state.MaxConcurrentAgents = config.Agent.MaxConcurrentAgents;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("GitHubTracker orchestrator starting");

        var issueWorkflow = LoadWorkflowIfAvailable(
            _workflowLoader,
            _issuesWorkflowPath,
            "issue");
        var pullRequestWorkflow = LoadWorkflowIfAvailable(
            _prWorkflowLoader,
            _pullRequestWorkflowPath,
            "PR review");

        var schedulerConfig = issueWorkflow?.Config ?? pullRequestWorkflow?.Config;
        if (schedulerConfig == null)
            throw new InvalidOperationException(
                "GitHubTracker requires at least one valid workflow. Configure IssuesWorkflowPath or PullRequestWorkflowPath.");

        _state.PollIntervalMs = schedulerConfig.Polling.IntervalMs;
        _state.MaxConcurrentAgents = schedulerConfig.Agent.MaxConcurrentAgents;

        await StartupCleanupAsync(ct);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _pollTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(_state.PollIntervalMs));

        _pollTask = PollLoopAsync(_cts.Token);
        _logger.LogInformation("GitHubTracker orchestrator started, poll interval: {Interval}ms", _state.PollIntervalMs);
    }

    public async Task StopAsync()
    {
        _logger.LogInformation("GitHubTracker orchestrator stopping");

        if (_cts != null)
            await _cts.CancelAsync();

        if (_pollTask != null)
        {
            try { await _pollTask; } catch (OperationCanceledException) { }
        }

        // Wait for all running workers to finish
        List<Task> runningTasks;
        lock (_stateLock)
        {
            foreach (var entry in _state.Running.Values)
                entry.Cts.Cancel();

            runningTasks = _state.Running.Values.Select(e => e.WorkerTask).ToList();
        }

        if (runningTasks.Count > 0)
        {
            try { await Task.WhenAll(runningTasks).WaitAsync(TimeSpan.FromSeconds(30)); }
            catch { /* best effort */ }
        }

        List<Task> cleanupTasks;
        lock (_stateLock) { cleanupTasks = [.. _pendingCleanups]; }

        if (cleanupTasks.Count > 0)
        {
            try { await Task.WhenAll(cleanupTasks).WaitAsync(TimeSpan.FromSeconds(30)); }
            catch { /* best effort */ }
        }

        _logger.LogInformation("GitHubTracker orchestrator stopped");
    }

    /// <summary>
    /// Get a snapshot of current orchestrator state for dashboard/API.
    /// </summary>
    public OrchestratorSnapshot GetSnapshot()
    {
        lock (_stateLock)
        {
            var activeSeconds = _state.Running.Values
                .Sum(r => (DateTimeOffset.UtcNow - r.StartedAt).TotalSeconds);

            return new OrchestratorSnapshot
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                RunningCount = _state.Running.Count,
                RetryingCount = _state.RetryAttempts.Count,
                Running = _state.Running.Values.Select(r => new RunningWorkItemSummary
                {
                    WorkItemId = r.WorkItemId,
                    Identifier = r.Identifier,
                    State = r.WorkItem.State,
                    SessionId = r.SessionId,
                    TurnCount = r.TurnCount,
                    LastEvent = r.LastEvent,
                    LastMessage = r.LastMessage,
                    StartedAt = r.StartedAt,
                    LastEventAt = r.LastEventTimestamp,
                    InputTokens = r.InputTokens,
                    OutputTokens = r.OutputTokens,
                    TotalTokens = r.TotalTokens,
                }).ToList(),
                Retrying = _state.RetryAttempts.Values.Select(r => new RetryWorkItemSummary
                {
                    WorkItemId = r.WorkItemId,
                    Identifier = r.Identifier,
                    Attempt = r.Attempt,
                    DueAtMs = r.DueAtMs,
                    Error = r.Error,
                }).ToList(),
                Totals = new AggregateMetrics
                {
                    InputTokens = _state.Totals.InputTokens,
                    OutputTokens = _state.Totals.OutputTokens,
                    TotalTokens = _state.Totals.TotalTokens,
                    SecondsRunning = _state.Totals.SecondsRunning + activeSeconds,
                },
            };
        }
    }

    /// <summary>
    /// Force an immediate poll + reconciliation cycle.
    /// </summary>
    public void TriggerRefresh()
    {
        _ = Task.Run(() => OnTickAsync(CancellationToken.None));
    }

    #region IOrchestratorSnapshotProvider

    string IOrchestratorSnapshotProvider.Name => "github-tracker";

    object IOrchestratorSnapshotProvider.GetSnapshot() => GetSnapshot();

    void IOrchestratorSnapshotProvider.TriggerRefresh() => TriggerRefresh();

    #endregion

    private async Task PollLoopAsync(CancellationToken ct)
    {
        // Immediate first tick
        await OnTickAsync(ct);

        while (_pollTimer != null && await _pollTimer.WaitForNextTickAsync(ct))
        {
            await OnTickAsync(ct);
        }
    }

    private async Task OnTickAsync(CancellationToken ct)
    {
        try
        {
            await ReconcileAsync(ct);

            var issueWorkflow = ReloadWorkflowIfAvailable(_workflowLoader, _issuesWorkflowPath);
            var pullRequestWorkflow = ReloadWorkflowIfAvailable(_prWorkflowLoader, _pullRequestWorkflowPath);
            var schedulerConfig = issueWorkflow?.Config ?? pullRequestWorkflow?.Config;
            if (schedulerConfig != null)
            {
                lock (_stateLock)
                {
                    _state.PollIntervalMs = schedulerConfig.Polling.IntervalMs;
                    _state.MaxConcurrentAgents = schedulerConfig.Agent.MaxConcurrentAgents;
                }
            }

            if (issueWorkflow == null && pullRequestWorkflow == null)
            {
                _logger.LogWarning("No valid GitHubTracker workflows are currently available, skipping dispatch");
                return;
            }

            IReadOnlyList<TrackedWorkItem> candidates;
            try
            {
                candidates = await _tracker.FetchCandidateWorkItemsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch candidates, skipping dispatch");
                return;
            }

            var sorted = DispatchSorter.Sort(candidates);

            foreach (var workItem in sorted)
            {
                if (ct.IsCancellationRequested) break;
                if (!IsTrackingEnabled(workItem.Kind)) continue;
                if (!HasAvailableSlots(workItem)) break;
                if (!ShouldDispatch(workItem, GetEffectiveConfig(workItem.Kind))) continue;

                var selectedWorkflow = SelectWorkflow(workItem.Kind);
                if (selectedWorkflow == null) continue;
                DispatchWorkItem(workItem, selectedWorkflow, attempt: null);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during poll tick");
        }
    }

    private async Task ReconcileAsync(CancellationToken ct)
    {
        List<RunningEntry> running;
        lock (_stateLock)
        {
            running = [.. _state.Running.Values];
        }

        if (running.Count == 0) return;

        // Stall detection
        foreach (var entry in running)
        {
            var stallTimeoutMs = GetEffectiveConfig(entry.WorkItem.Kind).Agent.StallTimeoutMs;
            if (stallTimeoutMs <= 0)
                continue;

            var lastActivity = entry.LastEventTimestamp ?? entry.StartedAt;
            var elapsed = (DateTimeOffset.UtcNow - lastActivity).TotalMilliseconds;

            if (elapsed > stallTimeoutMs)
            {
                _logger.LogWarning("Issue {Identifier} stalled (no activity for {Elapsed}ms), terminating",
                    entry.Identifier, (int)elapsed);
                TerminateRunning(entry.WorkItemId, cleanWorkspace: false);
                ScheduleRetry(entry.WorkItemId, entry.Identifier, entry.WorkItem.Kind,
                    (entry.RetryAttempt ?? 0) + 1, "stall timeout");
            }
        }

        // Tracker state refresh
        lock (_stateLock) { running = [.. _state.Running.Values]; }
        if (running.Count == 0) return;

        var ids = running.Select(r => r.WorkItemId).ToList();

        IReadOnlyList<WorkItemStateSnapshot> refreshed;
        try
        {
            refreshed = await _tracker.FetchWorkItemStatesByIdsAsync(ids, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "State refresh failed, keeping workers running");
            return;
        }

        var stateMap = refreshed.ToDictionary(s => s.Id, s => s.State);
        foreach (var entry in running)
        {
            if (!stateMap.TryGetValue(entry.WorkItemId, out var currentState)) continue;

            var cfg = GetEffectiveConfig(entry.WorkItem.Kind);
            var terminalStates = GetTerminalStatesForKind(entry.WorkItem.Kind, cfg);
            var activeStates = GetActiveStatesForKind(entry.WorkItem.Kind, cfg);

            var isTerminal = terminalStates.Any(t =>
                string.Equals(t.Trim(), currentState.Trim(), StringComparison.OrdinalIgnoreCase));
            var isActive = activeStates.Any(a =>
                string.Equals(a.Trim(), currentState.Trim(), StringComparison.OrdinalIgnoreCase));

            if (isTerminal)
            {
                _logger.LogInformation("{Identifier} reached terminal state {State}, stopping and cleaning",
                    entry.Identifier, currentState);
                TerminateRunning(entry.WorkItemId, cleanWorkspace: true);
                lock (_stateLock) { _state.Completed.Remove(entry.WorkItemId); }
            }
            else if (!isActive)
            {
                _logger.LogInformation("{Identifier} is no longer active (state: {State}), stopping",
                    entry.Identifier, currentState);
                TerminateRunning(entry.WorkItemId, cleanWorkspace: false);
            }
            else
            {
                lock (_stateLock)
                {
                    if (_state.Running.TryGetValue(entry.WorkItemId, out var re))
                    {
                        re.WorkItem = new TrackedWorkItem
                        {
                            Id = entry.WorkItem.Id,
                            Identifier = entry.WorkItem.Identifier,
                            Title = entry.WorkItem.Title,
                            Description = entry.WorkItem.Description,
                            Priority = entry.WorkItem.Priority,
                            State = currentState,
                            Kind = entry.WorkItem.Kind,
                            BranchName = entry.WorkItem.BranchName,
                            HeadBranch = entry.WorkItem.HeadBranch,
                            BaseBranch = entry.WorkItem.BaseBranch,
                            DiffUrl = entry.WorkItem.DiffUrl,
                            ReviewState = entry.WorkItem.ReviewState,
                            IsDraft = entry.WorkItem.IsDraft,
                            Url = entry.WorkItem.Url,
                            Labels = entry.WorkItem.Labels,
                            BlockedBy = entry.WorkItem.BlockedBy,
                            CreatedAt = entry.WorkItem.CreatedAt,
                            UpdatedAt = entry.WorkItem.UpdatedAt,
                        };
                    }
                }
            }
        }
    }

    private bool ShouldDispatch(TrackedWorkItem workItem, GitHubTrackerConfig config)
    {
        lock (_stateLock)
        {
            if (_state.Running.ContainsKey(workItem.Id)) return false;
            if (_state.Claimed.Contains(workItem.Id)) return false;
            if (_state.Completed.Contains(workItem.Id)) return false;

            if (string.Equals(workItem.State.Trim(), "todo", StringComparison.OrdinalIgnoreCase))
            {
                var terminalStates = config.Tracker.TerminalStates
                    .Select(s => s.Trim().ToLowerInvariant()).ToHashSet();

                foreach (var blocker in workItem.BlockedBy)
                {
                    if (blocker.State != null && !terminalStates.Contains(blocker.State.Trim().ToLowerInvariant()))
                        return false;
                }
            }

            return true;
        }
    }

    private bool HasAvailableSlots(TrackedWorkItem workItem)
    {
        lock (_stateLock)
        {
            var cfg = GetEffectiveConfig(workItem.Kind);
            if (_state.Running.Count >= _state.MaxConcurrentAgents) return false;

            if (workItem.Kind == WorkItemKind.PullRequest)
            {
                var prLimit = cfg.Agent.MaxConcurrentPullRequestAgents;
                if (prLimit > 0)
                {
                    var prCount = _state.Running.Values.Count(r => r.WorkItem.Kind == WorkItemKind.PullRequest);
                    if (prCount >= prLimit) return false;
                }
            }

            if (cfg.Agent.MaxConcurrentByState is { Count: > 0 } byState)
            {
                var normalizedState = workItem.State.Trim().ToLowerInvariant();
                if (byState.TryGetValue(normalizedState, out var limit) && limit > 0)
                {
                    var count = _state.Running.Values
                        .Count(r => string.Equals(r.WorkItem.State.Trim(), workItem.State.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (count >= limit) return false;
                }
            }

            return true;
        }
    }

    private void DispatchWorkItem(TrackedWorkItem workItem, WorkflowDefinition workflow, int? attempt)
    {
        var cts = new CancellationTokenSource();
        var workItemId = workItem.Id;

        var workerTask = Task.Run(async () =>
        {
            try
            {
                var outcome = await _agentRunnerFactory.RunAsync(
                    workItem, workflow, attempt, cts.Token,
                    onTurnCompleted: (turn, input, output, total) =>
                    {
                        lock (_stateLock)
                        {
                            if (_state.Running.TryGetValue(workItemId, out var entry))
                            {
                                entry.TurnCount = turn;
                                entry.InputTokens = input;
                                entry.OutputTokens = output;
                                entry.TotalTokens = total;
                                entry.LastEventTimestamp = DateTimeOffset.UtcNow;
                            }
                        }
                    });
                await OnWorkerExitAsync(workItemId, workItem, WorkerExitReason.Normal, attempt, outcome);
            }
            catch (OperationCanceledException)
            {
                await OnWorkerExitAsync(workItemId, workItem, WorkerExitReason.Cancelled, attempt, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker for {Identifier} failed", workItem.Identifier);
                await OnWorkerExitAsync(workItemId, workItem, WorkerExitReason.Failed, attempt, null);
            }
        }, CancellationToken.None);

        lock (_stateLock)
        {
            _state.Claimed.Add(workItemId);
            _state.RetryAttempts.Remove(workItemId);
            _state.Running[workItemId] = new RunningEntry
            {
                WorkItemId = workItemId,
                Identifier = workItem.Identifier,
                WorkItem = workItem,
                StartedAt = DateTimeOffset.UtcNow,
                Cts = cts,
                WorkerTask = workerTask,
                RetryAttempt = attempt,
                SessionId = WorkItemAgentRunnerFactory.GetSessionKey(workItem.Identifier),
            };
        }

        _logger.LogInformation("Dispatched work item {Identifier} (attempt: {Attempt})",
            workItem.Identifier, attempt?.ToString() ?? "initial");
    }

    private async Task OnWorkerExitAsync(string workItemId, TrackedWorkItem workItem, WorkerExitReason reason, int? attempt, AgentRunOutcome? outcome)
    {
        lock (_stateLock)
        {
            if (_state.Running.TryGetValue(workItemId, out var entry))
            {
                _state.Totals.SecondsRunning += (DateTimeOffset.UtcNow - entry.StartedAt).TotalSeconds;
                _state.Totals.InputTokens += entry.InputTokens;
                _state.Totals.OutputTokens += entry.OutputTokens;
                _state.Totals.TotalTokens += entry.TotalTokens;
            }
            _state.Running.Remove(workItemId);
        }

        switch (reason)
        {
            case WorkerExitReason.Normal:
                lock (_stateLock) { _state.Completed.Add(workItemId); }
                _logger.LogInformation(
                    "Work item {Identifier} run complete (result: {Result}, turns: {Turns}, tokens: {Total}), scheduling continuation check",
                    workItem.Identifier, outcome?.Result, outcome?.TurnsCompleted, outcome?.TotalTokens);

                var runConfig = GetEffectiveConfig(workItem.Kind);
                if (workItem.Kind == WorkItemKind.PullRequest
                    && !string.IsNullOrEmpty(runConfig.Tracker.PullRequestLabelFilter))
                {
                    var labelRemoved = false;
                    for (var i = 0; i < 3 && !labelRemoved; i++)
                    {
                        try
                        {
                            if (i > 0) await Task.Delay(1000 * i);
                            await _tracker.RemoveLabelAsync(workItemId, runConfig.Tracker.PullRequestLabelFilter);
                            labelRemoved = true;
                            _logger.LogInformation(
                                "Removed label '{Label}' from PR {Identifier} after review",
                                runConfig.Tracker.PullRequestLabelFilter, workItem.Identifier);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "Attempt {Attempt} to remove label '{Label}' from PR {Identifier} failed",
                                i + 1, runConfig.Tracker.PullRequestLabelFilter, workItem.Identifier);
                        }
                    }

                    if (!labelRemoved)
                        _logger.LogError(
                            "All attempts to remove label '{Label}' from PR {Identifier} exhausted; PR may be re-dispatched",
                            runConfig.Tracker.PullRequestLabelFilter, workItem.Identifier);
                }

                var skipContinuation = workItem.Kind == WorkItemKind.PullRequest
                    && outcome?.ReviewSubmitted == true;

                if (!skipContinuation)
                    ScheduleRetry(workItemId, workItem.Identifier, workItem.Kind, 1, null);
                else
                    lock (_stateLock) { _state.Claimed.Remove(workItemId); }
                break;

            case WorkerExitReason.Failed:
                var nextAttempt = (attempt ?? 0) + 1;
                ScheduleRetry(workItemId, workItem.Identifier, workItem.Kind, nextAttempt, "worker failed");
                break;

            case WorkerExitReason.Cancelled:
                lock (_stateLock) { _state.Claimed.Remove(workItemId); }
                break;
        }
    }

    private void ScheduleRetry(string workItemId, string identifier, WorkItemKind kind, int attempt, string? error)
    {
        var maxBackoff = GetEffectiveConfig(kind).Agent.MaxRetryBackoffMs;

        var delayMs = error == null
            ? RetryQueue.ContinuationDelayMs
            : RetryQueue.ComputeBackoffDelayMs(attempt, maxBackoff);

        var dueAt = RetryQueue.NowMs + delayMs;

        var retryCts = new CancellationTokenSource();

        lock (_stateLock)
        {
            if (_state.RetryAttempts.TryGetValue(workItemId, out var existing))
                existing.TimerCts?.Cancel();

            _state.RetryAttempts[workItemId] = new RetryEntry
            {
                WorkItemId = workItemId,
                Identifier = identifier,
                Attempt = attempt,
                DueAtMs = dueAt,
                Error = error,
                TimerCts = retryCts,
            };
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, retryCts.Token);
                await HandleRetryAsync(workItemId, attempt);
            }
            catch (OperationCanceledException) { }
        });

        _logger.LogDebug("Scheduled retry for {Identifier} attempt {Attempt} in {Delay}ms",
            identifier, attempt, delayMs);
    }

    private async Task HandleRetryAsync(string workItemId, int attempt)
    {
        lock (_stateLock)
        {
            _state.RetryAttempts.Remove(workItemId);
        }

        try
        {
            var candidates = await _tracker.FetchCandidateWorkItemsAsync();
            var workItem = candidates.FirstOrDefault(c => c.Id == workItemId);

            if (workItem == null)
            {
                lock (_stateLock) { _state.Claimed.Remove(workItemId); }
                return;
            }

            var workflow = SelectWorkflow(workItem.Kind);
            if (workflow == null)
            {
                lock (_stateLock) { _state.Claimed.Remove(workItemId); }
                return;
            }

            if (!HasAvailableSlots(workItem))
            {
                ScheduleRetry(workItemId, workItem.Identifier, workItem.Kind, attempt + 1, "no available orchestrator slots");
                return;
            }

            DispatchWorkItem(workItem, workflow, attempt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Retry handling failed for work item {WorkItemId}", workItemId);
            lock (_stateLock) { _state.Claimed.Remove(workItemId); }
        }
    }

    private void TerminateRunning(string workItemId, bool cleanWorkspace)
    {
        RunningEntry? entry;
        lock (_stateLock)
        {
            _state.Running.Remove(workItemId, out entry);
            _state.Claimed.Remove(workItemId);
        }

        if (entry == null) return;

        entry.Cts.Cancel();

        if (cleanWorkspace)
        {
            var cleanupTask = _workspaceManager.CleanWorkspaceAsync(entry.Identifier);
            lock (_stateLock) { _pendingCleanups.Add(cleanupTask); }
            _ = cleanupTask.ContinueWith(_ =>
            {
                lock (_stateLock) { _pendingCleanups.Remove(cleanupTask); }
            }, TaskScheduler.Default);
        }
    }

    private async Task StartupCleanupAsync(CancellationToken ct)
    {
        var terminalStates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var issueWorkflow = GetIssueWorkflow();
        var pullRequestWorkflow = GetPullRequestWorkflow();

        if (issueWorkflow != null)
        {
            foreach (var state in issueWorkflow.Config.Tracker.TerminalStates)
                terminalStates.Add(state);
        }

        if (pullRequestWorkflow != null)
        {
            foreach (var state in pullRequestWorkflow.Config.Tracker.PullRequestTerminalStates)
                terminalStates.Add(state);
        }

        if (terminalStates.Count == 0)
            return;

        try
        {
            var terminalItems = await _tracker.FetchWorkItemsByStatesAsync([.. terminalStates], ct);
            foreach (var item in terminalItems)
            {
                await _workspaceManager.CleanWorkspaceAsync(item.Identifier, ct);
            }
            _logger.LogInformation("Startup cleanup: processed {Count} terminal workspaces", terminalItems.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Startup terminal cleanup failed, continuing");
        }
    }

    /// <summary>
    /// Select the appropriate workflow definition for a work-item kind.
    /// </summary>
    private WorkflowDefinition? SelectWorkflow(WorkItemKind kind) =>
        kind == WorkItemKind.PullRequest
            ? GetPullRequestWorkflow()
            : GetIssueWorkflow();

    private bool IsTrackingEnabled(WorkItemKind kind) => SelectWorkflow(kind) != null;

    private GitHubTrackerConfig GetEffectiveConfig(WorkItemKind kind) =>
        SelectWorkflow(kind)?.Config ?? _config;

    private WorkflowDefinition? GetIssueWorkflow() =>
        WorkflowFileExists(_issuesWorkflowPath) ? _workflowLoader.Current : null;

    private WorkflowDefinition? GetPullRequestWorkflow() =>
        WorkflowFileExists(_pullRequestWorkflowPath) ? _prWorkflowLoader.Current : null;

    private WorkflowDefinition? LoadWorkflowIfAvailable(WorkflowLoader loader, string? path, string workflowName)
    {
        if (!WorkflowFileExists(path))
            return null;

        var workflow = loader.Load(path!);
        _logger.LogInformation("Loaded {WorkflowName} workflow from {Path}", workflowName, path);
        return workflow;
    }

    private WorkflowDefinition? ReloadWorkflowIfAvailable(WorkflowLoader loader, string? path)
    {
        if (!WorkflowFileExists(path))
            return null;

        return loader.Reload() ?? loader.Current;
    }

    private static string? ResolveWorkflowPath(string? configuredPath, string workspacePath) =>
        string.IsNullOrWhiteSpace(configuredPath)
            ? null
            : Path.GetFullPath(configuredPath, workspacePath);

    private static bool WorkflowFileExists(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private static List<string> GetActiveStatesForKind(WorkItemKind kind, GitHubTrackerConfig cfg) =>
        kind == WorkItemKind.PullRequest
            ? cfg.Tracker.PullRequestActiveStates
            : cfg.Tracker.ActiveStates;

    private static List<string> GetTerminalStatesForKind(WorkItemKind kind, GitHubTrackerConfig cfg) =>
        kind == WorkItemKind.PullRequest
            ? cfg.Tracker.PullRequestTerminalStates
            : cfg.Tracker.TerminalStates;

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _pollTimer?.Dispose();
        _cts?.Dispose();
    }

    private enum WorkerExitReason { Normal, Failed, Cancelled }
}
