namespace DotCraft.GitHubTracker.Tracker;

/// <summary>
/// Abstraction for work-item tracker integration.
/// Supports both GitHub Issues and Pull Requests.
/// </summary>
public interface IWorkItemTracker
{
    /// <summary>
    /// Fetch work items (issues and/or PRs) in configured active states.
    /// Used for dispatch candidate selection.
    /// </summary>
    Task<IReadOnlyList<TrackedWorkItem>> FetchCandidateWorkItemsAsync(CancellationToken ct = default);

    /// <summary>
    /// Fetch current state for specific work-item IDs.
    /// Used for active-run reconciliation.
    /// </summary>
    Task<IReadOnlyList<WorkItemStateSnapshot>> FetchWorkItemStatesByIdsAsync(
        IReadOnlyList<string> workItemIds, CancellationToken ct = default);

    /// <summary>
    /// Fetch work items currently in the given state names.
    /// Used for startup terminal cleanup.
    /// </summary>
    Task<IReadOnlyList<TrackedWorkItem>> FetchWorkItemsByStatesAsync(
        IReadOnlyList<string> stateNames, CancellationToken ct = default);

    /// <summary>
    /// Close an issue on the tracker to signal task completion.
    /// Removes active-state labels and closes the issue.
    /// </summary>
    Task CloseIssueAsync(string issueId, string reason, CancellationToken ct = default);

    /// <summary>
    /// Submit a review on a pull request.
    /// </summary>
    /// <param name="pullNumber">PR number.</param>
    /// <param name="body">Review body / summary.</param>
    /// <param name="event">One of: APPROVE, REQUEST_CHANGES, COMMENT.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SubmitReviewAsync(string pullNumber, string body, string @event, CancellationToken ct = default);

    /// <summary>
    /// Fetch the diff content for a pull request.
    /// </summary>
    Task<string> FetchPullRequestDiffAsync(string pullNumber, CancellationToken ct = default);

    /// <summary>
    /// Fetch changed files metadata for a pull request.
    /// </summary>
    Task<IReadOnlyList<PullRequestChangedFile>> FetchPullRequestFilesAsync(
        string pullNumber, CancellationToken ct = default);

    /// <summary>
    /// Fetch previously submitted bot findings for a pull request.
    /// </summary>
    Task<IReadOnlyList<PullRequestReviewFinding>> FetchBotReviewsAsync(
        string pullNumber, CancellationToken ct = default);
}
