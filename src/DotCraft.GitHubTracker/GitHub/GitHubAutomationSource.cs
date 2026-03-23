using System.Collections.Concurrent;
using System.ComponentModel;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Automations.Abstractions;
using DotCraft.GitHubTracker.Tracker;
using DotCraft.GitHubTracker.Workflow;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.GitHubTracker.GitHub;

/// <summary>
/// <see cref="IAutomationSource"/> adapter for GitHub issues and pull requests.
/// Replaces the standalone <c>GitHubTrackerOrchestrator</c> by plugging into
/// <c>AutomationOrchestrator</c>.
/// </summary>
public sealed class GitHubAutomationSource : IAutomationSource
{
    private readonly IWorkItemTracker _tracker;
    private readonly WorkflowLoader _issueWorkflowLoader;
    private readonly WorkflowLoader _prWorkflowLoader;
    private readonly GitHubTrackerConfig _config;
    private readonly string _workspacePath;
    private readonly ILogger<GitHubAutomationSource> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private readonly ConcurrentDictionary<string, GitHubAutomationTask> _activeTasks = new();
    private readonly ConcurrentDictionary<string, string> _reviewedSha = new();
    private readonly ConcurrentDictionary<string, bool> _reviewCompleted = new();
    private readonly ConcurrentDictionary<string, GitHubAutomationTask> _completedTasks = new();

    private readonly string? _issuesWorkflowPath;
    private readonly string? _prWorkflowPath;

    public GitHubAutomationSource(
        IWorkItemTracker tracker,
        WorkflowLoader issueWorkflowLoader,
        WorkflowLoader prWorkflowLoader,
        GitHubTrackerConfig config,
        string workspacePath,
        ILoggerFactory loggerFactory)
    {
        _tracker = tracker;
        _issueWorkflowLoader = issueWorkflowLoader;
        _prWorkflowLoader = prWorkflowLoader;
        _config = config;
        _workspacePath = workspacePath;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<GitHubAutomationSource>();

        _issuesWorkflowPath = ResolveWorkflowPath(config.IssuesWorkflowPath);
        _prWorkflowPath = ResolveWorkflowPath(config.PullRequestWorkflowPath);
    }

    /// <inheritdoc />
    public string Name => "github";

    /// <inheritdoc />
    public string ToolProfileName => "github-issue";

    /// <inheritdoc />
    public void RegisterToolProfile(IToolProfileRegistry registry)
    {
        var issueProvider = new GitHubTaskToolProvider(
            WorkItemKind.Issue, _activeTasks, _tracker, _reviewCompleted,
            _loggerFactory.CreateLogger<GitHubTaskToolProvider>());

        var prProvider = new GitHubTaskToolProvider(
            WorkItemKind.PullRequest, _activeTasks, _tracker, _reviewCompleted,
            _loggerFactory.CreateLogger<GitHubTaskToolProvider>());

        registry.Register("github-issue", [new CoreToolProvider(), issueProvider]);
        registry.Register("github-pr", [new CoreToolProvider(), prProvider]);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AutomationTask>> GetPendingTasksAsync(CancellationToken ct)
    {
        var candidates = await _tracker.FetchCandidateWorkItemsAsync(ct);
        var result = new List<AutomationTask>();
        var repo = _config.Tracker.Repository ?? "";

        foreach (var workItem in candidates)
        {
            if (!IsTrackingEnabled(workItem.Kind))
                continue;

            if (workItem.Kind == WorkItemKind.PullRequest)
            {
                if (_reviewedSha.TryGetValue(workItem.Id, out var sha) && sha == workItem.HeadSha)
                    continue;
            }

            var profileOverride = workItem.Kind == WorkItemKind.PullRequest
                ? "github-pr"
                : "github-issue";

            var task = GitHubAutomationTask.FromWorkItem(workItem, repo, profileOverride);
            result.Add(task);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AutomationTask>> GetAllTasksAsync(CancellationToken ct)
    {
        var merged = new Dictionary<string, AutomationTask>(StringComparer.Ordinal);

        foreach (var kv in _completedTasks)
            merged[kv.Key] = kv.Value;
        foreach (var kv in _activeTasks)
            merged[kv.Key] = kv.Value;

        var pending = await GetPendingTasksAsync(ct);
        foreach (var t in pending)
            merged.TryAdd(t.Id, t);

        return merged.Values.ToList();
    }

    /// <inheritdoc />
    public async Task<AutomationWorkflowDefinition> GetWorkflowAsync(AutomationTask task, CancellationToken ct)
    {
        var gh = (GitHubAutomationTask)task;
        var loader = gh.Kind == WorkItemKind.PullRequest ? _prWorkflowLoader : _issueWorkflowLoader;
        var path = gh.Kind == WorkItemKind.PullRequest ? _prWorkflowPath : _issuesWorkflowPath;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new InvalidOperationException(
                $"Workflow file not found for {gh.Kind}: {path}");

        var workflow = loader.Load(path);

        string? prDiff = null;
        if (gh.Kind == WorkItemKind.PullRequest)
        {
            try
            {
                prDiff = await _tracker.FetchPullRequestDiffAsync(gh.WorkItem.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch diff for PR {Identifier}", gh.WorkItem.Identifier);
            }
        }

        var workItemData = BuildWorkItemData(gh.WorkItem, prDiff);
        var prompt = loader.RenderPrompt(workflow.PromptTemplate, workItemData, attempt: null);

        return new AutomationWorkflowDefinition
        {
            Steps = [new WorkflowStep { Prompt = prompt }],
            MaxRounds = workflow.Config.Agent.MaxTurns,
        };
    }

    /// <inheritdoc />
    public Task OnStatusChangedAsync(AutomationTask task, AutomationTaskStatus newStatus, CancellationToken ct)
    {
        var gh = (GitHubAutomationTask)task;
        task.Status = newStatus;

        switch (newStatus)
        {
            case AutomationTaskStatus.Dispatched:
            case AutomationTaskStatus.AgentRunning:
                _activeTasks[task.Id] = gh;
                break;

            case AutomationTaskStatus.AwaitingReview:
                _activeTasks.TryRemove(task.Id, out _);
                _completedTasks[task.Id] = gh;
                break;

            case AutomationTaskStatus.Approved:
            case AutomationTaskStatus.Rejected:
            case AutomationTaskStatus.Failed:
                _activeTasks.TryRemove(task.Id, out _);
                _completedTasks[task.Id] = gh;
                _reviewCompleted.TryRemove(task.Id, out _);
                break;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnAgentCompletedAsync(AutomationTask task, string agentSummary, CancellationToken ct)
    {
        var gh = (GitHubAutomationTask)task;
        task.AgentSummary = agentSummary;

        if (gh.Kind == WorkItemKind.PullRequest && gh.WorkItem.HeadSha != null)
        {
            _reviewedSha[task.Id] = gh.WorkItem.HeadSha;
            gh.ReviewedAtSha = gh.WorkItem.HeadSha;
            _logger.LogInformation(
                "Recorded ReviewedSha for PR {Identifier} at {Sha}",
                gh.WorkItem.Identifier, gh.WorkItem.HeadSha);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> ShouldStopWorkflowAfterTurnAsync(AutomationTask task, CancellationToken ct)
    {
        var gh = (GitHubAutomationTask)task;

        if (gh.Kind == WorkItemKind.PullRequest)
        {
            if (_reviewCompleted.TryGetValue(task.Id, out var completed) && completed)
            {
                _logger.LogInformation("PR {Identifier} review submitted, stopping workflow", gh.WorkItem.Identifier);
                return true;
            }
        }

        try
        {
            var states = await _tracker.FetchWorkItemStatesByIdsAsync([gh.WorkItem.Id], ct);
            if (states.Count > 0)
            {
                var activeStates = GetActiveStatesForKind(gh.Kind);
                var isStillActive = activeStates.Any(a =>
                    string.Equals(a.Trim(), states[0].State.Trim(), StringComparison.OrdinalIgnoreCase));
                if (!isStillActive)
                {
                    _logger.LogInformation(
                        "{Identifier} is no longer active (state: {State}), stopping workflow",
                        gh.WorkItem.Identifier, states[0].State);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check state for {Identifier}, stopping workflow", gh.WorkItem.Identifier);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public Task ApproveTaskAsync(string taskId, CancellationToken ct)
    {
        _logger.LogInformation("GitHub task {TaskId} approved (no-op: human reviews via Desktop)", taskId);
        if (_completedTasks.TryGetValue(taskId, out var gh))
            gh.Status = AutomationTaskStatus.Approved;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RejectTaskAsync(string taskId, string? reason, CancellationToken ct)
    {
        _logger.LogInformation("GitHub task {TaskId} rejected: {Reason}", taskId, reason ?? "(no reason)");
        if (_completedTasks.TryGetValue(taskId, out var gh))
            gh.Status = AutomationTaskStatus.Rejected;
        return Task.CompletedTask;
    }

    #region Helpers

    private bool IsTrackingEnabled(WorkItemKind kind)
    {
        var path = kind == WorkItemKind.PullRequest ? _prWorkflowPath : _issuesWorkflowPath;
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
    }

    private List<string> GetActiveStatesForKind(WorkItemKind kind) =>
        kind == WorkItemKind.PullRequest
            ? _config.Tracker.PullRequestActiveStates
            : _config.Tracker.ActiveStates;

    private string? ResolveWorkflowPath(string? configuredPath) =>
        string.IsNullOrWhiteSpace(configuredPath)
            ? null
            : Path.GetFullPath(configuredPath, _workspacePath);

    private static Dictionary<string, object?> BuildWorkItemData(TrackedWorkItem workItem, string? prDiff) => new()
    {
        ["id"] = workItem.Id,
        ["identifier"] = workItem.Identifier,
        ["title"] = workItem.Title,
        ["description"] = workItem.Description,
        ["priority"] = workItem.Priority,
        ["state"] = workItem.State,
        ["kind"] = workItem.Kind.ToString(),
        ["branch_name"] = workItem.BranchName,
        ["url"] = workItem.Url,
        ["labels"] = workItem.Labels.ToList(),
        ["created_at"] = workItem.CreatedAt?.ToString("o"),
        ["updated_at"] = workItem.UpdatedAt?.ToString("o"),
        ["head_branch"] = workItem.HeadBranch,
        ["base_branch"] = workItem.BaseBranch,
        ["diff_url"] = workItem.DiffUrl,
        ["review_state"] = workItem.ReviewState.ToString(),
        ["is_draft"] = workItem.IsDraft,
        ["diff"] = prDiff,
    };

    #endregion

    #region Inner tool provider

    /// <summary>
    /// Per-profile tool provider that creates GitHub-specific tools (SubmitReview or CompleteIssue)
    /// by resolving the active task from the workspace path at tool creation time.
    /// </summary>
    internal sealed class GitHubTaskToolProvider(
        WorkItemKind kind,
        ConcurrentDictionary<string, GitHubAutomationTask> activeTasks,
        IWorkItemTracker tracker,
        ConcurrentDictionary<string, bool> reviewCompleted,
        ILogger<GitHubTaskToolProvider> logger) : IAgentToolProvider
    {
        public int Priority => 95;

        public IEnumerable<AITool> CreateTools(ToolProviderContext context)
        {
            var taskId = ExtractTaskIdFromWorkspace(context.WorkspacePath);
            if (taskId == null || !activeTasks.TryGetValue(taskId, out var task))
                yield break;

            if (kind == WorkItemKind.PullRequest)
            {
                var pullNumber = task.WorkItem.Id;
                var capturedTaskId = task.Id;

                yield return AIFunctionFactory.Create(
                    async (
                        [Description("Review event type. Accepted for compatibility but always submitted as COMMENT.")] string reviewEvent,
                        [Description("Review body summarizing your findings.")] string body) =>
                    {
                        logger.LogInformation("Agent submitting COMMENT review on PR #{Number}", pullNumber);
                        try
                        {
                            await tracker.SubmitReviewAsync(pullNumber, body, "COMMENT");
                            reviewCompleted[capturedTaskId] = true;
                            return $"Review (COMMENT) submitted on PR #{pullNumber}. The review is complete.";
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to submit review on PR #{Number}", pullNumber);
                            return $"Warning: could not submit review ({ex.Message}).";
                        }
                    },
                    "SubmitReview",
                    "Submit a COMMENT review on the pull request with your findings. " +
                    "Call this once you have finished reviewing all changed files.");
            }
            else
            {
                var issueId = task.WorkItem.Id;

                yield return AIFunctionFactory.Create(
                    async ([Description("Brief description of what was done to complete the issue.")] string reason) =>
                    {
                        logger.LogInformation("Agent calling CompleteIssue for {IssueId}: {Reason}", issueId, reason);
                        try
                        {
                            await tracker.CloseIssueAsync(issueId, reason);
                            return $"Issue #{issueId} has been marked as complete and closed on the tracker.";
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to close issue #{IssueId}", issueId);
                            return $"Warning: could not close issue ({ex.Message}).";
                        }
                    },
                    "CompleteIssue",
                    "Call this when the task is fully complete. Marks the issue as done and closes it on the tracker.");
            }
        }

        private static string? ExtractTaskIdFromWorkspace(string? workspacePath)
        {
            if (string.IsNullOrEmpty(workspacePath))
                return null;
            return Path.GetFileName(workspacePath);
        }
    }

    #endregion
}
