using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DotCraft.Localization;
using DotCraft.Mcp;
using Microsoft.Extensions.AI;

namespace DotCraft.Configuration;

[ConfigSection("", DisplayName = "Core", Order = 0)]
public sealed class AppConfig
{
    [ConfigField(Sensitive = true, Hint = "Leave blank to inherit from global")]
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gpt-4o-mini";

    public string EndPoint { get; set; } = "https://api.openai.com/v1";

    /// <summary>
    /// Controls provider reasoning/thinking behavior.
    /// Disabled by default; enable when you want providers that support reasoning to use it.
    /// </summary>
    [ConfigField(Ignore = true)]
    public ReasoningConfig Reasoning { get; set; } = new();

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
    [ConfigField(Min = 0, Hint = "0 = unlimited")]
    public int MaxSessionQueueSize { get; set; } = 3;

    /// <summary>
    /// Maximum cumulative input tokens before triggering context compaction.
    /// When the total input tokens across all turns in a session exceed this value,
    /// the conversation history will be summarized to reduce context size.
    /// Set to 0 to disable automatic compaction (default: 160K => 80% * 200K context length for popular models).
    /// </summary>
    [ConfigField(Min = 0, Hint = "0 = disable compaction")]
    public int MaxContextTokens { get; set; } = 160000;

    /// <summary>
    /// Number of messages in a session before triggering background memory consolidation.
    /// When exceeded, old messages are consolidated into MEMORY.md (long-term facts) and
    /// HISTORY.md (grep-searchable event log) via an LLM call.
    /// Set to 0 to disable message-count-based consolidation (default: 50).
    /// </summary>
    [ConfigField(Min = 0, Hint = "0 = disable consolidation")]
    public int MemoryWindow { get; set; } = 50;

    /// <summary>
    /// Model used for memory consolidation. When empty, uses <see cref="Model"/> (same as main agent).
    /// When set, use this model for consolidation only (e.g. a non-thinking model to avoid tool_choice restrictions in thinking mode).
    /// </summary>
    [ConfigField(Hint = "Model for memory consolidation. Empty = use main Model. Set to a non-thinking model if main model does not support tool_choice in thinking mode.")]
    public string ConsolidationModel { get; set; } = string.Empty;

    /// <summary>
    /// Enable debug mode to display full tool call arguments without truncation.
    /// Can be toggled at runtime by administrators using /debug command in QQ/WeCom bots.
    /// </summary>
    public bool DebugMode { get; set; }

    /// <summary>
    /// Filter which tools are globally available in all modes.
    /// Empty list means all tools are enabled.
    /// </summary>
    [ConfigField(Hint = "JSON array of tool names. Built-in tools use PascalCase (e.g. Shell, ReadFile); MCP/manual tools often use snake_case. Empty = all enabled.")]
    public List<string> EnabledTools { get; set; } = [];

    [ConfigField(Ignore = true)]
    public ToolsConfig Tools { get; set; } = new();

    [ConfigField(Ignore = true)]
    public SecurityConfig Security { get; set; } = new();

    [ConfigField(Ignore = true)]
    public HeartbeatConfig Heartbeat { get; set; } = new();

    [ConfigField(Ignore = true)]
    public CronConfig Cron { get; set; } = new();

    [ConfigField(Ignore = true)]
    public SkillsConfig Skills { get; set; } = new();

    [ConfigField(Ignore = true)]
    public HooksConfig Hooks { get; set; } = new();

    [ConfigField(Ignore = true)]
    public TracingConfig Tracing { get; set; } = new();

    [ConfigField(Ignore = true)]
    public DashBoardConfig DashBoard { get; set; } = new();

    [ConfigField(Ignore = true)]
    public LoggingConfig Logging { get; set; } = new();

    [ConfigField(Ignore = true)]
    [JsonConverter(typeof(McpServerConfigListConverter))]
    public List<McpServerConfig> McpServers { get; set; } = [];

    /// <summary>
    /// Captures all module-specific config sections that are not defined in Core.
    /// Module projects (QQ, WeCom, AGUI, etc.) store their config here when they
    /// are not directly referenced by Core.
    /// </summary>
    [JsonExtensionData]
    [ConfigField(Ignore = true)]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    // Section cache: avoids repeated deserialization on hot paths
    [JsonIgnore]
    private readonly ConcurrentDictionary<string, object> _sectionCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a typed config section by JSON key (case-insensitive).
    /// The section is deserialized from <see cref="ExtensionData"/> on first access and cached.
    /// If the key is not present in the config file, a default instance is returned.
    /// </summary>
    /// <typeparam name="T">The config section type. Must have a parameterless constructor.</typeparam>
    /// <param name="key">The JSON key, e.g. "QQBot", "WeComBot", "AgUi".</param>
    public T GetSection<T>(string key) where T : class, new()
    {
        return (T)_sectionCache.GetOrAdd(key, _ =>
        {
            if (ExtensionData != null)
            {
                foreach (var (k, v) in ExtensionData)
                {
                    if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                        return v.Deserialize<T>(SerializerOptions) ?? new T();
                }
            }
            return new T();
        });
    }

    /// <summary>
    /// Stores a typed config section in the cache, overriding any value deserialized from config.json.
    /// Use this for programmatic config overrides (e.g. forcing ACP mode from a command-line flag).
    /// </summary>
    public void SetSection<T>(string key, T value) where T : class, new()
    {
        _sectionCache[key] = value;
    }

    /// <summary>
    /// Checks whether a config section has <c>Enabled = true</c>, without requiring knowledge of
    /// the section's type. Used by cross-cutting modules (GatewayModule, UnityModule) that need
    /// to check enabled status of sections from other modules without depending on their types.
    /// </summary>
    /// <param name="key">The JSON key (case-insensitive), e.g. "QQBot", "WeComBot".</param>
    public bool IsSectionEnabled(string key)
    {
        // Check the section cache first — SetSection may have overridden the value.
        if (_sectionCache.TryGetValue(key, out var cached))
        {
            var enabledProp = cached.GetType().GetProperty("Enabled");
            if (enabledProp != null && enabledProp.PropertyType == typeof(bool))
                return (bool)(enabledProp.GetValue(cached) ?? false);
        }

        if (ExtensionData == null) return false;
        foreach (var (k, v) in ExtensionData)
        {
            if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;
            if (v.TryGetProperty("Enabled", out var enabled) || v.TryGetProperty("enabled", out enabled))
                return enabled.ValueKind == JsonValueKind.True;
            return false;
        }
        return false;
    }

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
        {
            return new AppConfig();
        }

        var node = JsonNode.Parse(File.ReadAllText(path)) ?? new JsonObject();
        ExpandEnvironmentVariables(node);
        return node.Deserialize<AppConfig>(SerializerOptions) ?? new AppConfig();
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

        // Expand environment variable references before deserializing
        ExpandEnvironmentVariables(mergedNode);

        return mergedNode.Deserialize<AppConfig>(SerializerOptions) ?? new AppConfig();
    }

    /// <summary>
    /// Recursively expands environment variable references in all string values of a JSON node tree.
    /// Supports two syntaxes:
    /// <list type="bullet">
    ///   <item><c>$VAR_NAME</c> — the entire string is replaced by the env var value.</item>
    ///   <item><c>${VAR_NAME}</c> — inline substitution; multiple references may appear in one string.</item>
    /// </list>
    /// If the referenced variable is not set, the original placeholder is preserved unchanged.
    /// </summary>
    internal static void ExpandEnvironmentVariables(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    var child = obj[key];
                    if (child is JsonValue val && val.TryGetValue<string>(out var str))
                    {
                        var expanded = ExpandEnvString(str);
                        if (expanded != str)
                            obj[key] = JsonValue.Create(expanded);
                    }
                    else if (child is JsonObject or JsonArray)
                    {
                        ExpandEnvironmentVariables(child);
                    }
                }
                break;

            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var child = arr[i];
                    if (child is JsonValue val && val.TryGetValue<string>(out var str))
                    {
                        var expanded = ExpandEnvString(str);
                        if (expanded != str)
                            arr[i] = JsonValue.Create(expanded);
                    }
                    else if (child is JsonObject or JsonArray)
                    {
                        ExpandEnvironmentVariables(child);
                    }
                }
                break;
        }
    }

    // Matches ${VAR_NAME} inline placeholders.
    private static readonly Regex InlineEnvVarRegex =
        new(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    // Matches a whole-string $VAR_NAME reference.
    private static readonly Regex WholeEnvVarRegex =
        new(@"^\$([A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.Compiled);

    private static string ExpandEnvString(string value)
    {
        // Whole-value reference: $VAR — replace entire string with env var value.
        var wholeMatch = WholeEnvVarRegex.Match(value);
        if (wholeMatch.Success)
            return Environment.GetEnvironmentVariable(wholeMatch.Groups[1].Value) ?? value;

        // Inline reference: ${VAR} — substitute each occurrence, keep placeholder if not set.
        return InlineEnvVarRegex.Replace(value, m =>
            Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
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

    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [ConfigSection("Reasoning", DisplayName = "Reasoning", Order = 10)]
    public sealed class ReasoningConfig
    {
        /// <summary>
        /// Whether to request provider reasoning support.
        /// Unsupported providers or models may ignore this setting.
        /// </summary>
        [ConfigField(Hint = "Request provider reasoning/thinking support when available.")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Requested reasoning effort level when reasoning is enabled.
        /// </summary>
        public ReasoningEffort Effort { get; set; } = ReasoningEffort.Medium;

        /// <summary>
        /// Controls how much reasoning content is exposed in responses.
        /// The default exposes full summary.
        /// </summary>
        [ConfigField(Hint = "Controls whether reasoning content is exposed in responses.")]
        public ReasoningOutput Output { get; set; } = ReasoningOutput.Full;

        /// <summary>
        /// Converts the configuration to chat reasoning options.
        /// Returns <see langword="null"/> when reasoning is disabled.
        /// </summary>
        public ReasoningOptions? ToOptions()
        {
            if (!Enabled)
                return null;

            return new ReasoningOptions
            {
                Effort = Effort,
                Output = Output
            };
        }
    }

    [ConfigSection("Tools.File", DisplayName = "Tools > File", Order = 20)]
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
        [ConfigField(Min = 0, Hint = "bytes, default 10485760 (10 MB)")]
        public int MaxFileSize { get; set; } = 10 * 1024 * 1024;
    }

    [ConfigSection("Tools.Shell", DisplayName = "Tools > Shell", Order = 21)]
    public sealed class ShellToolsConfig
    {
        /// <summary>
        /// Command execution timeout in seconds.
        /// </summary>
        [ConfigField(Min = 0, Hint = "seconds")]
        public int Timeout { get; set; } = 300;

        /// <summary>
        /// Maximum output length in characters.
        /// </summary>
        [ConfigField(Min = 0, Hint = "characters")]
        public int MaxOutputLength { get; set; } = 10000;
    }

    [ConfigSection("Tools.Web", DisplayName = "Tools > Web", Order = 22)]
    public sealed class WebToolsConfig
    {
        /// <summary>
        /// Maximum characters to extract from fetched content (default: 50000).
        /// </summary>
        [ConfigField(Min = 0)]
        public int MaxChars { get; set; } = 50000;

        /// <summary>
        /// Request timeout in seconds (default: 300).
        /// </summary>
        [ConfigField(Min = 0, Hint = "seconds")]
        public int Timeout { get; set; } = 300;

        /// <summary>
        /// Default maximum number of search results to return (default: 5, range: 1-10).
        /// </summary>
        [ConfigField(Min = 1, Max = 10)]
        public int SearchMaxResults { get; set; } = 5;

        /// <summary>
        /// Search provider: "Bing" (globally accessible) or "Exa" (AI-optimized, free via MCP).
        /// </summary>
        [ConfigField(FieldType = "select", Options = ["Exa", "Bing"])]
        public string SearchProvider { get; set; } = WebSearchProvider.Exa;
    }

    [ConfigSection("Tools.Sandbox", DisplayName = "Tools > Sandbox", Order = 23)]
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
        [ConfigField(Sensitive = true)]
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
        [ConfigField(Min = 0)]
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
        [ConfigField(FieldType = "select", Options = ["allow", "deny", "custom"])]
        public string NetworkPolicy { get; set; } = "allow";

        /// <summary>
        /// Domains allowed for outbound network access when NetworkPolicy is "custom".
        /// </summary>
        [ConfigField(Hint = "JSON array of domain strings")]
        public List<string> AllowedEgressDomains { get; set; } = [];

        /// <summary>
        /// Seconds of inactivity before an idle sandbox is automatically destroyed.
        /// Set to 0 to disable idle cleanup.
        /// </summary>
        [ConfigField(Min = 0)]
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
        [ConfigField(Hint = "JSON array of relative paths to exclude from workspace sync (prefix matching). Default covers all sensitive .craft/ runtime data. Extend rather than replace.")]
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

    [ConfigSection("Tools.DeferredLoading", DisplayName = "Tools > Deferred Loading", Order = 24)]
    public sealed class DeferredLoadingConfig
    {
        /// <summary>
        /// Enable deferred loading for MCP tools. Default: false.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// MCP tool names that are always loaded upfront, even when deferred loading is enabled.
        /// Use this for high-frequency tools that should be available immediately.
        /// </summary>
        [ConfigField(Hint = "JSON array of MCP tool names to always load upfront. MCP tools typically use snake_case; use exact name as exposed by the server.")]
        public List<string> AlwaysLoadedTools { get; set; } = [];

        /// <summary>
        /// Maximum number of results returned by SearchTools per query. Default: 5.
        /// </summary>
        [ConfigField(Min = 1, Max = 20)]
        public int MaxSearchResults { get; set; } = 5;

        /// <summary>
        /// Minimum MCP tool count required to activate deferred loading.
        /// If the total number of MCP tools is below this threshold, all tools load normally.
        /// Default: 10.
        /// </summary>
        [ConfigField(Min = 1, Hint = "Deferred loading only activates when total MCP tools >= this value")]
        public int DeferThreshold { get; set; } = 10;
    }

    public sealed class ToolsConfig
    {
        public FileToolsConfig File { get; set; } = new();
        
        public ShellToolsConfig Shell { get; set; } = new();
        
        public WebToolsConfig Web { get; set; } = new();

        public SandboxConfig Sandbox { get; set; } = new();

        public DeferredLoadingConfig DeferredLoading { get; set; } = new();

        /// <summary>
        /// Global tool result size limits and spill-to-disk preview settings.
        /// </summary>
        public ToolResultLimitsConfig ResultLimits { get; set; } = new();
    }

    [ConfigSection("Tools.ResultLimits", DisplayName = "Tools > Result limits", Order = 24)]
    public sealed class ToolResultLimitsConfig
    {
        /// <summary>
        /// Default maximum tool result length in characters before spill-to-disk (per-tool overrides via ToolAttribute.MaxResultChars).
        /// </summary>
        [ConfigField(Min = 0, Hint = "characters; 0 disables limiting for tools that use the global default")]
        public int MaxToolResultChars { get; set; } = 50_000;

        /// <summary>
        /// Number of head and tail lines included in the preview when a result is spilled to disk.
        /// </summary>
        [ConfigField(Min = 1, Max = 500)]
        public int SpillPreviewLines { get; set; } = 40;
    }

    [ConfigSection("Security", DisplayName = "Security", Order = 80)]
    public sealed class SecurityConfig
    {
        [ConfigField(Hint = "JSON array of path strings")]
        public List<string> BlacklistedPaths { get; set; } = [];
    }

    [ConfigSection("Heartbeat", DisplayName = "Heartbeat", Order = 70)]
    public sealed class HeartbeatConfig
    {
        public bool Enabled { get; set; }
        
        [ConfigField(Min = 1)]
        public int IntervalSeconds { get; set; } = 1800;
        
        public bool NotifyAdmin { get; set; } = true;
    }

    [ConfigSection("Cron", DisplayName = "Cron", Order = 60)]
    public sealed class CronConfig
    {
        public bool Enabled { get; set; } = true;
        
        public string StorePath { get; set; } = "cron/jobs.json";
    }

    [ConfigSection("Skills", DisplayName = "Skills", Order = 58)]
    public sealed class SkillsConfig
    {
        /// <summary>
        /// Skill directory names disabled for this workspace (not injected into agent context).
        /// </summary>
        [ConfigField(Hint = "JSON array of skill names to disable for this workspace")]
        public List<string> DisabledSkills { get; set; } = [];
    }

    [ConfigSection("Hooks", DisplayName = "Hooks", Order = 85)]
    public sealed class HooksConfig
    {
        /// <summary>
        /// Whether hooks are enabled (default: true when hooks.json exists).
        /// Set to false to globally disable all hooks.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }

    [ConfigSection("Tracing", DisplayName = "Tracing", Order = 45)]
    public sealed class TracingConfig
    {
        /// <summary>
        /// Whether tracing is enabled. When disabled, no trace events are recorded regardless of Dashboard state.
        /// </summary>
        public bool Enabled { get; set; } = true;
    }

    [ConfigSection("DashBoard", DisplayName = "Dashboard", Order = 50)]
    public sealed class DashBoardConfig
    {
        public bool Enabled { get; set; } = true;

        [ConfigField(Min = 1, Max = 65535)]
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
        [ConfigField(Sensitive = true)]
        public string Password { get; set; } = string.Empty;
    }

    [ConfigSection("Logging", DisplayName = "Logging", Order = 90)]
    public sealed class LoggingConfig
    {
        /// <summary>
        /// Enable file logging. Default: true.
        /// </summary>
        [ConfigField(Hint = "Write log entries to a daily-rotated file under the logs directory")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Also write logs to the console (stdout). Default: false.
        /// </summary>
        [ConfigField(Hint = "Also print log entries to stdout (useful for debugging)")]
        public bool Console { get; set; }

        /// <summary>
        /// Minimum log level: Trace, Debug, Information, Warning, Error, Critical. Default: Information.
        /// </summary>
        [ConfigField(Hint = "Minimum log level to record", FieldType = "select", Options = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"])]
        public string MinLevel { get; set; } = "Information";

        /// <summary>
        /// Log directory relative to the .craft path. Default: "logs".
        /// </summary>
        [ConfigField(Hint = "Log directory relative to the .craft path")]
        public string Directory { get; set; } = "logs";

        /// <summary>
        /// Number of days to retain log files before deletion. Set to 0 to disable cleanup. Default: 7.
        /// </summary>
        [ConfigField(Hint = "Days to keep log files before auto-deletion; 0 = keep forever")]
        public int RetentionDays { get; set; } = 7;
    }
}

