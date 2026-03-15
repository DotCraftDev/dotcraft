using System.Text.Json.Nodes;

namespace DotCraft.Sessions.Protocol;

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
    public string TextDelta { get; init; } = string.Empty;
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
