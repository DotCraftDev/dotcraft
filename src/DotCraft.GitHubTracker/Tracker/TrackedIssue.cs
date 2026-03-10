namespace DotCraft.GitHubTracker.Tracker;

/// <summary>
/// Normalized issue record used by orchestration, prompt rendering, and observability.
/// Field semantics follow GitHubTracker SPEC.md Section 4.1.1.
/// </summary>
public sealed class TrackedIssue
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

    public string? BranchName { get; init; }

    public string? Url { get; init; }

    /// <summary>
    /// Labels normalized to lowercase.
    /// </summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    public IReadOnlyList<BlockerRef> BlockedBy { get; init; } = [];

    public DateTimeOffset? CreatedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
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
public sealed class IssueStateSnapshot
{
    public required string Id { get; init; }
    
    public required string State { get; init; }
}
