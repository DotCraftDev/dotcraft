using System.Text.Json.Nodes;

namespace DotCraft.Protocol;

/// <summary>
/// Metadata for a user-provided local image attachment.
/// </summary>
public sealed record UserMessageImage
{
    /// <summary>
    /// Absolute path to the local image attachment.
    /// </summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>
    /// Optional MIME type hint supplied by the input channel.
    /// </summary>
    public string? MimeType { get; init; }

    /// <summary>
    /// Optional original file name supplied by the input channel.
    /// </summary>
    public string? FileName { get; init; }
}

/// <summary>
/// Payload for UserMessage items.
/// </summary>
public sealed record UserMessagePayload
{
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// How this user message entered the turn: normal, queued follow-up, or
    /// same-turn guidance.
    /// </summary>
    public string? DeliveryMode { get; init; }

    /// <summary>
    /// Native transport-level input parts captured as the source of truth for
    /// history rendering and draft rehydration.
    /// </summary>
    public IReadOnlyList<SessionWireInputPart>? NativeInputParts { get; init; }

    /// <summary>
    /// Materialized input parts captured after transport-side expansion, matching
    /// the content snapshot that was sent to the model for this turn.
    /// </summary>
    public IReadOnlyList<SessionWireInputPart>? MaterializedInputParts { get; init; }

    /// <summary>
    /// Individual sender within a group session (nullable for single-user channels).
    /// </summary>
    public string? SenderId { get; init; }

    /// <summary>
    /// Display name of the sender (nullable).
    /// </summary>
    public string? SenderName { get; init; }

    /// <summary>
    /// Role of the sender when the originating channel provides it.
    /// </summary>
    public string? SenderRole { get; init; }

    /// <summary>
    /// Originating channel for this user message.
    /// </summary>
    public string? ChannelName { get; init; }

    /// <summary>
    /// Channel-specific context for this user message.
    /// </summary>
    public string? ChannelContext { get; init; }

    /// <summary>
    /// Group or chat identifier when the message originates from a group context.
    /// </summary>
    public string? GroupId { get; init; }

    /// <summary>
    /// Optional local image metadata used by clients to rehydrate user message attachments.
    /// </summary>
    public IReadOnlyList<UserMessageImage>? Images { get; init; }

    /// <summary>
    /// Non-null when this user message was synthesized by an automation mechanism
    /// (heartbeat, cron, automations) rather than typed by a human. Clients use this
    /// to render a "Sent via automation" affordance.
    /// </summary>
    public string? TriggerKind { get; init; }

    /// <summary>
    /// Optional human-readable label for the automation source (e.g. cron job name, local task identifier).
    /// </summary>
    public string? TriggerLabel { get; init; }

    /// <summary>
    /// Optional routing id for client-side click-through (e.g. cron job id, task id).
    /// </summary>
    public string? TriggerRefId { get; init; }
}

/// <summary>
/// Optional transport-supplied input snapshots associated with a turn submission.
/// </summary>
public sealed record SessionInputSnapshot
{
    /// <summary>
    /// Native transport parts (commandRef/skillRef/fileRef/text/etc.) as supplied by the client.
    /// </summary>
    public IReadOnlyList<SessionWireInputPart>? NativeInputParts { get; init; }

    /// <summary>
    /// Materialized parts after transport-side expansion, aligned to the content actually sent to the model.
    /// </summary>
    public IReadOnlyList<SessionWireInputPart>? MaterializedInputParts { get; init; }

    /// <summary>
    /// Compatibility/display text derived from the native transport parts.
    /// </summary>
    public string? DisplayText { get; init; }

    /// <summary>
    /// Optional delivery mode carried into the persisted UserMessage payload.
    /// </summary>
    public string? DeliveryMode { get; init; }
}

/// <summary>
/// Persisted FIFO input queued while a thread already has an active turn.
/// </summary>
public sealed record QueuedTurnInput
{
    public string Id { get; init; } = string.Empty;

    public string ThreadId { get; init; } = string.Empty;

    public IReadOnlyList<SessionWireInputPart> NativeInputParts { get; init; } = [];

    public IReadOnlyList<SessionWireInputPart> MaterializedInputParts { get; init; } = [];

    public string DisplayText { get; init; } = string.Empty;

    public SenderContext? Sender { get; init; }

    public string Status { get; init; } = "queued";

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? ReadyAfterTurnId { get; init; }
}

/// <summary>
/// Result returned when a queued input is promoted to current-turn guidance.
/// </summary>
public sealed record TurnSteerResult
{
    public string TurnId { get; init; } = string.Empty;

    public IReadOnlyList<QueuedTurnInput> QueuedInputs { get; init; } = [];
}

/// <summary>
/// Payload for AgentMessage items (final, after streaming).
/// </summary>
public sealed record AgentMessagePayload
{
    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// Delta payload emitted during AgentMessage streaming (item/delta events).
/// </summary>
public sealed record AgentMessageDelta
{
    public string DeltaKind { get; init; } = "agentMessage";

    public string TextDelta { get; init; } = string.Empty;
}

/// <summary>
/// Payload for ReasoningContent items.
/// </summary>
public sealed record ReasoningContentPayload
{
    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// Delta payload emitted during ReasoningContent streaming.
/// </summary>
public sealed record ReasoningContentDelta
{
    public string DeltaKind { get; init; } = "reasoningContent";

    public string TextDelta { get; init; } = string.Empty;
}

/// <summary>
/// Payload for CommandExecution items.
/// </summary>
public sealed record CommandExecutionPayload
{
    public string Command { get; init; } = string.Empty;

    public string WorkingDirectory { get; init; } = string.Empty;

    /// <summary>
    /// "host" or "sandbox".
    /// </summary>
    public string Source { get; init; } = "host";

    /// <summary>
    /// "inProgress", "completed", "failed", or "cancelled".
    /// </summary>
    public string Status { get; init; } = "inProgress";

    public string AggregatedOutput { get; init; } = string.Empty;

    public int? ExitCode { get; init; }

    public long? DurationMs { get; init; }

    /// <summary>
    /// Matches the related ToolCall/ToolResult call id when available.
    /// </summary>
    public string? CallId { get; init; }
}

/// <summary>
/// Delta payload emitted while a command execution is producing output.
/// </summary>
public sealed record CommandExecutionOutputDelta
{
    public string DeltaKind { get; init; } = "commandExecution";

    public string TextDelta { get; init; } = string.Empty;
}

/// <summary>
/// Delta payload emitted while tool-call arguments are still streaming.
/// </summary>
public sealed record ToolCallArgumentsDelta
{
    public string DeltaKind { get; init; } = "toolCallArguments";

    /// <summary>
    /// Tool name (typically present on the first chunk).
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// Tool call id (typically present on the first chunk).
    /// </summary>
    public string? CallId { get; init; }

    /// <summary>
    /// Raw JSON fragment emitted by the model for the tool-call arguments.
    /// </summary>
    public string Delta { get; init; } = string.Empty;
}

/// <summary>
/// Payload for ToolCall items.
/// </summary>
public sealed record ToolCallPayload
{
    public string ToolName { get; init; } = string.Empty;

    /// <summary>
    /// Tool arguments as a JSON object.
    /// </summary>
    public JsonObject? Arguments { get; init; }

    /// <summary>
    /// Correlation ID linking this ToolCall to its ToolResult.
    /// </summary>
    public string CallId { get; init; } = string.Empty;
}

/// <summary>
/// Payload for ExternalChannelToolCall items.
/// </summary>
public sealed record ExternalChannelToolCallPayload
{
    public string ToolName { get; init; } = string.Empty;

    public string CallId { get; init; } = string.Empty;

    public string ChannelName { get; init; } = string.Empty;

    public bool RequiresChatContext { get; init; }

    public JsonObject? Arguments { get; init; }

    public string? Result { get; init; }

    public bool Success { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Payload for ToolResult items.
/// </summary>
public sealed record ToolResultPayload
{
    /// <summary>
    /// Matches the ToolCall.CallId.
    /// </summary>
    public string CallId { get; init; } = string.Empty;

    public string Result { get; init; } = string.Empty;

    public bool Success { get; init; }
}

/// <summary>
/// Payload for ApprovalRequest items.
/// </summary>
public sealed record ApprovalRequestPayload
{
    /// <summary>
    /// "file" or "shell"
    /// </summary>
    public string ApprovalType { get; init; } = string.Empty;

    /// <summary>
    /// For file: "read", "write", "edit", "list". For shell: the command.
    /// </summary>
    public string Operation { get; init; } = string.Empty;

    /// <summary>
    /// For file: the path. For shell: the working directory.
    /// </summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>
    /// Unique ID for correlating with ApprovalResponse.
    /// </summary>
    public string RequestId { get; init; } = string.Empty;

    /// <summary>
    /// Session-scoped cache key for repeated approvals of the same class of operation.
    /// </summary>
    public string ScopeKey { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable explanation of why this approval is needed, shown to the user in approval UIs.
    /// </summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Payload for ApprovalResponse items.
/// </summary>
public sealed record ApprovalResponsePayload
{
    /// <summary>
    /// Matches the ApprovalRequest.RequestId.
    /// </summary>
    public string RequestId { get; init; } = string.Empty;

    public bool Approved { get; init; }

    /// <summary>
    /// Rich decision captured for the request.
    /// </summary>
    public SessionApprovalDecision Decision { get; init; } = SessionApprovalDecision.Reject;
}

/// <summary>
/// Payload for SystemNotice items. Currently used to mark points where the
/// context was compacted so clients can render a persistent divider in the
/// conversation timeline.
/// </summary>
public sealed record SystemNoticePayload
{
    /// <summary>
    /// Notice classifier. Currently only <c>"compacted"</c> is emitted; leaving
    /// this as a string keeps future kinds (e.g. <c>"memoryConsolidated"</c>)
    /// additive without rev'ing the wire protocol.
    /// </summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// How compaction was triggered. One of: <c>"auto"</c>, <c>"reactive"</c>,
    /// <c>"manual"</c>.
    /// </summary>
    public string Trigger { get; init; } = string.Empty;

    /// <summary>
    /// Which compaction mode actually ran. One of: <c>"micro"</c>, <c>"partial"</c>.
    /// </summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>
    /// Approximate input token count right before compaction ran.
    /// </summary>
    public long TokensBefore { get; init; }

    /// <summary>
    /// Approximate input token count after compaction ran.
    /// </summary>
    public long TokensAfter { get; init; }

    /// <summary>
    /// Percent of the effective context window still available after compaction (0.0 - 1.0).
    /// </summary>
    public double PercentLeftAfter { get; init; }

    /// <summary>
    /// Number of tool results cleared by the microcompact pass (0 when only
    /// the partial summary ran).
    /// </summary>
    public int ClearedToolResults { get; init; }
}

/// <summary>
/// Payload for Error items.
/// </summary>
public sealed record ErrorPayload
{
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Machine-readable error code (e.g., "agent_error", "timeout").
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    /// Whether this error terminates the Turn.
    /// </summary>
    public bool Fatal { get; init; }
}
