using System.Text.Json.Serialization;

namespace DotCraft.Sessions.Protocol;

/// <summary>
/// A structured event emitted by Session Core during Thread/Turn/Item lifecycle transitions.
/// Channel adapters subscribe to these events and translate them to their transport format.
/// </summary>
public sealed class SessionEvent
{
    /// <summary>
    /// Unique event ID, monotonically increasing within a Turn.
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    public SessionEventType EventType { get; set; }

    /// <summary>
    /// Parent Thread ID.
    /// </summary>
    public string ThreadId { get; set; } = string.Empty;

    /// <summary>
    /// Parent Turn ID. Null for thread-level events (thread/created, thread/resumed, thread/statusChanged).
    /// </summary>
    public string? TurnId { get; set; }

    /// <summary>
    /// Related Item ID. Null for turn-level and thread-level events.
    /// </summary>
    public string? ItemId { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Event-type-specific payload. Use typed accessor properties for safe access.
    /// </summary>
    public object? Payload { get; set; }

    // Typed payload accessors

    [JsonIgnore]
    public SessionThread? ThreadPayload => Payload as SessionThread;

    [JsonIgnore]
    public SessionTurn? TurnPayload => Payload as SessionTurn;

    [JsonIgnore]
    public SessionItem? ItemPayload => Payload as SessionItem;

    [JsonIgnore]
    public AgentMessageDelta? DeltaPayload => Payload as AgentMessageDelta;

    [JsonIgnore]
    public ReasoningContentDelta? ReasoningDeltaPayload => Payload as ReasoningContentDelta;

    [JsonIgnore]
    public ThreadStatusChangedPayload? StatusChangedPayload => Payload as ThreadStatusChangedPayload;
}

/// <summary>
/// Payload for thread/statusChanged events.
/// </summary>
public sealed record ThreadStatusChangedPayload
{
    public ThreadStatus PreviousStatus { get; init; }

    public ThreadStatus NewStatus { get; init; }
}
