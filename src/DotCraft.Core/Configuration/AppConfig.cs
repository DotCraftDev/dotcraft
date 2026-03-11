using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Localization;
using DotCraft.Mcp;

namespace DotCraft.Configuration;

public sealed class AppConfig
{
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gpt-4o-mini";

    public string EndPoint { get; set; } = "https://api.openai.com/v1";

    /// <summary>
    /// Language setting for CLI interface. QQ and WeCom bots always use Chinese.
    /// </summary>
    public Language Language { get; set; } = Language.Chinese;

    public int MaxToolCallRounds { get; set; } = 100;

    public int SubagentMaxToolCallRounds { get; set; } = 50;

    /// <summary>
    /// Maximum number of subagents that can run concurrently.
    /// Controls parallel API call pressure. Excess subagents will queue and wait.
    /// </summary>
    public int SubagentMaxConcurrency { get; set; } = 3;

    /// <summary>
    /// Maximum number of pending requests per session in the gateway queue.
    /// When exceeded, the oldest waiting request is evicted and the user is notified.
    /// Set to 0 to disable the limit (unlimited queue).
    /// </summary>
    public int MaxSessionQueueSize { get; set; } = 3;

    /// <summary>
    /// Enable compact sessions to reduce context cost but may cause context cache miss.
    /// </summary>
    public bool CompactSessions { get; set; } = true;

    /// <summary>
    /// Maximum cumulative input tokens before triggering context compaction.
    /// When the total input tokens across all turns in a session exceed this value,
    /// the conversation history will be summarized to reduce context size.
    /// Set to 0 to disable automatic compaction (default: 160K => 80% * 200K context length for popular models).
    /// </summary>
    public int MaxContextTokens { get; set; } = 160000;

    /// <summary>
    /// Number of messages in a session before triggering background memory consolidation.
    /// When exceeded, old messages are consolidated into MEMORY.md (long-term facts) and
    /// HISTORY.md (grep-searchable event log) via an LLM call.
    /// Set to 0 to disable message-count-based consolidation (default: 50).
    /// </summary>
    public int MemoryWindow { get; set; } = 50;

    /// <summary>
    /// Enable debug mode to display full tool call arguments without truncation.
    /// Can be toggled at runtime by administrators using /debug command in QQ/WeCom bots.
    /// </summary>
    public bool DebugMode { get; set; }

    /// <summary>
    /// Filter which tools are globally available in all modes.
    /// Empty list means all tools are enabled.
    /// </summary>
    public List<string> EnabledTools { get; set; } = [];

    public ToolsConfig Tools { get; set; } = new();

    public QQBotConfig QQBot { get; set; } = new();

    public SecurityConfig Security { get; set; } = new();

    public HeartbeatConfig Heartbeat { get; set; } = new();

    public WeComConfig WeCom { get; set; } = new();

    public WeComBotConfig WeComBot { get; set; } = new();

    public CronConfig Cron { get; set; } = new();

    public ApiConfig Api { get; set; } = new();

    public AgUiConfig AgUi { get; set; } = new();

    public AcpConfig Acp { get; set; } = new();

    public HooksConfig Hooks { get; set; } = new();

    public DashBoardConfig DashBoard { get; set; } = new();

    public GitHubTrackerConfig GitHubTracker { get; set; } = new();

    public List<McpServerConfig> McpServers { get; set; } = [];

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppConfig();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions) ?? new AppConfig();
    }

    public static AppConfig LoadWithGlobalFallback(string workspacePath)
    {
        var globalConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".craft",
            "config.json");

        // Load as JsonNode for proper merging
        JsonNode? globalNode = File.Exists(globalConfigPath)
            ? JsonNode.Parse(File.ReadAllText(globalConfigPath))
            : new JsonObject();

        JsonNode? workspaceNode = File.Exists(workspacePath)
            ? JsonNode.Parse(File.ReadAllText(workspacePath))
            : new JsonObject();

        // Merge workspace config into global config (workspace values take precedence)
        var mergedNode = MergeNodes(globalNode ?? new JsonObject(), workspaceNode ?? new JsonObject());

        // Deserialize merged result
        return mergedNode.Deserialize<AppConfig>(SerializerOptions) ?? new AppConfig();
    }

    private static JsonNode MergeNodes(JsonNode baseNode, JsonNode overrideNode)
    {
        if (overrideNode is JsonObject overrideObj && baseNode is JsonObject baseObj)
        {
            var result = JsonSerializer.Deserialize<JsonObject>(baseObj.ToJsonString()) ?? [];

            foreach (var property in overrideObj)
            {
                if (result.TryGetPropertyValue(property.Key, out var existingValue))
                {
                    result[property.Key] = MergeNodes(existingValue ?? new JsonObject(), property.Value ?? new JsonObject());
                }
                else
                {
                    result[property.Key] = property.Value?.DeepClone();
                }
            }

            return result;
        }

        // For arrays and values, override node takes precedence if it exists
        return overrideNode.DeepClone();
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public sealed class ToolsConfig
    {
        public FileToolsConfig File { get; set; } = new();
        
        public ShellToolsConfig Shell { get; set; } = new();
        
        public WebToolsConfig Web { get; set; } = new();

        public SandboxConfig Sandbox { get; set; } = new();
    }

    public sealed class FileToolsConfig
    {
        /// <summary>
        /// If true, operations outside workspace require user approval.
        /// If false, operations outside workspace are completely blocked.
        /// </summary>
        public bool RequireApprovalOutsideWorkspace { get; set; } = true;

        /// <summary>
        /// Maximum file size in bytes (default: 10 MB).
        /// </summary>
        public int MaxFileSize { get; set; } = 10 * 1024 * 1024;
    }

    public sealed class ShellToolsConfig
    {
        /// <summary>
        /// If true, commands accessing paths outside workspace require user approval.
        /// If false, commands accessing paths outside workspace are completely blocked.
        /// </summary>
        public bool RequireApprovalOutsideWorkspace { get; set; } = true;

        /// <summary>
        /// Command execution timeout in seconds.
        /// </summary>
        public int Timeout { get; set; } = 300;

        /// <summary>
        /// Maximum output length in characters.
        /// </summary>
        public int MaxOutputLength { get; set; } = 10000;
    }

    public sealed class WebToolsConfig
    {
        /// <summary>
        /// Maximum characters to extract from fetched content (default: 50000).
        /// </summary>
        public int MaxChars { get; set; } = 50000;

        /// <summary>
        /// Request timeout in seconds (default: 300).
        /// </summary>
        public int Timeout { get; set; } = 300;

        /// <summary>
        /// Default maximum number of search results to return (default: 5, range: 1-10).
        /// </summary>
        public int SearchMaxResults { get; set; } = 5;

        /// <summary>
        /// Search provider: "Bing" (globally accessible) or "Exa" (AI-optimized, free via MCP).
        /// </summary>
        public string SearchProvider { get; set; } = WebSearchProvider.Exa;
    }

    public sealed class QQBotConfig
    {
        public bool Enabled { get; set; }

        public string Host { get; set; } = "127.0.0.1";

        public int Port { get; set; } = 6700;

        public string AccessToken { get; set; } = string.Empty;

        public List<long> AdminUsers { get; set; } = [];

        public List<long> WhitelistedUsers { get; set; } = [];

        public List<long> WhitelistedGroups { get; set; } = [];

        public int ApprovalTimeoutSeconds { get; set; } = 60;
    }

    public sealed class SandboxConfig
    {
        /// <summary>
        /// Enable sandbox mode. When enabled, shell and file tools execute
        /// inside an isolated OpenSandbox container instead of the host machine.
        /// Requires a running OpenSandbox server.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// OpenSandbox server address (host:port).
        /// </summary>
        public string Domain { get; set; } = "localhost:5880";

        /// <summary>
        /// OpenSandbox API key (optional, depends on server configuration).
        /// </summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Use HTTPS to connect to the OpenSandbox server.
        /// </summary>
        public bool UseHttps { get; set; }

        /// <summary>
        /// Docker image used for sandbox containers.
        /// </summary>
        public string Image { get; set; } = "ubuntu:latest";

        /// <summary>
        /// Sandbox auto-termination timeout in seconds (server-side TTL).
        /// </summary>
        public int TimeoutSeconds { get; set; } = 600;

        /// <summary>
        /// CPU resource limit for the sandbox container.
        /// </summary>
        public string Cpu { get; set; } = "1";

        /// <summary>
        /// Memory resource limit for the sandbox container.
        /// </summary>
        public string Memory { get; set; } = "512Mi";

        /// <summary>
        /// Network policy: "deny" (block all egress), "allow" (no restrictions),
        /// "custom" (allow only domains listed in AllowedEgressDomains).
        /// </summary>
        public string NetworkPolicy { get; set; } = "allow";

        /// <summary>
        /// Domains allowed for outbound network access when NetworkPolicy is "custom".
        /// </summary>
        public List<string> AllowedEgressDomains { get; set; } = [];

        /// <summary>
        /// Seconds of inactivity before an idle sandbox is automatically destroyed.
        /// Set to 0 to disable idle cleanup.
        /// </summary>
        public int IdleTimeoutSeconds { get; set; } = 300;

        /// <summary>
        /// Whether to sync the host workspace into the sandbox on creation.
        /// </summary>
        public bool SyncWorkspace { get; set; } = true;

        /// <summary>
        /// Relative paths (from workspace root) to exclude when syncing the workspace into the sandbox.
        /// Entries are matched as path prefixes: a pattern of "foo/bar" excludes the file "foo/bar"
        /// and everything inside the directory "foo/bar/".
        /// Defaults protect sensitive DotCraft runtime data from leaking into the sandbox.
        /// </summary>
        public List<string> SyncExclude { get; set; } =
        [
            ".craft/config.json",   // API keys and all runtime settings
            ".craft/sessions",      // full conversation history
            ".craft/memory",        // long-term user/project facts
            ".craft/dashboard",     // LLM trace data and token usage records
            ".craft/security",      // persisted approval records (authorized paths/commands)
            ".craft/logs",          // ACP communication debug logs
            ".craft/plans",         // per-session task planning history
        ];
    }

    public sealed class SecurityConfig
    {
        public List<string> BlacklistedPaths { get; set; } = [];
    }

    public sealed class HeartbeatConfig
    {
        public bool Enabled { get; set; }
        
        public int IntervalSeconds { get; set; } = 1800;
        
        public bool NotifyAdmin { get; set; } = true;
    }

    public sealed class WeComConfig
    {
        public bool Enabled { get; set; }

        /// <summary>
        /// Full webhook URL including key, e.g. https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=YOUR_KEY
        /// </summary>
        public string WebhookUrl { get; set; } = string.Empty;
    }

    public sealed class WeComBotConfig
    {
        /// <summary>
        /// Enable WeCom Bot service (receive messages and events)
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Host to bind the HTTP server (default: 0.0.0.0)
        /// </summary>
        public string Host { get; set; } = "0.0.0.0";

        /// <summary>
        /// Port to bind the HTTP server (default: 9000)
        /// </summary>
        public int Port { get; set; } = 9000;

        /// <summary>
        /// List of admin user IDs (WeCom userId strings)
        /// </summary>
        public List<string> AdminUsers { get; set; } = [];

        /// <summary>
        /// List of whitelisted user IDs (WeCom userId strings)
        /// </summary>
        public List<string> WhitelistedUsers { get; set; } = [];

        /// <summary>
        /// List of whitelisted chat IDs
        /// </summary>
        public List<string> WhitelistedChats { get; set; } = [];

        /// <summary>
        /// Approval request timeout in seconds (default: 60)
        /// </summary>
        public int ApprovalTimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// List of bot configurations (each bot corresponds to a path)
        /// </summary>
        public List<WeComRobotConfig> Robots { get; set; } = [];

        /// <summary>
        /// Default robot configuration (for unmatched paths)
        /// </summary>
        public WeComRobotConfig? DefaultRobot { get; set; }
    }

    public sealed class WeComRobotConfig
    {
        /// <summary>
        /// Bot path (e.g., /dotcraft)
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Token from WeCom bot configuration
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// EncodingAESKey (43 chars without trailing '=')
        /// </summary>
        public string AesKey { get; set; } = string.Empty;
    }

    public sealed class CronConfig
    {
        public bool Enabled { get; set; } = true;
        
        public string StorePath { get; set; } = "cron/jobs.json";
    }

    public sealed class ApiConfig
    {
        public bool Enabled { get; set; }

        public string Host { get; set; } = "127.0.0.1";

        public int Port { get; set; } = 8080;

        public string ApiKey { get; set; } = string.Empty;

        public bool AutoApprove { get; set; } = true;

        /// <summary>
        /// Approval mode for sensitive operations in API mode.
        /// "auto" = auto-approve all operations (default, same as AutoApprove=true).
        /// "reject" = auto-reject all operations (same as AutoApprove=false).
        /// "interactive" = pause and wait for approval via /v1/approvals endpoint (Human-in-the-Loop).
        /// When set, takes precedence over AutoApprove.
        /// </summary>
        public string ApprovalMode { get; set; } = string.Empty;

        /// <summary>
        /// Timeout in seconds for interactive approval requests (default: 120).
        /// If no approval decision is received within this time, the operation is rejected.
        /// Only applies when ApprovalMode is "interactive".
        /// </summary>
        public int ApprovalTimeoutSeconds { get; set; } = 120;

    }

    public sealed class AgUiConfig
    {
        /// <summary>
        /// Enable AG-UI protocol server channel.
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// AG-UI endpoint path (default: /ag-ui).
        /// </summary>
        public string Path { get; set; } = "/ag-ui";

        /// <summary>
        /// Host to bind the AG-UI HTTP server (default: 127.0.0.1).
        /// </summary>
        public string Host { get; set; } = "127.0.0.1";

        /// <summary>
        /// Port to bind the AG-UI HTTP server (default: 5100).
        /// </summary>
        public int Port { get; set; } = 5100;

        /// <summary>
        /// When true, require Bearer API key for AG-UI requests.
        /// </summary>
        public bool RequireAuth { get; set; }

        /// <summary>
        /// API key for AG-UI when RequireAuth is true.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Approval mode for sensitive tool operations: "interactive" (request frontend approval, default)
        /// or "auto" (auto-approve all, matches legacy behavior).
        /// </summary>
        public string ApprovalMode { get; set; } = "interactive";
    }

    public sealed class AcpConfig
    {
        /// <summary>
        /// Enable ACP (Agent Client Protocol) mode for editor/IDE integration via stdio.
        /// </summary>
        public bool Enabled { get; set; }
    }

    public sealed class HooksConfig
    {
        /// <summary>
        /// Whether hooks are enabled (default: true when hooks.json exists).
        /// Set to false to globally disable all hooks.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }

    public sealed class DashBoardConfig
    {
        public bool Enabled { get; set; } = true;

        public int Port { get; set; } = 8080;

        public string Host { get; set; } = "127.0.0.1";

        /// <summary>
        /// Dashboard login username. When both Username and Password are set,
        /// all dashboard routes require authentication via a login page.
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Dashboard login password.
        /// </summary>
        public string Password { get; set; } = string.Empty;
    }
}
