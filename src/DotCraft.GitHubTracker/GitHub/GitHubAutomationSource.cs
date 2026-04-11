using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Automations.Abstractions;
using DotCraft.GitHubTracker.Tracker;
using DotCraft.GitHubTracker.Workflow;
using DotCraft.GitHubTracker.Workspace;
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
    private readonly WorkItemWorkspaceManager _workItemWorkspaceManager;
    private readonly WorkflowLoader _issueWorkflowLoader;
    private readonly WorkflowLoader _prWorkflowLoader;
    private readonly GitHubTrackerConfig _config;
    private readonly string _workspacePath;
    private readonly ILogger<GitHubAutomationSource> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ReviewStateStore _reviewStateStore;

    private readonly ConcurrentDictionary<string, GitHubAutomationTask> _activeTasks = new();
    private readonly ConcurrentDictionary<string, string> _reviewedSha = new();
    private readonly ConcurrentDictionary<string, bool> _reviewCompleted = new();
    private readonly ConcurrentDictionary<string, GitHubAutomationTask> _completedTasks = new();
    private readonly ConcurrentDictionary<string, string> _workspaceNameToTaskId = new();
    private readonly ConcurrentDictionary<string, string> _workspacePathByTaskId = new();
    private readonly ConcurrentDictionary<string, List<PullRequestReviewFinding>> _persistedFindings = new();
    private readonly ConcurrentDictionary<string, List<PullRequestReviewFinding>> _submittedFindings = new();

    private readonly string? _issuesWorkflowPath;
    private readonly string? _prWorkflowPath;

    public GitHubAutomationSource(
        IWorkItemTracker tracker,
        WorkItemWorkspaceManager workItemWorkspaceManager,
        WorkflowLoader issueWorkflowLoader,
        WorkflowLoader prWorkflowLoader,
        GitHubTrackerConfig config,
        string workspacePath,
        string craftPath,
        ILoggerFactory loggerFactory)
    {
        _tracker = tracker;
        _workItemWorkspaceManager = workItemWorkspaceManager;
        _issueWorkflowLoader = issueWorkflowLoader;
        _prWorkflowLoader = prWorkflowLoader;
        _config = config;
        _workspacePath = workspacePath;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<GitHubAutomationSource>();
        _reviewStateStore = new ReviewStateStore(craftPath, loggerFactory.CreateLogger<ReviewStateStore>());

        _issuesWorkflowPath = ResolveWorkflowPath(config.IssuesWorkflowPath);
        _prWorkflowPath = ResolveWorkflowPath(config.PullRequestWorkflowPath);

        var persistedStates = _reviewStateStore.LoadAll();
        foreach (var state in persistedStates.Values)
        {
            if (!string.IsNullOrWhiteSpace(state.LastReviewedSha))
                _reviewedSha[state.PullRequestId] = state.LastReviewedSha;

            if (state.Findings.Count > 0)
                _persistedFindings[state.PullRequestId] = [.. state.Findings];
        }
    }

    /// <inheritdoc />
    public string Name => "github";

    /// <inheritdoc />
    public string ToolProfileName => "github-issue";

    /// <inheritdoc />
    public void RegisterToolProfile(IToolProfileRegistry registry)
    {
        var issueProvider = new GitHubTaskToolProvider(
            WorkItemKind.Issue, _activeTasks, _workspaceNameToTaskId, _tracker, _reviewCompleted,
            _submittedFindings, _persistedFindings, _loggerFactory.CreateLogger<GitHubTaskToolProvider>());

        var prProvider = new GitHubTaskToolProvider(
            WorkItemKind.PullRequest, _activeTasks, _workspaceNameToTaskId, _tracker, _reviewCompleted,
            _submittedFindings, _persistedFindings, _loggerFactory.CreateLogger<GitHubTaskToolProvider>());

        registry.Register("github-issue", [issueProvider]);
        registry.Register("github-pr", [prProvider]);
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

            if (workItem.Kind == WorkItemKind.Issue && IsBlocked(workItem))
                continue;

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

        IReadOnlyList<PullRequestChangedFile> changedFiles = [];
        IReadOnlyList<PullRequestReviewFinding> previousFindings =
            _persistedFindings.TryGetValue(task.Id, out var persisted) ? [.. persisted] : [];
        string? lastReviewedSha = null;
        string? incrementalBaseSha = null;
        var isIncrementalReview = false;

        if (gh.Kind == WorkItemKind.PullRequest)
        {
            _reviewedSha.TryGetValue(task.Id, out lastReviewedSha);
            incrementalBaseSha = lastReviewedSha;

            try
            {
                changedFiles = await _tracker.FetchPullRequestFilesAsync(gh.WorkItem.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch changed files for PR {Identifier}", gh.WorkItem.Identifier);
            }

            try
            {
                var fetchedFindings = await _tracker.FetchBotReviewsAsync(gh.WorkItem.Id, ct);
                if (fetchedFindings.Count > 0)
                {
                    previousFindings = fetchedFindings;
                    _persistedFindings[task.Id] = [.. fetchedFindings];
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch prior reviews for PR {Identifier}", gh.WorkItem.Identifier);
            }

            if (!string.IsNullOrWhiteSpace(lastReviewedSha) && !string.IsNullOrWhiteSpace(gh.WorkItem.HeadSha))
            {
                if (_workspacePathByTaskId.TryGetValue(task.Id, out var workspacePath))
                {
                    isIncrementalReview = await IsAncestorCommitAsync(
                        workspacePath, lastReviewedSha!, gh.WorkItem.HeadSha!, ct);
                    if (!isIncrementalReview)
                    {
                        incrementalBaseSha = null;
                        _logger.LogInformation(
                            "Falling back to full-scope review for PR {Identifier} because {Sha} is not an ancestor of HEAD {HeadSha}",
                            gh.WorkItem.Identifier,
                            lastReviewedSha,
                            gh.WorkItem.HeadSha);
                    }
                }
                else
                {
                    incrementalBaseSha = null;
                }
            }
        }

        var workItemData = BuildWorkItemData(
            gh.WorkItem,
            changedFiles,
            previousFindings,
            lastReviewedSha,
            incrementalBaseSha,
            isIncrementalReview);
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
            var findings = _submittedFindings.TryRemove(task.Id, out var submittedFindings)
                ? submittedFindings
                : (_persistedFindings.TryGetValue(task.Id, out var previousFindings) ? previousFindings : []);

            _reviewedSha[task.Id] = gh.WorkItem.HeadSha;
            gh.ReviewedAtSha = gh.WorkItem.HeadSha;
            _persistedFindings[task.Id] = [.. findings];
            _reviewStateStore.Save(new StoredReviewState
            {
                PullRequestId = task.Id,
                LastReviewedSha = gh.WorkItem.HeadSha,
                ReviewedAtUtc = DateTimeOffset.UtcNow,
                Findings = [.. findings],
            });

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
    public async Task<string?> ProvisionWorkspaceAsync(AutomationTask task, CancellationToken ct)
    {
        var gh = (GitHubAutomationTask)task;
        var workspace = await _workItemWorkspaceManager.EnsureWorkspaceAsync(gh.WorkItem, ct);
        var dirName = Path.GetFileName(workspace.Path);
        if (dirName != null)
            _workspaceNameToTaskId[dirName] = task.Id;
        _workspacePathByTaskId[task.Id] = workspace.Path;
        return workspace.Path;
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

    /// <inheritdoc />
    public Task DeleteTaskAsync(string taskId, CancellationToken ct)
    {
        _logger.LogInformation("GitHub task {TaskId} removed from local automation cache", taskId);
        _completedTasks.TryRemove(taskId, out _);
        _activeTasks.TryRemove(taskId, out _);
        _reviewedSha.TryRemove(taskId, out _);
        _persistedFindings.TryRemove(taskId, out _);
        _submittedFindings.TryRemove(taskId, out _);
        _workspacePathByTaskId.TryRemove(taskId, out _);
        _reviewStateStore.Delete(taskId);
        var workspaceKeys = new List<string>(_workspaceNameToTaskId.Keys);
        foreach (var key in workspaceKeys)
        {
            if (_workspaceNameToTaskId.TryGetValue(key, out var tid) &&
                string.Equals(tid, taskId, StringComparison.Ordinal))
                _workspaceNameToTaskId.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnBeforeAgentRunAsync(AutomationTask task, string workspacePath, CancellationToken ct) =>
        _workItemWorkspaceManager.RunBeforeRunHookAsync(workspacePath, ct);

    /// <inheritdoc />
    public Task OnAfterAgentRunAsync(AutomationTask task, string workspacePath, CancellationToken ct) =>
        _workItemWorkspaceManager.RunAfterRunHookAsync(workspacePath, ct);

    #region Helpers

    private bool IsBlocked(TrackedWorkItem workItem)
    {
        if (!string.Equals(workItem.State.Trim(), "todo", StringComparison.OrdinalIgnoreCase))
            return false;
        var terminalStates = _config.Tracker.TerminalStates
            .Select(s => s.Trim().ToLowerInvariant()).ToHashSet();
        return workItem.BlockedBy.Any(b =>
            b.State != null && !terminalStates.Contains(b.State.Trim().ToLowerInvariant()));
    }

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

    private static Dictionary<string, object?> BuildWorkItemData(
        TrackedWorkItem workItem,
        IReadOnlyList<PullRequestChangedFile> changedFiles,
        IReadOnlyList<PullRequestReviewFinding> previousFindings,
        string? lastReviewedSha,
        string? incrementalBaseSha,
        bool isIncrementalReview)
    {
        var changedFileRows = changedFiles.Select(f => new Dictionary<string, object?>
        {
            ["filename"] = f.Filename,
            ["status"] = f.Status,
            ["additions"] = f.Additions,
            ["deletions"] = f.Deletions,
        }).ToList();

        var previousFindingRows = previousFindings.Select(f => new Dictionary<string, object?>
        {
            ["severity"] = f.Severity == ReviewFindingSeverity.Red ? "RED" : "YELLOW",
            ["title"] = f.Title,
            ["summary"] = f.Summary,
            ["file"] = f.FilePath,
            ["resolved"] = f.IsResolved,
        }).ToList();

        var diffStats = new Dictionary<string, object?>
        {
            ["files_changed"] = changedFiles.Count,
            ["additions"] = changedFiles.Sum(f => f.Additions),
            ["deletions"] = changedFiles.Sum(f => f.Deletions),
        };

        return new Dictionary<string, object?>
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
            ["head_sha"] = workItem.HeadSha,
            ["diff_url"] = workItem.DiffUrl,
            ["review_state"] = workItem.ReviewState.ToString(),
            ["is_draft"] = workItem.IsDraft,
            ["changed_files"] = changedFileRows,
            ["diff_stats"] = diffStats,
            ["previous_findings"] = previousFindingRows,
            ["last_reviewed_sha"] = lastReviewedSha,
            ["incremental_base_sha"] = incrementalBaseSha,
            ["is_incremental_review"] = isIncrementalReview,
        };
    }

    private async Task<bool> IsAncestorCommitAsync(
        string workspacePath,
        string ancestorSha,
        string headSha,
        CancellationToken ct)
    {
        try
        {
            var startInfo = new ProcessStartInfo("git", $"merge-base --is-ancestor {ancestorSha} {headSha}")
            {
                WorkingDirectory = workspacePath,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await process.WaitForExitAsync(ct);

            return process.ExitCode switch
            {
                0 => true,
                1 => false,
                _ => false,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to determine incremental range in {WorkspacePath}; defaulting to full-scope review",
                workspacePath);
            return false;
        }
    }

    #endregion

    #region Inner tool provider

    /// <summary>
    /// Per-profile tool provider that creates GitHub-specific tools (SubmitReview or CompleteIssue)
    /// by resolving the active task from the workspace path at tool creation time.
    /// </summary>
    internal sealed class GitHubTaskToolProvider(
        WorkItemKind kind,
        ConcurrentDictionary<string, GitHubAutomationTask> activeTasks,
        ConcurrentDictionary<string, string> workspaceNameToTaskId,
        IWorkItemTracker tracker,
        ConcurrentDictionary<string, bool> reviewCompleted,
        ConcurrentDictionary<string, List<PullRequestReviewFinding>> submittedFindings,
        ConcurrentDictionary<string, List<PullRequestReviewFinding>> previousFindings,
        ILogger<GitHubTaskToolProvider> logger) : IAgentToolProvider
    {
        public int Priority => 95;

        public IEnumerable<AITool> CreateTools(ToolProviderContext context)
        {
            var dirName = ExtractDirNameFromWorkspace(context.WorkspacePath);
            if (dirName == null)
                yield break;

            var taskId = workspaceNameToTaskId.GetValueOrDefault(dirName, dirName);
            if (!activeTasks.TryGetValue(taskId, out var task))
                yield break;

            if (kind == WorkItemKind.PullRequest)
            {
                var pullNumber = task.WorkItem.Id;
                var capturedTaskId = task.Id;

                yield return AIFunctionFactory.Create(
                    async (
                        [Description("JSON object with majorCount, minorCount, suggestionCount, and body. Use body 'No issues found.' when there are no issues.")] string summaryJson,
                        [Description("JSON array of inline comments. Use [] when there are no inline comments. Each item must include severity, title, body, path, line, and optional side, startLine, startSide, and suggestionReplacement.")] string commentsJson) =>
                    {
                        logger.LogInformation("Agent submitting structured COMMENT review on PR #{Number}", pullNumber);
                        try
                        {
                            var summary = ParseStructuredReviewSummary(summaryJson);
                            var comments = ParseStructuredInlineComments(commentsJson);
                            var currentFindings = ConvertInlineCommentsToFindings(comments);
                            submittedFindings[capturedTaskId] = [.. currentFindings];

                            if (currentFindings.Count > 0 &&
                                previousFindings.TryGetValue(capturedTaskId, out var historicalFindings) &&
                                historicalFindings.Count > 0)
                            {
                                var duplicateCount = CountLikelyDuplicates(currentFindings, historicalFindings);
                                if (duplicateCount > 0)
                                {
                                    logger.LogWarning(
                                        "PR #{Number} structured review has {Count} likely duplicate finding(s) compared with previous reviews",
                                        pullNumber,
                                        duplicateCount);
                                }
                            }

                            var submitResult = await tracker.SubmitStructuredReviewAsync(pullNumber, summary, comments);
                            if (submitResult.SummaryPosted)
                                reviewCompleted[capturedTaskId] = true;

                            if (!submitResult.SummaryPosted)
                                return $"Warning: structured review on PR #{pullNumber} did not submit any summary comment.";

                            if (submitResult.InlineRequestedCount > 0 && submitResult.InlinePostedCount == 0)
                            {
                                var details = submitResult.Warnings.Count > 0
                                    ? $" Warnings: {string.Join(" | ", submitResult.Warnings)}"
                                    : string.Empty;
                                return $"Review summary submitted on PR #{pullNumber}, but no inline comments were posted.{details}";
                            }

                            if (submitResult.InlineFailedCount > 0 || submitResult.Warnings.Count > 0)
                            {
                                var details = submitResult.Warnings.Count > 0
                                    ? $" Warnings: {string.Join(" | ", submitResult.Warnings)}"
                                    : string.Empty;
                                return $"Review summary submitted on PR #{pullNumber} with {submitResult.InlinePostedCount}/{submitResult.InlineRequestedCount} inline comments posted.{details}";
                            }

                            return $"Structured review (COMMENT) submitted on PR #{pullNumber} with {submitResult.InlinePostedCount} inline comments. The review is complete.";
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Failed to submit structured review on PR #{Number}", pullNumber);
                            return $"Warning: could not submit structured review ({ex.Message}).";
                        }
                    },
                    "SubmitReview",
                    "Submit a COMMENT review with a summary plus inline review comments. " +
                    "Use this for the GitHub PR workflow whenever you can anchor findings to changed lines. " +
                    "Each inline comment may optionally include a single-file, single-range suggestion.");
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

        private static string? ExtractDirNameFromWorkspace(string? workspacePath)
        {
            if (string.IsNullOrEmpty(workspacePath))
                return null;
            return Path.GetFileName(workspacePath);
        }

        private static int CountLikelyDuplicates(
            IReadOnlyList<PullRequestReviewFinding> current,
            IReadOnlyList<PullRequestReviewFinding> previous)
        {
            var duplicateCount = 0;
            foreach (var finding in current)
            {
                var isDuplicate = previous.Any(p =>
                    p.Severity == finding.Severity &&
                    string.Equals(Normalize(p.Title), Normalize(finding.Title), StringComparison.Ordinal) &&
                    string.Equals(Normalize(p.Summary), Normalize(finding.Summary), StringComparison.Ordinal));

                if (isDuplicate)
                    duplicateCount++;
            }

            return duplicateCount;
        }

        private static IReadOnlyList<PullRequestReviewFinding> ParseFindingsFromReviewBody(string body)
        {
            var findings = new List<PullRequestReviewFinding>();
            var headingMatches = ReviewHeadingRegex.Matches(body);
            for (var i = 0; i < headingMatches.Count; i++)
            {
                var current = headingMatches[i];
                var nextIndex = i + 1 < headingMatches.Count ? headingMatches[i + 1].Index : body.Length;
                var summary = body.Substring(current.Index + current.Length, nextIndex - (current.Index + current.Length)).Trim();
                if (string.IsNullOrWhiteSpace(summary))
                    continue;

                findings.Add(new PullRequestReviewFinding
                {
                    Severity = current.Groups["icon"].Value == "🔴" ? ReviewFindingSeverity.Red : ReviewFindingSeverity.Yellow,
                    Title = current.Groups["title"].Value.Trim(),
                    Summary = summary,
                    FilePath = null,
                    IsResolved = false,
                });
            }

            return findings;
        }

        private static PullRequestReviewSummary ParseStructuredReviewSummary(string summaryJson)
        {
            var summary = JsonSerializer.Deserialize<StructuredReviewSummaryInput>(summaryJson, StructuredReviewJsonOptions)
                ?? throw new InvalidOperationException("Structured review summary JSON was empty.");

            if (string.IsNullOrWhiteSpace(summary.Body))
                throw new InvalidOperationException("Structured review summary must include a non-empty body.");

            return new PullRequestReviewSummary
            {
                MajorCount = Math.Max(0, summary.MajorCount),
                MinorCount = Math.Max(0, summary.MinorCount),
                SuggestionCount = Math.Max(0, summary.SuggestionCount),
                Body = summary.Body.Trim(),
            };
        }

        internal static IReadOnlyList<PullRequestInlineComment> ParseStructuredInlineComments(string commentsJson)
        {
            var items = JsonSerializer.Deserialize<List<StructuredInlineCommentInput>>(commentsJson, StructuredReviewJsonOptions) ?? [];
            return items.Select(item =>
            {
                if (string.IsNullOrWhiteSpace(item.Title))
                    throw new InvalidOperationException("Each inline comment must include a title.");
                if (string.IsNullOrWhiteSpace(item.Body))
                    throw new InvalidOperationException("Each inline comment must include a body.");
                if (string.IsNullOrWhiteSpace(item.Path))
                    throw new InvalidOperationException("Each inline comment must include a path.");
                if (item.Line <= 0)
                    throw new InvalidOperationException("Each inline comment must include a positive line number.");
                if (item.StartLine.HasValue && item.StartLine.Value <= 0)
                    throw new InvalidOperationException("StartLine must be a positive line number when provided.");
                if (item.StartLine.HasValue && item.StartLine.Value > item.Line)
                    throw new InvalidOperationException("StartLine must be less than or equal to Line.");

                var normalizedPath = NormalizeCommentPath(item.Path);
                var side = ParseSide(item.Side, PullRequestReviewCommentSide.Right);
                var normalizedStartLine = item.StartLine == item.Line ? null : item.StartLine;
                var normalizedStartSide = normalizedStartLine.HasValue
                    ? (ParseOptionalSide(item.StartSide) ?? side)
                    : (PullRequestReviewCommentSide?)null;

                if (normalizedStartLine.HasValue && normalizedStartSide != side)
                    throw new InvalidOperationException("Multi-line inline comments must stay on a single diff side.");

                PullRequestSuggestion? suggestion = null;
                if (!string.IsNullOrWhiteSpace(item.SuggestionReplacement))
                {
                    if (side != PullRequestReviewCommentSide.Right ||
                        (normalizedStartSide.HasValue && normalizedStartSide.Value != PullRequestReviewCommentSide.Right))
                    {
                        throw new InvalidOperationException("Suggestions are only supported on RIGHT-side diff anchors.");
                    }

                    suggestion = new PullRequestSuggestion
                    {
                        Replacement = item.SuggestionReplacement.TrimEnd(),
                    };
                }

                return new PullRequestInlineComment
                {
                    Severity = ParseSeverity(item.Severity),
                    Title = item.Title.Trim(),
                    Body = item.Body.Trim(),
                    Path = normalizedPath,
                    Line = item.Line,
                    Side = side,
                    StartLine = normalizedStartLine,
                    StartSide = normalizedStartSide,
                    Suggestion = suggestion,
                };
            }).ToList();
        }

        private static string NormalizeCommentPath(string path)
        {
            var normalized = path.Trim().Replace('\\', '/');
            while (normalized.StartsWith("./", StringComparison.Ordinal))
                normalized = normalized[2..];

            if (string.IsNullOrWhiteSpace(normalized))
                throw new InvalidOperationException("Each inline comment must include a non-empty relative path.");
            if (Path.IsPathRooted(normalized))
                throw new InvalidOperationException("Inline comment path must be relative to the repository root.");
            if (normalized.StartsWith("/", StringComparison.Ordinal))
                throw new InvalidOperationException("Inline comment path must not start with '/'.");
            if (normalized.Contains("/../", StringComparison.Ordinal) ||
                normalized.StartsWith("../", StringComparison.Ordinal) ||
                normalized.EndsWith("/..", StringComparison.Ordinal) ||
                normalized.Contains("/./", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Inline comment path must not contain path traversal segments.");
            }

            return normalized;
        }

        private static List<PullRequestReviewFinding> ConvertInlineCommentsToFindings(
            IReadOnlyList<PullRequestInlineComment> comments) =>
            comments.Select(comment => new PullRequestReviewFinding
            {
                Severity = comment.Severity,
                Title = comment.Title,
                Summary = comment.Body,
                FilePath = comment.Path,
                IsResolved = false,
            }).ToList();

        private static ReviewFindingSeverity ParseSeverity(string? severity) =>
            string.Equals(severity?.Trim(), "RED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(severity?.Trim(), "MAJOR", StringComparison.OrdinalIgnoreCase)
                ? ReviewFindingSeverity.Red
                : ReviewFindingSeverity.Yellow;

        private static PullRequestReviewCommentSide ParseSide(string? side, PullRequestReviewCommentSide defaultValue)
        {
            var parsed = ParseOptionalSide(side);
            return parsed ?? defaultValue;
        }

        private static PullRequestReviewCommentSide? ParseOptionalSide(string? side)
        {
            if (string.IsNullOrWhiteSpace(side))
                return null;

            return side.Trim().ToUpperInvariant() switch
            {
                "LEFT" => PullRequestReviewCommentSide.Left,
                "RIGHT" => PullRequestReviewCommentSide.Right,
                _ => throw new InvalidOperationException($"Unsupported review comment side '{side}'."),
            };
        }

        private static string Normalize(string input) =>
            WhitespaceRegex.Replace(input.Trim().ToLowerInvariant(), " ");

        private static readonly Regex ReviewHeadingRegex = new(
            @"(?m)^(?<icon>🔴|🟡)\s+(?<title>.+?)\s*$",
            RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

        private static readonly JsonSerializerOptions StructuredReviewJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private sealed class StructuredReviewSummaryInput
        {
            public int MajorCount { get; set; }

            public int MinorCount { get; set; }

            public int SuggestionCount { get; set; }

            public string? Body { get; set; }
        }

        private sealed class StructuredInlineCommentInput
        {
            public string? Severity { get; set; }

            public string? Title { get; set; }

            public string? Body { get; set; }

            public string? Path { get; set; }

            public int Line { get; set; }

            public string? Side { get; set; }

            public int? StartLine { get; set; }

            public string? StartSide { get; set; }

            public string? SuggestionReplacement { get; set; }
        }
    }

    #endregion
}
