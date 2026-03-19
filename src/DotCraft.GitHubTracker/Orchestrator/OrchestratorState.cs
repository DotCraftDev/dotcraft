using DotCraft.GitHubTracker.Tracker;

namespace DotCraft.GitHubTracker.Orchestrator;

/// <summary>
/// In-memory runtime state owned by the orchestrator (single authority).
/// </summary>
public sealed class OrchestratorState
{
    public int PollIntervalMs { get; set; }
    public int MaxConcurrentAgents { get; set; }
    public Dictionary<string, RunningEntry> Running { get; } = [];
    public HashSet<string> Claimed { get; } = [];
    public Dictionary<string, RetryEntry> RetryAttempts { get; } = [];
    public HashSet<string> Completed { get; } = [];

    /// <summary>
    /// Maps PR work-item ID to the head SHA that was reviewed.
    /// In-memory only; empty after restart so all open PRs are re-reviewed on the first tick.
    /// See PR Lifecycle Spec section 4.2; Symphony SPEC section 14.3.
    /// </summary>
    public Dictionary<string, string> ReviewedSha { get; } = [];

    public AggregateMetrics Totals { get; } = new();
}

public sealed class RunningEntry
{
    public required string WorkItemId { get; init; }
    public required string Identifier { get; init; }
    public required TrackedWorkItem WorkItem { get; set; }
    public required DateTimeOffset StartedAt { get; init; }
    public required CancellationTokenSource Cts { get; init; }
    public required Task WorkerTask { get; init; }
    public int? RetryAttempt { get; init; }

    public string? SessionId { get; set; }
    public string? LastEvent { get; set; }
    public DateTimeOffset? LastEventTimestamp { get; set; }
    public string? LastMessage { get; set; }
    public int TurnCount { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens { get; set; }
}

public sealed class RetryEntry
{
    public required string WorkItemId { get; init; }
    public required string Identifier { get; init; }
    public required int Attempt { get; init; }
    public required long DueAtMs { get; init; }
    public string? Error { get; init; }
    public CancellationTokenSource? TimerCts { get; set; }
}

public sealed class AggregateMetrics
{
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens { get; set; }
    public double SecondsRunning { get; set; }
}

/// <summary>
/// Snapshot for the dashboard / status API.
/// </summary>
public sealed class OrchestratorSnapshot
{
    public DateTimeOffset GeneratedAt { get; init; }
    public int RunningCount { get; init; }
    public int RetryingCount { get; init; }
    public IReadOnlyList<RunningWorkItemSummary> Running { get; init; } = [];
    public IReadOnlyList<RetryWorkItemSummary> Retrying { get; init; } = [];
    public AggregateMetrics Totals { get; init; } = new();
}

public sealed class RunningWorkItemSummary
{
    public required string WorkItemId { get; init; }
    public required string Identifier { get; init; }
    public required string State { get; init; }
    public string? SessionId { get; init; }
    public int TurnCount { get; init; }
    public string? LastEvent { get; init; }
    public string? LastMessage { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? LastEventAt { get; init; }
    public long InputTokens { get; init; }
    public long OutputTokens { get; init; }
    public long TotalTokens { get; init; }
}

public sealed class RetryWorkItemSummary
{
    public required string WorkItemId { get; init; }
    public required string Identifier { get; init; }
    public required int Attempt { get; init; }
    public required long DueAtMs { get; init; }
    public string? Error { get; init; }
}
