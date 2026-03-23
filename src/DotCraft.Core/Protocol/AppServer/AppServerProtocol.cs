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

    /// <summary>
    /// Channel adapter capability (external-channel-adapter.md §5.1).
    /// Null for regular clients (CLI, VS Code, etc.).
    /// When present, identifies this connection as an external channel adapter.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ChannelAdapterCapability? ChannelAdapter { get; set; }
}

/// <summary>
/// Declares that this client is an external channel adapter.
/// Sent inside <see cref="AppServerClientCapabilities"/> during the initialize handshake.
/// See external-channel-adapter.md §5.1.
/// </summary>
public sealed class ChannelAdapterCapability
{
    /// <summary>
    /// Canonical channel name (e.g. "telegram"). Must match the server-side
    /// ExternalChannels configuration key.
    /// </summary>
    public string ChannelName { get; set; } = string.Empty;

    /// <summary>
    /// Whether this adapter can receive <c>ext/channel/deliver</c> requests.
    /// Defaults to true when not specified.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DeliverySupport { get; set; }
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

    /// <summary>
    /// Server supports cron management methods (cron/list, cron/remove, cron/enable).
    /// False when the cron service is not configured. See spec Section 16.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CronManagement { get; set; }

    /// <summary>
    /// Server supports heartbeat management methods (heartbeat/trigger).
    /// False when the heartbeat service is not configured. See spec Section 17.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool HeartbeatManagement { get; set; }

    /// <summary>
    /// Server supports skills management methods (skills/list, skills/read, skills/setEnabled). See spec Section 18.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool SkillsManagement { get; set; }

    /// <summary>
    /// Server supports automation task methods (automation/task/*).
    /// False when the Automations module is not loaded.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Automations { get; set; }
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

    /// <summary>
    /// When set, only threads whose <c>originChannel</c> matches (case-insensitive) are returned.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChannelName { get; set; }
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

// ───── thread/rename ─────

public sealed class ThreadRenameParams
{
    public string ThreadId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
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

// ───── cron/list ─────

public sealed class CronListParams
{
    /// <summary>When true, disabled jobs are included. Default false.</summary>
    public bool IncludeDisabled { get; set; }
}

public sealed class CronListResult
{
    public List<CronJobWireInfo> Jobs { get; set; } = [];
}

// ───── cron/remove ─────

public sealed class CronRemoveParams
{
    public string JobId { get; set; } = string.Empty;
}

public sealed class CronRemoveResult
{
    public bool Removed { get; set; }
}

// ───── cron/enable ─────

public sealed class CronEnableParams
{
    public string JobId { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}

public sealed class CronEnableResult
{
    public CronJobWireInfo Job { get; set; } = new();
}

// ───── heartbeat/trigger (spec Section 17.2) ─────

public sealed class HeartbeatTriggerResult
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Result { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}

// ───── CronJobInfo wire DTO (spec Section 16.2) ─────

/// <summary>
/// Transport-safe projection of the internal CronJob domain model.
/// Used in cron/list and cron/enable results.
/// </summary>
public sealed class CronJobWireInfo
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public CronScheduleWireInfo Schedule { get; set; } = new();

    public bool Enabled { get; set; }

    public long CreatedAtMs { get; set; }

    public bool DeleteAfterRun { get; set; }

    public CronJobStateWireInfo State { get; set; } = new();
}

public sealed class CronScheduleWireInfo
{
    /// <summary>"every" or "at"</summary>
    public string Kind { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? EveryMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? AtMs { get; set; }
}

public sealed class CronJobStateWireInfo
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? NextRunAtMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? LastRunAtMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastStatus { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastError { get; set; }
}

// ───── skills/* (spec Section 18) ─────

public sealed class SkillsListParams
{
    /// <summary>When false, skills with unmet requirements are excluded. Default true.</summary>
    public bool? IncludeUnavailable { get; set; }
}

public sealed class SkillsListResult
{
    public List<SkillInfoWire> Skills { get; set; } = [];
}

/// <summary>
/// Wire projection of <see cref="DotCraft.Skills.SkillsLoader.SkillInfo"/> for skills/list and skills/setEnabled.
/// </summary>
public sealed class SkillInfoWire
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public bool Available { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UnavailableReason { get; set; }

    public bool Enabled { get; set; } = true;

    public string Path { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class SkillsReadParams
{
    public string Name { get; set; } = string.Empty;
}

public sealed class SkillsReadResult
{
    public string Name { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Metadata { get; set; }
}

public sealed class SkillsSetEnabledParams
{
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; }
}

public sealed class SkillsSetEnabledResult
{
    public SkillInfoWire Skill { get; set; } = new();
}

// ───── automation/task/* DTOs (M6) ─────

public sealed class AutomationTaskWire
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ThreadId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AgentSummary { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class AutomationTaskListParams
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkspacePath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceName { get; set; }
}

public sealed class AutomationTaskListResult
{
    public List<AutomationTaskWire> Tasks { get; set; } = [];
}

public sealed class AutomationTaskReadParams
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
}

public sealed class AutomationTaskCreateParams
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkflowTemplate { get; set; }
}

public sealed class AutomationTaskCreateResult
{
    public string TaskId { get; set; } = string.Empty;
    public string TaskDirectory { get; set; } = string.Empty;
}

public sealed class AutomationTaskApproveParams
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
}

public sealed class AutomationTaskRejectParams
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; set; }
}

public sealed class AutomationTaskDeleteParams
{
    public string WorkspacePath { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
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
    public const string ThreadRename = "thread/rename";
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

    // Server → Client notification (incremental token usage)
    public const string ItemUsageDelta = "item/usage/delta";

    // Server → Client notification (system maintenance events)
    public const string SystemEvent = "system/event";

    // Server → Client notification (plan/todo progress updates, spec Section 6.8)
    public const string PlanUpdated = "plan/updated";

    // Server → Client notification (cron/heartbeat job result, spec Section 6.9)
    public const string SystemJobResult = "system/jobResult";

    // Server → Client requests (external channel adapter, ext-channel-adapter spec §6)
    public const string ExtChannelDeliver = "ext/channel/deliver";
    public const string ExtChannelHeartbeat = "ext/channel/heartbeat";

    // Client → Server requests (cron management, spec Section 16)
    public const string CronList = "cron/list";
    public const string CronRemove = "cron/remove";
    public const string CronEnable = "cron/enable";

    // Client → Server requests (heartbeat management, spec Section 17)
    public const string HeartbeatTrigger = "heartbeat/trigger";

    // Client → Server requests (skills management, spec Section 18)
    public const string SkillsList = "skills/list";
    public const string SkillsRead = "skills/read";
    public const string SkillsSetEnabled = "skills/setEnabled";

    // Client → Server requests (automations, M6)
    public const string AutomationTaskList = "automation/task/list";
    public const string AutomationTaskRead = "automation/task/read";
    public const string AutomationTaskCreate = "automation/task/create";
    public const string AutomationTaskApprove = "automation/task/approve";
    public const string AutomationTaskReject = "automation/task/reject";
    public const string AutomationTaskDelete = "automation/task/delete";

    // Server → Client notification (automations, M6)
    public const string AutomationTaskUpdated = "automation/task/updated";
}
