namespace DotCraft.GitHubTracker.Tracker;

/// <summary>
/// Abstraction for issue tracker integration.
/// Implementations must support three operations per SPEC.md Section 11.1.
/// </summary>
public interface IIssueTracker
{
    /// <summary>
    /// Fetch issues in configured active states for the configured project.
    /// Used for dispatch candidate selection.
    /// </summary>
    Task<IReadOnlyList<TrackedIssue>> FetchCandidateIssuesAsync(CancellationToken ct = default);

    /// <summary>
    /// Fetch current state for specific issue IDs.
    /// Used for active-run reconciliation.
    /// </summary>
    Task<IReadOnlyList<IssueStateSnapshot>> FetchIssueStatesByIdsAsync(
        IReadOnlyList<string> issueIds, CancellationToken ct = default);

    /// <summary>
    /// Fetch issues currently in the given state names.
    /// Used for startup terminal cleanup.
    /// </summary>
    Task<IReadOnlyList<TrackedIssue>> FetchIssuesByStatesAsync(
        IReadOnlyList<string> stateNames, CancellationToken ct = default);

    /// <summary>
    /// Close an issue on the tracker to signal task completion.
    /// Removes active-state labels and closes the issue.
    /// </summary>
    Task CloseIssueAsync(string issueId, string reason, CancellationToken ct = default);
}
