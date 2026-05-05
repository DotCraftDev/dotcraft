using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol;

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
        // Wire-spec-compliant approval decision names must be registered before the generic camelCase converter
        // so that the specific converter takes precedence for SessionApprovalDecision.
        options.Converters.Add(new WireApprovalDecisionConverter());
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

/// <summary>
/// Maps <see cref="SessionApprovalDecision"/> to the wire-spec string names defined in Section 7.3
/// of the Session Wire Protocol Specification:
/// <list type="bullet">
/// <item><c>accept</c> — AcceptOnce</item>
/// <item><c>acceptForSession</c> — AcceptForSession</item>
/// <item><c>decline</c> — Reject</item>
/// <item><c>cancel</c> — CancelTurn</item>
/// </list>
/// This converter is registered in <see cref="SessionWireJsonOptions"/> only, so the persistence
/// format (SessionJsonOptions.Default) is unaffected.
/// </summary>
internal sealed class WireApprovalDecisionConverter : JsonConverter<SessionApprovalDecision>
{
    public override SessionApprovalDecision Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return value switch
        {
            "accept" => SessionApprovalDecision.AcceptOnce,
            "acceptForSession" => SessionApprovalDecision.AcceptForSession,
            "acceptAlways" => SessionApprovalDecision.AcceptAlways,
            "decline" => SessionApprovalDecision.Reject,
            "cancel" => SessionApprovalDecision.CancelTurn,
            _ => throw new JsonException($"Unknown approval decision wire value: '{value}'")
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        SessionApprovalDecision value,
        JsonSerializerOptions options)
    {
        var wireValue = value switch
        {
            SessionApprovalDecision.AcceptOnce => "accept",
            SessionApprovalDecision.AcceptForSession => "acceptForSession",
            SessionApprovalDecision.AcceptAlways => "acceptAlways",
            SessionApprovalDecision.Reject => "decline",
            SessionApprovalDecision.CancelTurn => "cancel",
            _ => throw new JsonException($"Unknown SessionApprovalDecision value: {value}")
        };
        writer.WriteStringValue(wireValue);
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
/// Snapshot of the per-thread context-window usage used to drive the desktop token ring.
/// Tokens comes from persisted per-thread context usage state, not billing usage
/// or historical message estimation.
/// </summary>
public sealed record ContextUsageSnapshot
{
    /// <summary>
    /// Persisted input tokens currently occupying the context window.
    /// This is context occupancy, not billing or cumulative turn usage.
    /// </summary>
    public long Tokens { get; init; }

    /// <summary>
    /// Raw configured context window (<c>CompactionConfig.EffectiveContextWindow()</c>).
    /// This is the denominator the desktop ring should use.
    /// </summary>
    public int ContextWindow { get; init; }

    /// <summary>
    /// Token count at which auto-compact runs (<c>CompactionConfig.AutoCompactThreshold()</c>).
    /// </summary>
    public int AutoCompactThreshold { get; init; }

    /// <summary>
    /// Token count at which <c>compactWarning</c> starts firing.
    /// </summary>
    public int WarningThreshold { get; init; }

    /// <summary>
    /// Token count at which <c>compactError</c> starts firing.
    /// </summary>
    public int ErrorThreshold { get; init; }

    /// <summary>
    /// Percent of the effective context window still available (0.0 - 1.0).
    /// </summary>
    public double PercentLeft { get; init; }
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

    public ThreadSource Source { get; init; } = ThreadSource.User();

    public ThreadStatus Status { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset LastActiveAt { get; init; }

    public HistoryMode HistoryMode { get; init; }

    public ThreadConfiguration? Configuration { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = [];

    /// <summary>
    /// Best-effort runtime snapshot derived from persisted turns.
    /// </summary>
    public ThreadRuntimeState Runtime { get; init; } = new();

    /// <summary>
    /// FIFO inputs queued behind the currently active turn.
    /// </summary>
    public List<QueuedTurnInput> QueuedInputs { get; init; } = [];

    /// <summary>
    /// Turn summaries. Populated only when the caller requests turn history (e.g. thread/read with includeTurns = true).
    /// </summary>
    public List<SessionWireTurn>? Turns { get; init; }

    /// <summary>
    /// Context usage snapshot used by the desktop token ring. Populated on
    /// <c>thread/start</c>, <c>thread/resumed</c>, and <c>thread/read</c>; null when the thread has
    /// no persisted context usage state yet.
    /// </summary>
    public ContextUsageSnapshot? ContextUsage { get; init; }
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

    /// <summary>
    /// Items produced during this turn. Populated in turn/completed and turn/started notifications.
    /// </summary>
    public List<SessionWireItem>? Items { get; init; }
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
/// Wire DTO for a single unit of user input in a turn/start request.
/// Corresponds to the InputPart tagged union defined in Section 5.1 of the Session Wire Protocol Specification.
/// </summary>
public sealed record SessionWireInputPart
{
    /// <summary>
    /// Discriminator. One of: "text", "commandRef", "skillRef", "fileRef", "image", "localImage".
    /// </summary>
    public string Type { get; init; } = "text";

    /// <summary>
    /// Plain text content. Present when <see cref="Type"/> is "text".
    /// </summary>
    public string? Text { get; init; }

    /// <summary>
    /// Native command or skill name. Present when <see cref="Type"/> is "commandRef" or "skillRef".
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Optional command arguments text. Present when <see cref="Type"/> is "commandRef".
    /// </summary>
    public string? ArgsText { get; init; }

    /// <summary>
    /// Optional raw command invocation text (for example "/review src/foo.cs").
    /// Present when <see cref="Type"/> is "commandRef".
    /// </summary>
    public string? RawText { get; init; }

    /// <summary>
    /// Canonical file-reference path. Present when <see cref="Type"/> is "fileRef".
    /// Also used for <see cref="Type"/> "localImage".
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Optional UI-facing file-reference path. Present when <see cref="Type"/> is "fileRef".
    /// </summary>
    public string? DisplayPath { get; init; }

    /// <summary>
    /// Remote image URL. Present when <see cref="Type"/> is "image".
    /// </summary>
    public string? Url { get; init; }

    /// <summary>
    /// Optional local image MIME type hint. Present when <see cref="Type"/> is "localImage".
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// Optional local image file name hint. Present when <see cref="Type"/> is "localImage".
    /// </summary>
    public string? FileName { get; init; }
}

/// <summary>
/// Maps persisted Session Core models into wire DTOs.
/// </summary>
public static class SessionWireMapper
{
    private const string AttachedFileMarkerPrefix = "[[Attached File: ";
    private const string AttachedFileMarkerSuffix = "]]";

    /// <summary>
    /// Maps a thread into the wire DTO without turn history.
    /// Equivalent to <c>thread.ToWire(includeTurns: false)</c>.
    /// The AppServer should call <c>ToWire(includeTurns: true)</c> when serving thread/read responses.
    /// </summary>
    public static SessionWireThread ToWire(this SessionThread thread) =>
        thread.ToWire(includeTurns: false);

    /// <summary>
    /// Maps a thread into the wire DTO, optionally including turn history.
    /// </summary>
    public static SessionWireThread ToWire(this SessionThread thread, bool includeTurns) =>
        new()
        {
            Id = thread.Id,
            WorkspacePath = thread.WorkspacePath,
            UserId = thread.UserId,
            OriginChannel = thread.OriginChannel,
            ChannelContext = thread.ChannelContext,
            DisplayName = thread.DisplayName,
            Source = thread.Source,
            Status = thread.Status,
            CreatedAt = thread.CreatedAt,
            LastActiveAt = thread.LastActiveAt,
            HistoryMode = thread.HistoryMode,
            Configuration = thread.Configuration,
            Metadata = new Dictionary<string, string>(thread.Metadata),
            Runtime = ToRuntimeState(thread),
            QueuedInputs = thread.QueuedInputs.ToList(),
            Turns = includeTurns ? thread.Turns.Select(t => t.ToWire(includeItems: true)).ToList() : null
        };

    private static ThreadRuntimeState ToRuntimeState(SessionThread thread)
    {
        var runningTurn = thread.Turns.LastOrDefault(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval);
        var lastTurn = runningTurn ?? thread.Turns.LastOrDefault();
        return new ThreadRuntimeState
        {
            Running = runningTurn != null,
            WaitingOnApproval = runningTurn?.Status == TurnStatus.WaitingApproval,
            WaitingOnPlanConfirmation = lastTurn?.Status == TurnStatus.Completed
                && EndsWithSuccessfulCreatePlanInPlanMode(thread, lastTurn)
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

    /// <summary>
    /// Maps a turn into the wire DTO without item list.
    /// </summary>
    public static SessionWireTurn ToWire(this SessionTurn turn) =>
        turn.ToWire(includeItems: false);

    /// <summary>
    /// Maps a turn into the wire DTO, optionally including items.
    /// </summary>
    public static SessionWireTurn ToWire(this SessionTurn turn, bool includeItems) =>
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
            Initiator = turn.Initiator,
            Items = includeItems ? turn.Items.Select(i => i.ToWire()).ToList() : null
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
    /// Returns the JSON-RPC notification method name for a given <see cref="SessionEvent"/>.
    /// The AppServer must call this to determine the <c>"method"</c> field of each outbound notification.
    ///
    /// Key mapping for item delta events (both use <see cref="SessionEventType.ItemDelta"/> internally):
    /// <list type="bullet">
    /// <item><see cref="AgentMessageDelta"/> (<c>deltaKind = "agentMessage"</c>) → <c>"item/agentMessage/delta"</c></item>
    /// <item><see cref="ReasoningContentDelta"/> (<c>deltaKind = "reasoningContent"</c>) → <c>"item/reasoning/delta"</c></item>
    /// <item><see cref="CommandExecutionOutputDelta"/> (<c>deltaKind = "commandExecution"</c>) → <c>"item/commandExecution/outputDelta"</c></item>
    /// <item><see cref="ToolCallArgumentsDelta"/> (<c>deltaKind = "toolCallArguments"</c>) → <c>"item/toolCall/argumentsDelta"</c></item>
    /// </list>
    /// All other mappings are 1:1 with the <see cref="SessionEventType"/> name converted to camelCase slash-notation.
    /// </summary>
    public static string ToWireMethodName(this SessionEvent evt) =>
        evt.EventType switch
        {
            SessionEventType.ThreadCreated => "thread/started",
            SessionEventType.ThreadResumed => "thread/resumed",
            SessionEventType.ThreadStatusChanged => "thread/statusChanged",
            SessionEventType.ThreadQueueUpdated => "thread/queue/updated",
            SessionEventType.TurnStarted => "turn/started",
            SessionEventType.TurnCompleted => "turn/completed",
            SessionEventType.TurnFailed => "turn/failed",
            SessionEventType.TurnCancelled => "turn/cancelled",
            SessionEventType.ItemStarted => "item/started",
            // ItemDelta maps to two different methods depending on payload DeltaKind
            SessionEventType.ItemDelta when evt.Payload is CommandExecutionOutputDelta => "item/commandExecution/outputDelta",
            SessionEventType.ItemDelta when evt.Payload is ReasoningContentDelta => "item/reasoning/delta",
            SessionEventType.ItemDelta when evt.Payload is ToolCallArgumentsDelta => "item/toolCall/argumentsDelta",
            SessionEventType.ItemDelta => "item/agentMessage/delta",
            SessionEventType.ItemCompleted => "item/completed",
            SessionEventType.ApprovalRequested => "item/approval/request",
            SessionEventType.ApprovalResolved => "item/approval/resolved",
            SessionEventType.SubAgentProgress => "subagent/progress",
            SessionEventType.UsageDelta => "item/usage/delta",
            SessionEventType.SystemEvent => "system/event",
            _ => evt.EventType.ToString()
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
                // Include items in turn notifications so clients receive the full turn state
                SessionTurn turn => turn.ToWire(includeItems: true),
                SessionItem item => item.ToWire(),
                // Map ThreadResumedPayload to wire shape: { thread, resumedBy }
                ThreadResumedPayload resumed => new { thread = resumed.Thread.ToWire(), resumedBy = resumed.ResumedBy },
                // Map TurnCancelledPayload to wire shape: { turn, reason }
                TurnCancelledPayload cancelled => new { turn = cancelled.Turn.ToWire(includeItems: true), reason = cancelled.Reason },
                // Map TurnFailedPayload to wire shape: { turn, error }
                TurnFailedPayload failed => new { turn = failed.Turn.ToWire(includeItems: true), error = failed.Error },
                // Map ThreadStatusChangedPayload to wire shape: { threadId, previousStatus, newStatus }
                ThreadStatusChangedPayload statusChanged => new { threadId = evt.ThreadId, previousStatus = statusChanged.PreviousStatus, newStatus = statusChanged.NewStatus },
                ThreadQueueUpdatedPayload queueUpdated => new { threadId = queueUpdated.ThreadId, queuedInputs = queueUpdated.QueuedInputs },
                // Flatten delta payloads to { delta } string per spec Section 6.3
                AgentMessageDelta agentDelta => new { delta = agentDelta.TextDelta },
                ReasoningContentDelta reasoningDelta => new { delta = reasoningDelta.TextDelta },
                CommandExecutionOutputDelta commandDelta => new { delta = commandDelta.TextDelta },
                ToolCallArgumentsDelta toolCallDelta => new
                {
                    deltaKind = toolCallDelta.DeltaKind,
                    toolName = toolCallDelta.ToolName,
                    callId = toolCallDelta.CallId,
                    delta = toolCallDelta.Delta
                },
                // SubAgent progress: pass through the payload as-is (entries array serialized directly)
                SubAgentProgressPayload => evt.Payload,
                // System event: pass through the payload as-is (kind + message)
                SystemEventPayload => evt.Payload,
                _ => evt.Payload
            }
        };

    /// <summary>
    /// Converts a wire input part into a <see cref="AIContent"/> for use with <see cref="ISessionService.SubmitInputAsync"/>.
    /// For <c>text</c> parts, returns <see cref="TextContent"/> directly.
    /// For <c>image</c> and <c>localImage</c> parts, returns a <see cref="TextContent"/> placeholder
    /// because <see cref="DataContent"/> requires base64-encoded <c>data:</c> URIs.
    /// The AppServer is responsible for fetching/reading image bytes and constructing proper
    /// <see cref="DataContent"/> instances before passing them to <see cref="ISessionService.SubmitInputAsync"/>.
    /// </summary>
    public static AIContent ToAIContent(this SessionWireInputPart part) =>
        part.Type switch
        {
            "text" => new TextContent(ExpandAttachedFileMarkersForModel(part.Text ?? string.Empty)),
            "commandRef" => new TextContent(BuildCommandRefText(part)),
            "skillRef" => new TextContent(BuildSkillRefText(part)),
            "fileRef" => new TextContent(BuildFileRefText(part)),
            // image/localImage: AppServer must resolve to DataContent(bytes, mediaType) before dispatch
            "image" when part.Url is { } url => new TextContent($"[image:{url}]"),
            "localImage" when part.Path is { } path => new TextContent($"[localImage:{path}]"),
            _ => new TextContent(part.Text ?? string.Empty)
        };

    /// <summary>
    /// Builds the compatibility/display text used for user-message previews and
    /// fallback rendering from a sequence of native input parts.
    /// </summary>
    public static string BuildDisplayText(IEnumerable<SessionWireInputPart>? parts)
    {
        if (parts == null)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(part.Type switch
            {
                "text" => part.Text ?? string.Empty,
                "commandRef" => BuildCommandRefText(part),
                "skillRef" => $"${part.Name?.TrimStart('/', '$') ?? string.Empty}",
                "fileRef" => $"@{(part.DisplayPath ?? part.Path ?? string.Empty)}",
                _ => string.Empty
            });
        }

        return sb.ToString();
    }

    private static string ExpandAttachedFileMarkersForModel(string text)
    {
        var (files, bodyText) = ParseLeadingAttachedFileMarkers(text);
        if (files.Count == 0)
            return text;

        var pathBlock = string.Join('\n', files);
        return string.IsNullOrEmpty(bodyText)
            ? pathBlock
            : $"{pathBlock}\n\n{bodyText}";
    }

    private static (List<string> Files, string BodyText) ParseLeadingAttachedFileMarkers(string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var files = new List<string>();
        var index = 0;

        while (index < lines.Length)
        {
            var line = lines[index] ?? string.Empty;
            if (!line.StartsWith(AttachedFileMarkerPrefix, StringComparison.Ordinal)
                || !line.EndsWith(AttachedFileMarkerSuffix, StringComparison.Ordinal))
            {
                break;
            }

            var path = line.Substring(
                AttachedFileMarkerPrefix.Length,
                line.Length - AttachedFileMarkerPrefix.Length - AttachedFileMarkerSuffix.Length).Trim();
            if (string.IsNullOrWhiteSpace(path))
                break;

            files.Add(path);
            index++;
        }

        if (files.Count == 0)
            return (files, text);

        if (index < lines.Length && string.IsNullOrEmpty(lines[index]))
            index++;

        return (files, string.Join('\n', lines[index..]));
    }

    /// <summary>
    /// Converts an <see cref="AIContent"/> into a wire input part for serialization.
    /// <see cref="DataContent"/> instances carry base64 <c>data:</c> URIs and are mapped to
    /// the <c>"image"</c> wire type with the data URI as the URL field.
    /// </summary>
    public static SessionWireInputPart ToWireInputPart(this AIContent content) =>
        content switch
        {
            TextContent tc => new SessionWireInputPart { Type = "text", Text = tc.Text },
            DataContent dc => new SessionWireInputPart { Type = "image", Url = dc.Uri },
            _ => new SessionWireInputPart { Type = "text", Text = content.ToString() }
        };

    private static string? GetPayloadKind(object? payload) =>
        payload switch
        {
            AgentMessageDelta => "agentMessageDelta",
            ReasoningContentDelta => "reasoningContentDelta",
            CommandExecutionOutputDelta => "commandExecutionOutputDelta",
            ToolCallArgumentsDelta => "toolCallArgumentsDelta",
            CommandExecutionPayload => "commandExecution",
            ToolExecutionPayload => "toolExecution",
            ApprovalRequestPayload => "approvalRequest",
            ApprovalResponsePayload => "approvalResponse",
            ErrorPayload => "error",
            ToolCallPayload => "toolCall",
            PluginFunctionCallPayload => "pluginFunctionCall",
            DynamicToolCallPayload => "dynamicToolCall",
            ToolResultPayload => "toolResult",
            UserMessagePayload => "userMessage",
            AgentMessagePayload => "agentMessage",
            ReasoningContentPayload => "reasoningContent",
            SystemNoticePayload => "systemNotice",
            SessionThread => "thread",
            SessionTurn => "turn",
            SessionItem => "item",
            ThreadStatusChangedPayload => "threadStatusChanged",
            ThreadResumedPayload => "threadResumed",
            ThreadQueueUpdatedPayload => "threadQueueUpdated",
            TurnCancelledPayload => "turnCancelled",
            TurnFailedPayload => "turnFailed",
            SubAgentProgressPayload => "subAgentProgress",
            SystemEventPayload => "systemEvent",
            _ => null
        };

    private static string BuildCommandRefText(SessionWireInputPart part)
    {
        if (!string.IsNullOrWhiteSpace(part.RawText))
            return part.RawText.Trim();

        var name = part.Name?.Trim().TrimStart('/', '$') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var args = part.ArgsText?.Trim();
        return string.IsNullOrWhiteSpace(args)
            ? $"/{name}"
            : $"/{name} {args}";
    }

    private static string BuildSkillRefText(SessionWireInputPart part)
    {
        var name = part.Name?.Trim().TrimStart('/') ?? string.Empty;
        return string.IsNullOrWhiteSpace(name) ? string.Empty : $"${name}";
    }

    private static string BuildFileRefText(SessionWireInputPart part)
    {
        var path = (part.DisplayPath ?? part.Path ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(path) ? string.Empty : $"@{path}";
    }
}
