namespace DotCraft.GitHubTracker.Tracker;

/// <summary>
/// Distinguishes between issue-based and pull-request-based work items.
/// </summary>
public enum WorkItemKind
{
    Issue,
    PullRequest,
}

/// <summary>
/// Review decision state for a pull request.
/// </summary>
public enum PullRequestReviewState
{
    None,
    Pending,
    Approved,
    ChangesRequested,
}

/// <summary>
/// Aggregated CI / status-check outcome for a pull request.
/// </summary>
public enum PullRequestChecksStatus
{
    None,
    Pending,
    Success,
    Failure,
}

/// <summary>
/// Normalized work-item record used by orchestration, prompt rendering, and observability.
/// Represents both GitHub Issues and Pull Requests.
/// </summary>
public sealed class TrackedWorkItem
{
    /// <summary>
    /// Stable tracker-internal ID.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable ticket key (e.g. "ABC-123" or "#42").
    /// </summary>
    public required string Identifier { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// Lower numbers are higher priority. Null sorts last in dispatch.
    /// </summary>
    public int? Priority { get; init; }

    /// <summary>
    /// Current tracker state name (normalized).
    /// </summary>
    public required string State { get; init; }

    /// <summary>
    /// Whether this work item is an issue or a pull request.
    /// </summary>
    public WorkItemKind Kind { get; init; } = WorkItemKind.Issue;

    public string? BranchName { get; init; }

    public string? Url { get; init; }

    /// <summary>
    /// Labels normalized to lowercase.
    /// </summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    public IReadOnlyList<BlockerRef> BlockedBy { get; init; } = [];

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    #region Pull-Request-specific fields (null/default for issues)

    /// <summary>
    /// Source branch of the pull request (e.g. "feature/my-branch").
    /// </summary>
    public string? HeadBranch { get; init; }

    /// <summary>
    /// Target branch of the pull request (e.g. "main").
    /// </summary>
    public string? BaseBranch { get; init; }

    /// <summary>
    /// URL to the raw diff of the pull request.
    /// </summary>
    public string? DiffUrl { get; init; }

    /// <summary>
    /// Aggregated review decision for the pull request.
    /// </summary>
    public PullRequestReviewState ReviewState { get; init; }

    /// <summary>
    /// Aggregated CI check status for the pull request.
    /// </summary>
    public PullRequestChecksStatus ChecksStatus { get; init; }

    /// <summary>
    /// Whether the pull request is in draft mode.
    /// </summary>
    public bool IsDraft { get; init; }

    #endregion
}

public sealed class BlockerRef
{
    public string? Id { get; init; }
    
    public string? Identifier { get; init; }
    
    public string? State { get; init; }
}

/// <summary>
/// Lightweight snapshot used for reconciliation state refresh.
/// </summary>
public sealed class WorkItemStateSnapshot
{
    public required string Id { get; init; }
    
    public required string State { get; init; }
}
