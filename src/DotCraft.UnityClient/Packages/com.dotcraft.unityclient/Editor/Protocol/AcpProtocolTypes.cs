using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Editor.Protocol
{
    #region ACP Initialize

    /// <summary>
    /// Parameters for the initialize method.
    /// </summary>
    public sealed class InitializeParams
    {
        [JsonPropertyName("protocolVersion")]
        public int ProtocolVersion { get; set; }

        [JsonPropertyName("clientCapabilities")]
        public ClientCapabilities ClientCapabilities { get; set; }

        [JsonPropertyName("clientInfo")]
        public ClientInfo ClientInfo { get; set; }
    }

    public sealed class ClientInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }
    }

    public sealed class ClientCapabilities
    {
        [JsonPropertyName("fs")]
        [JsonConverter(typeof(BoolOrObjectConverter<FsCapabilities>))]
        public FsCapabilities Fs { get; set; }

        [JsonPropertyName("terminal")]
        [JsonConverter(typeof(BoolOrObjectConverter<TerminalCapabilities>))]
        public TerminalCapabilities Terminal { get; set; }

        /// <summary>
        /// Extension method prefixes supported by this client (e.g. ["_unity"]).
        /// The agent uses this list to decide which extension tool families to register.
        /// </summary>
        [JsonPropertyName("extensions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[] Extensions { get; set; }
    }

    public sealed class FsCapabilities
    {
        [JsonPropertyName("readTextFile")]
        public bool ReadTextFile { get; set; }

        [JsonPropertyName("writeTextFile")]
        public bool WriteTextFile { get; set; }

        public static FsCapabilities All => new() { ReadTextFile = true, WriteTextFile = true };
    }

    public sealed class TerminalCapabilities
    {
        [JsonPropertyName("create")]
        public bool Create { get; set; }

        public static TerminalCapabilities All => new() { Create = true };
    }

    /// <summary>
    /// Handles ACP capability fields that may be either a boolean shorthand or an object.
    /// </summary>
    public sealed class BoolOrObjectConverter<T> : JsonConverter<T> where T : class
    {
        public override T Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.True)
            {
                var allProp = typeToConvert.GetProperty("All",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                return allProp?.GetValue(null) as T
                       ?? System.Activator.CreateInstance<T>();
            }

            if (reader.TokenType == JsonTokenType.False || reader.TokenType == JsonTokenType.Null)
                return null;

            return JsonSerializer.Deserialize<T>(ref reader, options);
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }

    public sealed class InitializeResult
    {
        [JsonPropertyName("protocolVersion")]
        public int ProtocolVersion { get; set; }

        [JsonPropertyName("agentCapabilities")]
        public AgentCapabilities AgentCapabilities { get; set; } = new();

        [JsonPropertyName("agentInfo")]
        public AgentInfo AgentInfo { get; set; } = new();

        [JsonPropertyName("authMethods")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AuthMethod[] AuthMethods { get; set; }
    }

    #endregion

    #region ACP Authentication

    public sealed class AuthMethod
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Description { get; set; }
    }

    public sealed class AuthenticateParams
    {
        [JsonPropertyName("methodId")]
        public string MethodId { get; set; } = "";
    }

    public sealed class AuthenticateResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
    }

    public sealed class AgentCapabilities
    {
        [JsonPropertyName("loadSession")]
        public bool LoadSession { get; set; }

        [JsonPropertyName("listSessions")]
        public bool ListSessions { get; set; }

        [JsonPropertyName("promptCapabilities")]
        public PromptCapabilities PromptCapabilities { get; set; }

        [JsonPropertyName("_meta")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AgentCapabilitiesMeta Meta { get; set; }
    }

    public sealed class AgentCapabilitiesMeta
    {
        [JsonPropertyName("dotcraft")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public DotCraftAgentCapabilities DotCraft { get; set; }
    }

    public sealed class DotCraftAgentCapabilities
    {
        [JsonPropertyName("sessionDelete")]
        public bool SessionDelete { get; set; }
    }

    public sealed class PromptCapabilities
    {
        [JsonPropertyName("text")]
        public bool Text { get; set; } = true;

        [JsonPropertyName("image")]
        public bool Image { get; set; }

        [JsonPropertyName("audio")]
        public bool Audio { get; set; }

        [JsonPropertyName("embeddedContext")]
        public bool EmbeddedContext { get; set; }
    }

    public sealed class AgentInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }
    }

    #endregion

    #region ACP Session/New

    /// <summary>ACP spec: env entry for MCP server (array of name/value).</summary>
    public sealed class AcpEnvVariable
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("value")]
        public string Value { get; set; } = "";
    }

    /// <summary>ACP spec: HTTP header for MCP server (array of name/value).</summary>
    public sealed class AcpHttpHeader
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("value")]
        public string Value { get; set; } = "";
    }

    public sealed class SessionNewParams
    {
        [JsonPropertyName("cwd")]
        public string Cwd { get; set; }

        [JsonPropertyName("mcpServers")]
        public List<AcpMcpServer> McpServers { get; set; }
    }

    public sealed class AcpMcpServer
    {
        [JsonPropertyName("type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Type { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("command")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Command { get; set; }

        [JsonPropertyName("args")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string> Args { get; set; }

        [JsonPropertyName("env")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<AcpEnvVariable> Env { get; set; }

        [JsonPropertyName("url")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Url { get; set; }

        [JsonPropertyName("headers")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<AcpHttpHeader> Headers { get; set; }
    }

    public sealed class SessionNewResult
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("configOptions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ConfigOption> ConfigOptions { get; set; }
    }

    #endregion

    #region ACP Session/Load

    public sealed class SessionLoadParams
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("cwd")]
        public string Cwd { get; set; }

        [JsonPropertyName("mcpServers")]
        public List<AcpMcpServer> McpServers { get; set; }
    }

    public sealed class SessionLoadResult
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("configOptions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ConfigOption> ConfigOptions { get; set; }
    }

    #endregion

    #region ACP Session/List

    public sealed class SessionListParams
    {
        [JsonPropertyName("cwd")]
        public string Cwd { get; set; }

        [JsonPropertyName("cursor")]
        public string Cursor { get; set; }
    }

    public sealed class SessionListResult
    {
        [JsonPropertyName("sessions")]
        public List<SessionListEntry> Sessions { get; set; } = new();

        [JsonPropertyName("nextCursor")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string NextCursor { get; set; }
    }

    public sealed class SessionListEntry
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("updatedAt")]
        public string UpdatedAt { get; set; }

        [JsonPropertyName("cwd")]
        public string Cwd { get; set; }
    }

    #endregion

    #region DotCraft Session Delete

    public sealed class SessionDeleteParams
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = "";
    }

    public sealed class SessionDeleteResult
    {
    }

    #endregion

    #region ACP Session/Prompt

    public sealed class SessionPromptParams
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("prompt")]
        public List<AcpContentBlock> Prompt { get; set; } = new();

        [JsonPropertyName("command")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Command { get; set; }
    }

    public sealed class AcpContentBlock
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "text";

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Text { get; set; }

        [JsonPropertyName("data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Data { get; set; }

        [JsonPropertyName("mimeType")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string MimeType { get; set; }

        [JsonPropertyName("resource")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AcpEmbeddedResource Resource { get; set; }
    }

    public sealed class AcpEmbeddedResource
    {
        [JsonPropertyName("uri")]
        public string Uri { get; set; } = "";

        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; }

        [JsonPropertyName("text")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Text { get; set; }
    }

    public sealed class SessionPromptResult
    {
        [JsonPropertyName("stopReason")]
        public string StopReason { get; set; } = "end_turn";
    }

    #endregion

    #region ACP Session/Update (Notification)

    public sealed class SessionUpdateParams
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("update")]
        public AcpSessionUpdate Update { get; set; } = new();
    }

    public sealed class AcpSessionUpdate
    {
        [JsonPropertyName("sessionUpdate")]
        public string SessionUpdate { get; set; } = "";

        [JsonPropertyName("content")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object Content { get; set; }

        [JsonPropertyName("toolCallId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ToolCallId { get; set; }

        [JsonPropertyName("title")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Title { get; set; }

        [JsonPropertyName("kind")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Kind { get; set; }

        [JsonPropertyName("status")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Status { get; set; }

        [JsonPropertyName("fileLocations")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<AcpFileLocation> FileLocations { get; set; }

        [JsonPropertyName("entries")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<AcpPlanEntry> Entries { get; set; }

        [JsonPropertyName("commands")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<AcpSlashCommand> Commands { get; set; }

        [JsonPropertyName("configOptions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<ConfigOption> ConfigOptions { get; set; }
    }

    public sealed class AcpFileLocation
    {
        [JsonPropertyName("uri")]
        public string Uri { get; set; } = "";
    }

    public sealed class AcpPlanEntry
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = "";

        [JsonPropertyName("priority")]
        public string Priority { get; set; } = AcpPlanEntryPriority.Medium;

        [JsonPropertyName("status")]
        public string Status { get; set; } = AcpToolStatus.Pending;
    }

    #endregion

    #region ACP Session/SetConfigOption

    public sealed class SessionSetConfigOptionParams
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("configId")]
        public string ConfigId { get; set; } = "";

        [JsonPropertyName("value")]
        public string Value { get; set; } = "";
    }

    public sealed class SessionSetConfigOptionResult
    {
        [JsonPropertyName("configOptions")]
        public List<ConfigOption> ConfigOptions { get; set; } = new();
    }

    #endregion

    #region ACP Session/Cancel (Notification)

    public sealed class SessionCancelParams
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = "";
    }

    #endregion

    #region ACP RequestPermission (Agent → Client)

    public sealed class RequestPermissionParams
    {
        [JsonPropertyName("sessionId")]
        public string SessionId { get; set; } = "";

        [JsonPropertyName("toolCall")]
        public AcpToolCallInfo ToolCall { get; set; } = new();

        [JsonPropertyName("options")]
        public List<PermissionOption> Options { get; set; } = new();
    }

    public sealed class AcpToolCallInfo
    {
        [JsonPropertyName("toolCallId")]
        public string ToolCallId { get; set; } = "";

        [JsonPropertyName("title")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Title { get; set; }

        [JsonPropertyName("kind")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Kind { get; set; }

        [JsonPropertyName("status")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Status { get; set; }
    }

    public sealed class PermissionOption
    {
        [JsonPropertyName("optionId")]
        public string OptionId { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "";
    }

    public sealed class RequestPermissionResult
    {
        [JsonPropertyName("outcome")]
        public PermissionOutcome Outcome { get; set; } = new();
    }

    public sealed class PermissionOutcome
    {
        [JsonPropertyName("outcome")]
        public string Outcome { get; set; } = "";

        [JsonPropertyName("optionId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string OptionId { get; set; }
    }

    #endregion

    #region ACP fs/readTextFile (Agent → Client)

    public sealed class FsReadTextFileParams
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("offset")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Offset { get; set; }

        [JsonPropertyName("limit")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Limit { get; set; }
    }

    public sealed class FsReadTextFileResult
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    #endregion

    #region ACP fs/writeTextFile (Agent → Client)

    public sealed class FsWriteTextFileParams
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    public sealed class FsWriteTextFileResult
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }
    }

    #endregion

    #region ACP terminal/* (Agent → Client)

    public sealed class TerminalCreateParams
    {
        [JsonPropertyName("command")]
        public string Command { get; set; } = "";

        [JsonPropertyName("cwd")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Cwd { get; set; }

        [JsonPropertyName("env")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string> Env { get; set; }
    }

    public sealed class TerminalCreateResult
    {
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; set; } = "";
    }

    public sealed class TerminalGetOutputParams
    {
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; set; } = "";
    }

    public sealed class TerminalGetOutputResult
    {
        [JsonPropertyName("output")]
        public string Output { get; set; } = "";

        [JsonPropertyName("exitCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? ExitCode { get; set; }
    }

    public sealed class TerminalWaitForExitParams
    {
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; set; } = "";

        [JsonPropertyName("timeout")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Timeout { get; set; }
    }

    public sealed class TerminalKillParams
    {
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; set; } = "";
    }

    public sealed class TerminalReleaseParams
    {
        [JsonPropertyName("terminalId")]
        public string TerminalId { get; set; } = "";
    }

    #endregion

    #region ACP Slash Commands

    public sealed class AcpSlashCommand
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Description { get; set; }

        [JsonPropertyName("inputHint")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string InputHint { get; set; }
    }

    #endregion

    #region ACP Config Options

    public sealed class ConfigOption
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Description { get; set; }

        [JsonPropertyName("category")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Category { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "select";

        [JsonPropertyName("currentValue")]
        public string CurrentValue { get; set; } = "";

        [JsonPropertyName("options")]
        public List<ConfigOptionValue> Options { get; set; } = new();
    }

    public sealed class ConfigOptionValue
    {
        [JsonPropertyName("value")]
        public string Value { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Description { get; set; }
    }

    #endregion

    #region ACP Constants

    public static class AcpMethods
    {
        // Client → Agent
        public const string Initialize = "initialize";
        public const string Authenticate = "authenticate";
        public const string SessionNew = "session/new";
        public const string SessionLoad = "session/load";
        public const string SessionList = "session/list";
        public const string DotCraftSessionDelete = "_dotcraft/session_delete";
        public const string SessionPrompt = "session/prompt";
        public const string SessionCancel = "session/cancel";
        public const string SessionSetConfigOption = "session/set_config_option";

        // Agent → Client
        public const string SessionUpdate = "session/update";
        public const string RequestPermission = "session/request_permission";
        public const string FsReadTextFile = "fs/readTextFile";
        public const string FsWriteTextFile = "fs/writeTextFile";
        public const string TerminalCreate = "terminal/create";
        public const string TerminalGetOutput = "terminal/getOutput";
        public const string TerminalWaitForExit = "terminal/waitForExit";
        public const string TerminalKill = "terminal/kill";
        public const string TerminalRelease = "terminal/release";
    }

    public static class AcpToolKind
    {
        public const string Read = "read";
        public const string Edit = "edit";
        public const string Delete = "delete";
        public const string Move = "move";
        public const string Search = "search";
        public const string Execute = "execute";
        public const string Think = "think";
        public const string Fetch = "fetch";
        public const string Unity = "unity";
        public const string Other = "other";
    }

    public static class AcpToolStatus
    {
        public const string Pending = "pending";
        public const string InProgress = "in_progress";
        public const string Completed = "completed";
        public const string Failed = "failed";
    }

    public static class AcpPlanEntryPriority
    {
        public const string High = "high";
        public const string Medium = "medium";
        public const string Low = "low";
    }

    public static class AcpPermissionKind
    {
        public const string AllowOnce = "allow_once";
        public const string AllowAlways = "allow_always";
        public const string RejectOnce = "reject_once";
    }

    public static class AcpStopReason
    {
        public const string EndTurn = "end_turn";
        public const string ToolUse = "tool_use";
        public const string MaxTokens = "max_tokens";
        public const string Cancelled = "cancelled";
    }

    public static class AcpUpdateKind
    {
        public const string AgentMessageChunk = "agent_message_chunk";
        public const string UserMessageChunk = "user_message_chunk";
        public const string AgentThoughtChunk = "agent_thought_chunk";
        public const string ToolCall = "tool_call";
        public const string ToolCallUpdate = "tool_call_update";
        public const string Plan = "plan";
        public const string ConfigOptionsUpdate = "config_options_update";
        public const string AvailableCommandsUpdate = "available_commands_update";
        public const string CurrentModeUpdate = "current_mode_update";
    }

    #endregion
}
