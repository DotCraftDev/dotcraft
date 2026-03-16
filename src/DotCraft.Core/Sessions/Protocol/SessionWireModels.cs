using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Sessions.Protocol;

/// <summary>
/// Wire-oriented JSON options for Session Protocol DTOs.
/// </summary>
public static class SessionWireJsonOptions
{
    /// <summary>
    /// The canonical options used for wire DTO serialization.
    /// </summary>
    public static readonly JsonSerializerOptions Default = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerOptions.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

/// <summary>
/// Advertised extension capability for wire clients.
/// </summary>
public sealed record SessionExtensionCapability
{
    public string Namespace { get; init; } = string.Empty;

    public string? Description { get; init; }
}

/// <summary>
/// Wire DTO for a thread.
/// </summary>
public sealed record SessionWireThread
{
    public string Id { get; init; } = string.Empty;

    public string WorkspacePath { get; init; } = string.Empty;

    public string? UserId { get; init; }

    public string OriginChannel { get; init; } = string.Empty;

    public string? ChannelContext { get; init; }

    public string? DisplayName { get; init; }

    public ThreadStatus Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastActiveAt { get; init; }

    public HistoryMode HistoryMode { get; init; }

    public ThreadConfiguration? Configuration { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = [];
}

/// <summary>
/// Wire DTO for a turn.
/// </summary>
public sealed record SessionWireTurn
{
    public string Id { get; init; } = string.Empty;

    public string ThreadId { get; init; } = string.Empty;

    public TurnStatus Status { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public TokenUsageInfo? TokenUsage { get; init; }

    public string? Error { get; init; }

    public string? OriginChannel { get; init; }

    public TurnInitiatorContext? Initiator { get; init; }
}

/// <summary>
/// Wire DTO for a session item.
/// </summary>
public sealed record SessionWireItem
{
    public string Id { get; init; } = string.Empty;

    public string TurnId { get; init; } = string.Empty;

    public ItemType Type { get; init; }

    public ItemStatus Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string? PayloadKind { get; init; }

    public object? Payload { get; init; }
}

/// <summary>
/// Wire DTO for a session event.
/// </summary>
public sealed record SessionWireEvent
{
    public string EventId { get; init; } = string.Empty;

    public SessionEventType EventType { get; init; }

    public string ThreadId { get; init; } = string.Empty;

    public string? TurnId { get; init; }

    public string? ItemId { get; init; }

    public DateTimeOffset Timestamp { get; init; }

    public string? PayloadKind { get; init; }

    public object? Payload { get; init; }
}

/// <summary>
/// Maps persisted Session Core models into wire DTOs.
/// </summary>
public static class SessionWireMapper
{
    /// <summary>
    /// Maps a thread into the wire DTO.
    /// </summary>
    public static SessionWireThread ToWire(this SessionThread thread) =>
        new()
        {
            Id = thread.Id,
            WorkspacePath = thread.WorkspacePath,
            UserId = thread.UserId,
            OriginChannel = thread.OriginChannel,
            ChannelContext = thread.ChannelContext,
            DisplayName = thread.DisplayName,
            Status = thread.Status,
            CreatedAt = thread.CreatedAt,
            LastActiveAt = thread.LastActiveAt,
            HistoryMode = thread.HistoryMode,
            Configuration = thread.Configuration,
            Metadata = new Dictionary<string, string>(thread.Metadata)
        };

    /// <summary>
    /// Maps a turn into the wire DTO.
    /// </summary>
    public static SessionWireTurn ToWire(this SessionTurn turn) =>
        new()
        {
            Id = turn.Id,
            ThreadId = turn.ThreadId,
            Status = turn.Status,
            StartedAt = turn.StartedAt,
            CompletedAt = turn.CompletedAt,
            TokenUsage = turn.TokenUsage,
            Error = turn.Error,
            OriginChannel = turn.OriginChannel,
            Initiator = turn.Initiator
        };

    /// <summary>
    /// Maps an item into the wire DTO.
    /// </summary>
    public static SessionWireItem ToWire(this SessionItem item) =>
        new()
        {
            Id = item.Id,
            TurnId = item.TurnId,
            Type = item.Type,
            Status = item.Status,
            CreatedAt = item.CreatedAt,
            CompletedAt = item.CompletedAt,
            PayloadKind = GetPayloadKind(item.Payload),
            Payload = item.Payload
        };

    /// <summary>
    /// Maps an event into the wire DTO.
    /// </summary>
    public static SessionWireEvent ToWire(this SessionEvent evt) =>
        new()
        {
            EventId = evt.EventId,
            EventType = evt.EventType,
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId,
            ItemId = evt.ItemId,
            Timestamp = evt.Timestamp,
            PayloadKind = GetPayloadKind(evt.Payload),
            Payload = evt.Payload switch
            {
                SessionThread thread => thread.ToWire(),
                SessionTurn turn => turn.ToWire(),
                SessionItem item => item.ToWire(),
                _ => evt.Payload
            }
        };

    private static string? GetPayloadKind(object? payload) =>
        payload switch
        {
            AgentMessageDelta => "agentMessageDelta",
            ReasoningContentDelta => "reasoningContentDelta",
            ApprovalRequestPayload => "approvalRequest",
            ApprovalResponsePayload => "approvalResponse",
            ErrorPayload => "error",
            ToolCallPayload => "toolCall",
            ToolResultPayload => "toolResult",
            UserMessagePayload => "userMessage",
            AgentMessagePayload => "agentMessage",
            ReasoningContentPayload => "reasoningContent",
            SessionThread => "thread",
            SessionTurn => "turn",
            SessionItem => "item",
            ThreadStatusChangedPayload => "threadStatusChanged",
            _ => null
        };
}
