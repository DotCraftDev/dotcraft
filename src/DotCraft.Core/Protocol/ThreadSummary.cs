using System.Text.Json.Serialization;

namespace DotCraft.Protocol;

/// <summary>
/// Best-effort runtime state attached to thread summaries so reconnecting clients
/// can hydrate list activity indicators without reading every thread.
/// </summary>
public sealed class ThreadSummaryRuntime
{
    /// <summary>
    /// True when the thread currently has a running or approval-waiting turn.
    /// </summary>
    public bool Running { get; set; }

    /// <summary>
    /// True when the active turn is waiting on approval.
    /// </summary>
    public bool WaitingOnApproval { get; set; }

    /// <summary>
    /// True when the last completed turn produced a plan that still needs user confirmation.
    /// </summary>
    public bool WaitingOnPlanConfirmation { get; set; }
}

/// <summary>
/// Lightweight Thread descriptor used in the thread index and discovery results.
/// Does not include full Turn/Item history.
/// </summary>
public sealed class ThreadSummary
{
    public string Id { get; set; } = string.Empty;

    public string? UserId { get; set; }

    public string WorkspacePath { get; set; } = string.Empty;

    public string OriginChannel { get; set; } = string.Empty;

    /// <summary>
    /// Channel-specific context key (mirrors SessionThread.ChannelContext).
    /// Populated from the first-class property with a fallback to Metadata["channelContext"]
    /// for threads created before this property was introduced.
    /// </summary>
    public string? ChannelContext { get; set; }

    public string? DisplayName { get; set; }

    public ThreadSource Source { get; set; } = ThreadSource.User();

    public ThreadStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastActiveAt { get; set; }

    public int TurnCount { get; set; }

    /// <summary>
    /// Optional process-local runtime snapshot for thread-list activity indicators.
    /// Persisted index rows may omit this when the thread is not loaded in memory.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadSummaryRuntime? Runtime { get; set; }

    /// <summary>
    /// Channel-specific metadata copied from the Thread.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// Creates a summary from a full SessionThread.
    /// </summary>
    public static ThreadSummary FromThread(SessionThread thread) =>
        new()
        {
            Id = thread.Id,
            UserId = thread.UserId,
            WorkspacePath = thread.WorkspacePath,
            OriginChannel = thread.OriginChannel,
            // Prefer the first-class property; fall back to Metadata for threads persisted before this field existed.
            ChannelContext = thread.ChannelContext
                ?? (thread.Metadata.TryGetValue("channelContext", out var mc) ? mc : null),
            DisplayName = thread.DisplayName,
            Source = thread.Source,
            Status = thread.Status,
            CreatedAt = thread.CreatedAt,
            LastActiveAt = thread.LastActiveAt,
            TurnCount = thread.Turns.Count,
            Runtime = CreateRuntimeSnapshot(thread),
            Metadata = new Dictionary<string, string>(thread.Metadata)
        };

    private static ThreadSummaryRuntime CreateRuntimeSnapshot(SessionThread thread)
    {
        var activeTurn = thread.Turns.LastOrDefault(turn =>
            turn.Status is TurnStatus.Running or TurnStatus.WaitingApproval);
        if (activeTurn is not null)
        {
            return new ThreadSummaryRuntime
            {
                Running = true,
                WaitingOnApproval = activeTurn.Status == TurnStatus.WaitingApproval,
                WaitingOnPlanConfirmation = false
            };
        }

        var lastTurn = thread.Turns.LastOrDefault();
        return new ThreadSummaryRuntime
        {
            Running = false,
            WaitingOnApproval = false,
            WaitingOnPlanConfirmation = lastTurn is not null && EndsWithSuccessfulCreatePlanInPlanMode(thread, lastTurn)
        };
    }

    private static bool EndsWithSuccessfulCreatePlanInPlanMode(SessionThread thread, SessionTurn turn)
    {
        if (!string.Equals(thread.Configuration?.Mode, "plan", StringComparison.OrdinalIgnoreCase))
            return false;

        for (var idx = turn.Items.Count - 1; idx >= 0; idx--)
        {
            if (turn.Items[idx].Payload is not ToolCallPayload toolCall)
                continue;

            if (!string.Equals(toolCall.ToolName, "CreatePlan", StringComparison.Ordinal))
                return false;

            return turn.Items
                .Where(item => item.Payload is ToolResultPayload)
                .Select(item => item.Payload as ToolResultPayload)
                .Any(result =>
                    result != null
                    && string.Equals(result.CallId, toolCall.CallId, StringComparison.Ordinal)
                    && result.Success);
        }

        return false;
    }
}
