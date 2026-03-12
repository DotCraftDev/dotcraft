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
    private readonly IIssueTracker _tracker;
    private readonly WorkflowLoader _workflowLoader;
    private readonly WorkflowLoader _prWorkflowLoader;
    private readonly IssueWorkspaceManager _workspaceManager;
    private readonly IssueAgentRunnerFactory _agentRunnerFactory;
    private readonly GitHubTrackerConfig _config;
    private readonly ILogger<GitHubTrackerOrchestrator> _logger;
    private readonly OrchestratorState _state = new();
    private readonly Lock _stateLock = new();
    private readonly List<Task> _pendingCleanups = [];
    private readonly string _workflowPath;
    private readonly string _prWorkflowPath;

    private PeriodicTimer? _pollTimer;
    private CancellationTokenSource? _cts;
    private Task? _pollTask;

    public GitHubTrackerOrchestrator(
        IIssueTracker tracker,
        WorkflowLoader workflowLoader,
        WorkflowLoader prWorkflowLoader,
        IssueWorkspaceManager workspaceManager,
        IssueAgentRunnerFactory agentRunnerFactory,
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
        _workflowPath = Path.GetFullPath(config.WorkflowPath, workspacePath);
        _prWorkflowPath = Path.GetFullPath(config.PullRequestWorkflowPath, workspacePath);

        _state.PollIntervalMs = config.Polling.IntervalMs;
        _state.MaxConcurrentAgents = config.Agent.MaxConcurrentAgents;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("GitHubTracker orchestrator starting");

        var workflow = _workflowLoader.Load(_workflowPath);
        _state.PollIntervalMs = workflow.Config.Polling.IntervalMs;
        _state.MaxConcurrentAgents = workflow.Config.Agent.MaxConcurrentAgents;

        if (_config.Tracker.TrackPullRequests && File.Exists(_prWorkflowPath))
        {
            _prWorkflowLoader.Load(_prWorkflowPath);
            _logger.LogInformation("Loaded PR review workflow from {Path}", _prWorkflowPath);
        }

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
                Running = _state.Running.Values.Select(r => new RunningIssueSummary
                {
                    IssueId = r.IssueId,
                    Identifier = r.Identifier,
                    State = r.Issue.State,
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
                Retrying = _state.RetryAttempts.Values.Select(r => new RetryIssueSummary
                {
                    IssueId = r.IssueId,
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

            var workflow = _workflowLoader.Reload() ?? _workflowLoader.Current;
            if (workflow != null)
            {
                lock (_stateLock)
                {
                    _state.PollIntervalMs = workflow.Config.Polling.IntervalMs;
                    _state.MaxConcurrentAgents = workflow.Config.Agent.MaxConcurrentAgents;
                }
            }

            if (_config.Tracker.TrackPullRequests && File.Exists(_prWorkflowPath))
                _prWorkflowLoader.Reload();

            if (workflow == null)
            {
                _logger.LogWarning("No valid workflow loaded, skipping dispatch");
                return;
            }

            IReadOnlyList<TrackedIssue> candidates;
            try
            {
                candidates = await _tracker.FetchCandidateIssuesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch candidates, skipping dispatch");
                return;
            }

            var sorted = DispatchSorter.Sort(candidates);

            foreach (var issue in sorted)
            {
                if (ct.IsCancellationRequested) break;
                if (!HasAvailableSlots(issue)) break;
                if (!ShouldDispatch(issue, workflow.Config)) continue;

                var selectedWorkflow = SelectWorkflow(issue.Kind) ?? workflow;
                DispatchIssue(issue, selectedWorkflow, attempt: null);
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
        var workflow = _workflowLoader.Current;
        var stallTimeoutMs = workflow?.Config.Agent.StallTimeoutMs ?? 300_000;

        if (stallTimeoutMs > 0)
        {
            foreach (var entry in running)
            {
                var lastActivity = entry.LastEventTimestamp ?? entry.StartedAt;
                var elapsed = (DateTimeOffset.UtcNow - lastActivity).TotalMilliseconds;

                if (elapsed > stallTimeoutMs)
                {
                    _logger.LogWarning("Issue {Identifier} stalled (no activity for {Elapsed}ms), terminating",
                        entry.Identifier, (int)elapsed);
                    TerminateRunning(entry.IssueId, cleanWorkspace: false);
                    ScheduleRetry(entry.IssueId, entry.Identifier,
                        (entry.RetryAttempt ?? 0) + 1, "stall timeout");
                }
            }
        }

        // Tracker state refresh
        lock (_stateLock) { running = [.. _state.Running.Values]; }
        if (running.Count == 0) return;

        var ids = running.Select(r => r.IssueId).ToList();

        IReadOnlyList<IssueStateSnapshot> refreshed;
        try
        {
            refreshed = await _tracker.FetchIssueStatesByIdsAsync(ids, ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "State refresh failed, keeping workers running");
            return;
        }

        var stateMap = refreshed.ToDictionary(s => s.Id, s => s.State);
        var cfg = workflow?.Config ?? _config;

        foreach (var entry in running)
        {
            if (!stateMap.TryGetValue(entry.IssueId, out var currentState)) continue;

            var terminalStates = GetTerminalStatesForKind(entry.Issue.Kind, cfg);
            var activeStates = GetActiveStatesForKind(entry.Issue.Kind, cfg);

            var isTerminal = terminalStates.Any(t =>
                string.Equals(t.Trim(), currentState.Trim(), StringComparison.OrdinalIgnoreCase));
            var isActive = activeStates.Any(a =>
                string.Equals(a.Trim(), currentState.Trim(), StringComparison.OrdinalIgnoreCase));

            if (isTerminal)
            {
                _logger.LogInformation("{Identifier} reached terminal state {State}, stopping and cleaning",
                    entry.Identifier, currentState);
                TerminateRunning(entry.IssueId, cleanWorkspace: true);
                lock (_stateLock) { _state.Completed.Remove(entry.IssueId); }
            }
            else if (!isActive)
            {
                _logger.LogInformation("{Identifier} is no longer active (state: {State}), stopping",
                    entry.Identifier, currentState);
                TerminateRunning(entry.IssueId, cleanWorkspace: false);
            }
            else
            {
                lock (_stateLock)
                {
                    if (_state.Running.TryGetValue(entry.IssueId, out var re))
                    {
                        re.Issue = new TrackedIssue
                        {
                            Id = entry.Issue.Id,
                            Identifier = entry.Issue.Identifier,
                            Title = entry.Issue.Title,
                            Description = entry.Issue.Description,
                            Priority = entry.Issue.Priority,
                            State = currentState,
                            Kind = entry.Issue.Kind,
                            BranchName = entry.Issue.BranchName,
                            HeadBranch = entry.Issue.HeadBranch,
                            BaseBranch = entry.Issue.BaseBranch,
                            DiffUrl = entry.Issue.DiffUrl,
                            ReviewState = entry.Issue.ReviewState,
                            IsDraft = entry.Issue.IsDraft,
                            Url = entry.Issue.Url,
                            Labels = entry.Issue.Labels,
                            BlockedBy = entry.Issue.BlockedBy,
                            CreatedAt = entry.Issue.CreatedAt,
                            UpdatedAt = entry.Issue.UpdatedAt,
                        };
                    }
                }
            }
        }
    }

    private bool ShouldDispatch(TrackedIssue issue, GitHubTrackerConfig config)
    {
        lock (_stateLock)
        {
            if (_state.Running.ContainsKey(issue.Id)) return false;
            if (_state.Claimed.Contains(issue.Id)) return false;

            // Blocker rule: Todo issues with non-terminal blockers are ineligible
            if (string.Equals(issue.State.Trim(), "todo", StringComparison.OrdinalIgnoreCase))
            {
                var terminalStates = config.Tracker.TerminalStates
                    .Select(s => s.Trim().ToLowerInvariant()).ToHashSet();

                foreach (var blocker in issue.BlockedBy)
                {
                    if (blocker.State != null && !terminalStates.Contains(blocker.State.Trim().ToLowerInvariant()))
                        return false;
                }
            }

            return true;
        }
    }

    private bool HasAvailableSlots(TrackedIssue issue)
    {
        lock (_stateLock)
        {
            if (_state.Running.Count >= _state.MaxConcurrentAgents) return false;

            // Per-kind concurrency limit for pull requests
            if (issue.Kind == WorkItemKind.PullRequest)
            {
                var prLimit = _config.Agent.MaxConcurrentPullRequestAgents;
                if (prLimit > 0)
                {
                    var prCount = _state.Running.Values.Count(r => r.Issue.Kind == WorkItemKind.PullRequest);
                    if (prCount >= prLimit) return false;
                }
            }

            var workflow = _workflowLoader.Current;
            if (workflow?.Config.Agent.MaxConcurrentByState is { Count: > 0 } byState)
            {
                var normalizedState = issue.State.Trim().ToLowerInvariant();
                if (byState.TryGetValue(normalizedState, out var limit) && limit > 0)
                {
                    var count = _state.Running.Values
                        .Count(r => string.Equals(r.Issue.State.Trim(), issue.State.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (count >= limit) return false;
                }
            }

            return true;
        }
    }

    private void DispatchIssue(TrackedIssue issue, WorkflowDefinition workflow, int? attempt)
    {
        var cts = new CancellationTokenSource();
        var issueId = issue.Id;

        var workerTask = Task.Run(async () =>
        {
            try
            {
                var outcome = await _agentRunnerFactory.RunAsync(
                    issue, workflow, attempt, cts.Token,
                    onTurnCompleted: (turn, input, output, total) =>
                    {
                        lock (_stateLock)
                        {
                            if (_state.Running.TryGetValue(issueId, out var entry))
                            {
                                entry.TurnCount = turn;
                                entry.InputTokens = input;
                                entry.OutputTokens = output;
                                entry.TotalTokens = total;
                                entry.LastEventTimestamp = DateTimeOffset.UtcNow;
                            }
                        }
                    });
                await OnWorkerExitAsync(issueId, issue, WorkerExitReason.Normal, attempt, outcome);
            }
            catch (OperationCanceledException)
            {
                await OnWorkerExitAsync(issueId, issue, WorkerExitReason.Cancelled, attempt, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker for {Identifier} failed", issue.Identifier);
                await OnWorkerExitAsync(issueId, issue, WorkerExitReason.Failed, attempt, null);
            }
        }, CancellationToken.None);

        lock (_stateLock)
        {
            _state.Claimed.Add(issueId);
            _state.RetryAttempts.Remove(issueId);
            _state.Running[issueId] = new RunningEntry
            {
                IssueId = issueId,
                Identifier = issue.Identifier,
                Issue = issue,
                StartedAt = DateTimeOffset.UtcNow,
                Cts = cts,
                WorkerTask = workerTask,
                RetryAttempt = attempt,
                SessionId = IssueAgentRunnerFactory.GetSessionKey(issue.Identifier),
            };
        }

        _logger.LogInformation("Dispatched issue {Identifier} (attempt: {Attempt})",
            issue.Identifier, attempt?.ToString() ?? "initial");
    }

    private async Task OnWorkerExitAsync(string issueId, TrackedIssue issue, WorkerExitReason reason, int? attempt, AgentRunOutcome? outcome)
    {
        lock (_stateLock)
        {
            if (_state.Running.TryGetValue(issueId, out var entry))
            {
                _state.Totals.SecondsRunning += (DateTimeOffset.UtcNow - entry.StartedAt).TotalSeconds;
                _state.Totals.InputTokens += entry.InputTokens;
                _state.Totals.OutputTokens += entry.OutputTokens;
                _state.Totals.TotalTokens += entry.TotalTokens;
            }
            _state.Running.Remove(issueId);
        }

        switch (reason)
        {
            case WorkerExitReason.Normal:
                // Per SPEC §16.6: add to Completed for bookkeeping, then schedule a short
                // continuation retry (1s) to re-check whether the issue is still active.
                // The retry fires, re-fetches candidates, and re-dispatches only if the issue
                // is still in an active state. The loop stops when the issue is closed/relabeled.
                lock (_stateLock) { _state.Completed.Add(issueId); }
                _logger.LogInformation(
                    "Issue {Identifier} run complete (result: {Result}, turns: {Turns}, tokens: {Total}), scheduling continuation check",
                    issue.Identifier, outcome?.Result, outcome?.TurnsCompleted, outcome?.TotalTokens);

                // For PR reviews with a label filter: remove the dispatch label so the continuation
                // retry's candidate fetch naturally excludes this PR, preventing re-dispatch.
                // This mirrors how CompleteIssue removes the issue from candidates for Issue work items.
                if (issue.Kind == WorkItemKind.PullRequest
                    && !string.IsNullOrEmpty(_config.Tracker.PullRequestLabelFilter))
                {
                    try
                    {
                        await _tracker.RemoveLabelAsync(issueId, _config.Tracker.PullRequestLabelFilter);
                        _logger.LogInformation(
                            "Removed label '{Label}' from PR {Identifier} after review",
                            _config.Tracker.PullRequestLabelFilter, issue.Identifier);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to remove label '{Label}' from PR {Identifier}; PR may be re-dispatched on next poll",
                            _config.Tracker.PullRequestLabelFilter, issue.Identifier);
                    }
                }

                ScheduleRetry(issueId, issue.Identifier, 1, null);
                break;

            case WorkerExitReason.Failed:
                var nextAttempt = (attempt ?? 0) + 1;
                ScheduleRetry(issueId, issue.Identifier, nextAttempt, "worker failed");
                break;

            case WorkerExitReason.Cancelled:
                lock (_stateLock) { _state.Claimed.Remove(issueId); }
                break;
        }
    }

    private void ScheduleRetry(string issueId, string identifier, int attempt, string? error)
    {
        var workflow = _workflowLoader.Current;
        var maxBackoff = workflow?.Config.Agent.MaxRetryBackoffMs ?? 300_000;

        var delayMs = error == null
            ? RetryQueue.ContinuationDelayMs
            : RetryQueue.ComputeBackoffDelayMs(attempt, maxBackoff);

        var dueAt = RetryQueue.NowMs + delayMs;

        var retryCts = new CancellationTokenSource();

        lock (_stateLock)
        {
            // Cancel existing retry for same issue
            if (_state.RetryAttempts.TryGetValue(issueId, out var existing))
                existing.TimerCts?.Cancel();

            _state.RetryAttempts[issueId] = new RetryEntry
            {
                IssueId = issueId,
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
                await HandleRetryAsync(issueId, attempt);
            }
            catch (OperationCanceledException) { }
        });

        _logger.LogDebug("Scheduled retry for {Identifier} attempt {Attempt} in {Delay}ms",
            identifier, attempt, delayMs);
    }

    private async Task HandleRetryAsync(string issueId, int attempt)
    {
        lock (_stateLock)
        {
            _state.RetryAttempts.Remove(issueId);
        }

        try
        {
            var candidates = await _tracker.FetchCandidateIssuesAsync();
            var issue = candidates.FirstOrDefault(c => c.Id == issueId);

            if (issue == null)
            {
                lock (_stateLock) { _state.Claimed.Remove(issueId); }
                return;
            }

            var workflow = SelectWorkflow(issue.Kind) ?? _workflowLoader.Current;
            if (workflow == null)
            {
                lock (_stateLock) { _state.Claimed.Remove(issueId); }
                return;
            }

            if (!HasAvailableSlots(issue))
            {
                ScheduleRetry(issueId, issue.Identifier, attempt + 1, "no available orchestrator slots");
                return;
            }

            DispatchIssue(issue, workflow, attempt);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Retry handling failed for issue {IssueId}", issueId);
            lock (_stateLock) { _state.Claimed.Remove(issueId); }
        }
    }

    private void TerminateRunning(string issueId, bool cleanWorkspace)
    {
        RunningEntry? entry;
        lock (_stateLock)
        {
            _state.Running.Remove(issueId, out entry);
            _state.Claimed.Remove(issueId);
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
        var workflow = _workflowLoader.Current;
        var cfg = workflow?.Config ?? _config;

        // Combine issue and PR terminal states for workspace cleanup.
        var terminalStates = cfg.Tracker.TerminalStates
            .Concat(cfg.Tracker.PullRequestTerminalStates)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        try
        {
            var terminalItems = await _tracker.FetchIssuesByStatesAsync(terminalStates, ct);
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
    /// Falls back to the issue workflow if no PR workflow is loaded.
    /// </summary>
    private WorkflowDefinition? SelectWorkflow(WorkItemKind kind) =>
        kind == WorkItemKind.PullRequest
            ? (_prWorkflowLoader.Current ?? _workflowLoader.Current)
            : _workflowLoader.Current;

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
