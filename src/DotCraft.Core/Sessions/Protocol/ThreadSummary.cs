namespace DotCraft.Sessions.Protocol;

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

    public ThreadStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastActiveAt { get; set; }

    public int TurnCount { get; set; }

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
            Status = thread.Status,
            CreatedAt = thread.CreatedAt,
            LastActiveAt = thread.LastActiveAt,
            TurnCount = thread.Turns.Count,
            Metadata = new Dictionary<string, string>(thread.Metadata)
        };
}
