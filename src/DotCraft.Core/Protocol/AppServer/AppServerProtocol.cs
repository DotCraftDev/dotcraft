using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Protocol.AppServer;

// ───── Inbound message (parsed from wire) ─────

/// <summary>
/// Represents any incoming JSON-RPC 2.0 message from a wire client.
/// The handler discriminates by the presence/absence of <see cref="Method"/> and <see cref="Id"/>.
/// </summary>
public sealed class AppServerIncomingMessage
{
    [JsonPropertyName("jsonrpc")]
    public string? JsonRpc { get; set; }

    [JsonPropertyName("id")]
    public JsonElement? Id { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }

    [JsonPropertyName("params")]
    public JsonElement? Params { get; set; }

    [JsonPropertyName("result")]
    public JsonElement? Result { get; set; }

    [JsonPropertyName("error")]
    public JsonElement? Error { get; set; }

    /// <summary>Incoming message is a response to a server-initiated request (e.g. approval).</summary>
    public bool IsResponse => Method == null && Id.HasValue && Id.Value.ValueKind != JsonValueKind.Undefined;

    /// <summary>Incoming message is a notification (has method, no id).</summary>
    public bool IsNotification => Method != null && (!Id.HasValue || Id.Value.ValueKind == JsonValueKind.Null || Id.Value.ValueKind == JsonValueKind.Undefined);

    /// <summary>Incoming message is a request (has method and id).</summary>
    public bool IsRequest => Method != null && Id.HasValue && Id.Value.ValueKind != JsonValueKind.Null && Id.Value.ValueKind != JsonValueKind.Undefined;
}

// ───── initialize ─────

public sealed class AppServerInitializeParams
{
    public AppServerClientInfo ClientInfo { get; set; } = new();

    public AppServerClientCapabilities? Capabilities { get; set; }
}

public sealed class AppServerClientInfo
{
    public string Name { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string Version { get; set; } = string.Empty;
}

public sealed class AppServerClientCapabilities
{
    /// <summary>Whether the client can handle server-initiated approval requests. Default true.</summary>
    public bool? ApprovalSupport { get; set; }

    /// <summary>Whether the client can consume streaming delta notifications. Default true.</summary>
    public bool? StreamingSupport { get; set; }

    /// <summary>Exact notification method names to suppress for this connection.</summary>
    public List<string>? OptOutNotificationMethods { get; set; }
}

public sealed class AppServerInitializeResult
{
    public AppServerServerInfo ServerInfo { get; set; } = new();

    public AppServerServerCapabilities Capabilities { get; set; } = new();
}

public sealed class AppServerServerInfo
{
    public string Name { get; set; } = "dotcraft";

    public string Version { get; set; } = string.Empty;

    /// <summary>Wire protocol version. Currently "1".</summary>
    public string ProtocolVersion { get; set; } = "1";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Extensions { get; set; }
}

public sealed class AppServerServerCapabilities
{
    public bool ThreadManagement { get; set; } = true;

    public bool ThreadSubscriptions { get; set; } = true;

    public bool ApprovalFlow { get; set; } = true;

    public bool ModeSwitch { get; set; } = true;

    public bool ConfigOverride { get; set; } = true;
}

// ───── thread/start ─────

public sealed class ThreadStartParams
{
    public SessionIdentity Identity { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ThreadConfiguration? Config { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HistoryMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; set; }
}

// ───── thread/resume ─────

public sealed class ThreadResumeParams
{
    public string ThreadId { get; set; } = string.Empty;
}

// ───── thread/list ─────

public sealed class ThreadListParams
{
    public SessionIdentity Identity { get; set; } = new();

    public bool? IncludeArchived { get; set; }
}

public sealed class ThreadListResult
{
    public List<ThreadSummary> Data { get; set; } = [];
}

// ───── thread/read ─────

public sealed class ThreadReadParams
{
    public string ThreadId { get; set; } = string.Empty;

    public bool? IncludeTurns { get; set; }
}

// ───── thread/subscribe ─────

public sealed class ThreadSubscribeParams
{
    public string ThreadId { get; set; } = string.Empty;

    public bool? ReplayRecent { get; set; }
}

// ───── thread/unsubscribe ─────

public sealed class ThreadUnsubscribeParams
{
    public string ThreadId { get; set; } = string.Empty;
}

// ───── thread/pause, archive, delete ─────

public sealed class ThreadPauseParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ThreadArchiveParams
{
    public string ThreadId { get; set; } = string.Empty;
}

public sealed class ThreadDeleteParams
{
    public string ThreadId { get; set; } = string.Empty;
}

// ───── thread/mode/set ─────

public sealed class ThreadModeSetParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string Mode { get; set; } = string.Empty;
}

// ───── thread/config/update ─────

public sealed class ThreadConfigUpdateParams
{
    public string ThreadId { get; set; } = string.Empty;

    public ThreadConfiguration Config { get; set; } = new();
}

// ───── turn/start ─────

public sealed class TurnStartParams
{
    public string ThreadId { get; set; } = string.Empty;

    public List<SessionWireInputPart> Input { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SenderContext? Sender { get; set; }

    /// <summary>
    /// Client-provided conversation history for HistoryMode.Client threads.
    /// Kept as raw JsonElement to defer deserialization of ChatMessage[].
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Messages { get; set; }
}

// ───── turn/interrupt ─────

public sealed class TurnInterruptParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;
}

// ───── item/approval/request (Server → Client request) ─────

public sealed class AppServerApprovalRequestParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string TurnId { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;

    public string RequestId { get; set; } = string.Empty;

    /// <summary>"shell" or "file"</summary>
    public string ApprovalType { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    public string Target { get; set; } = string.Empty;

    public string ScopeKey { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}

// ───── item/approval/request response (Client → Server) ─────

public sealed class AppServerApprovalResponseResult
{
    /// <summary>One of: "accept", "acceptForSession", "decline", "cancel".</summary>
    public string Decision { get; set; } = string.Empty;
}

// ───── Wire protocol method name constants ─────

public static class AppServerMethods
{
    // Client → Server requests
    public const string Initialize = "initialize";
    public const string ThreadStart = "thread/start";
    public const string ThreadResume = "thread/resume";
    public const string ThreadList = "thread/list";
    public const string ThreadRead = "thread/read";
    public const string ThreadSubscribe = "thread/subscribe";
    public const string ThreadUnsubscribe = "thread/unsubscribe";
    public const string ThreadPause = "thread/pause";
    public const string ThreadArchive = "thread/archive";
    public const string ThreadDelete = "thread/delete";
    public const string ThreadModeSet = "thread/mode/set";
    public const string ThreadConfigUpdate = "thread/config/update";
    public const string TurnStart = "turn/start";
    public const string TurnInterrupt = "turn/interrupt";

    // Client → Server notification (no id)
    public const string Initialized = "initialized";

    // Server → Client notifications
    public const string ThreadStarted = "thread/started";
    public const string ThreadResumed = "thread/resumed";
    public const string ThreadStatusChanged = "thread/statusChanged";
    public const string TurnStarted = "turn/started";
    public const string TurnCompleted = "turn/completed";
    public const string TurnFailed = "turn/failed";
    public const string TurnCancelled = "turn/cancelled";
    public const string ItemStarted = "item/started";
    public const string ItemAgentMessageDelta = "item/agentMessage/delta";
    public const string ItemReasoningDelta = "item/reasoning/delta";
    public const string ItemCompleted = "item/completed";
    public const string ItemApprovalResolved = "item/approval/resolved";

    // Server → Client request (bidirectional approval)
    public const string ItemApprovalRequest = "item/approval/request";

    // Server → Client notification (SubAgent progress)
    public const string SubAgentProgress = "subagent/progress";
}
