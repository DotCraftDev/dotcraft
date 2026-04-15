using System.Text.Json.Nodes;

namespace DotCraft.Protocol;

/// <summary>
/// Payload for UserMessage items.
/// </summary>
public sealed record UserMessagePayload
{
    public string Text { get; init; } = string.Empty;

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
