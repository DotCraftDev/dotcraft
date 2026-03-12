using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UnityEditor;
using UnityEngine;

namespace DotCraft.Editor.Settings
{
    /// <summary>
    /// A single MCP server entry stored in DotCraft settings.
    /// Supports both stdio and http transports.
    /// </summary>
    [Serializable]
    public sealed class McpServerEntry
    {
        /// <summary>Display name for the server (used as the MCP server name in the ACP protocol).</summary>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>Whether this server is included when creating or loading sessions.</summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>Transport type: "stdio" or "http".</summary>
        [JsonPropertyName("transport")]
        public string Transport { get; set; } = "stdio";

        // stdio-specific fields

        /// <summary>Executable command (stdio transport only).</summary>
        [JsonPropertyName("command")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Command { get; set; }

        /// <summary>Command-line arguments (stdio transport only).</summary>
        [JsonPropertyName("arguments")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string> Arguments { get; set; }

        /// <summary>Environment variables to inject into the server process (stdio transport only).</summary>
        [JsonPropertyName("environmentVariables")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string> EnvironmentVariables { get; set; }

        // http-specific fields

        /// <summary>Server URL (http transport only).</summary>
        [JsonPropertyName("url")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Url { get; set; }

        /// <summary>HTTP headers (http transport only).</summary>
        [JsonPropertyName("headers")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string> Headers { get; set; }
    }

    /// <summary>
    /// Configuration settings for DotCraft Unity Client.
    /// Stored in UserSettings/DotCraftSettings.json (per-user, not in version control).
    /// </summary>
    [Serializable]
    public sealed class DotCraftSettings
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static DotCraftSettings _instance;
        private static readonly string SettingsPath = "UserSettings/DotCraftSettings.json";

        public static DotCraftSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = LoadOrCreate();
                }
                return _instance;
            }
        }

        /// <summary>
        /// Command to execute DotCraft (e.g., "dotnet" or full path to executable).
        /// </summary>
        [JsonPropertyName("dotCraftCommand")]
        public string DotCraftCommand { get; set; } = "dotcraft";

        /// <summary>
        /// Arguments passed to DotCraft command.
        /// Example: "run --project /path/to/DotCraft -- --acp"
        /// </summary>
        [JsonPropertyName("dotCraftArguments")]
        public string DotCraftArguments { get; set; } = "-acp";

        /// <summary>
        /// Working directory for DotCraft process. Defaults to Unity project root.
        /// </summary>
        [JsonPropertyName("workspacePath")]
        public string WorkspacePath { get; set; } = "";

        /// <summary>
        /// Environment variables to inject into DotCraft process.
        /// Use for API keys and other configuration.
        /// </summary>
        [JsonPropertyName("environmentVariables")]
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

        /// <summary>
        /// Automatically reconnect after Domain Reload.
        /// </summary>
        [JsonPropertyName("autoReconnect")]
        public bool AutoReconnect { get; set; } = true;

        /// <summary>
        /// Enable verbose logging for debugging.
        /// </summary>
        [JsonPropertyName("verboseLogging")]
        public bool VerboseLogging { get; set; } = false;

        /// <summary>
        /// Timeout in seconds for ACP requests.
        /// </summary>
        [JsonPropertyName("requestTimeoutSeconds")]
        public int RequestTimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// Maximum number of messages to keep in chat history.
        /// </summary>
        [JsonPropertyName("maxHistoryMessages")]
        public int MaxHistoryMessages { get; set; } = 1000;

        /// <summary>
        /// Enable built-in Unity tools (_unity/* extension methods).
        /// Disable if using external Unity integration.
        /// </summary>
        [JsonPropertyName("enableBuiltinUnityTools")]
        public bool EnableBuiltinUnityTools { get; set; } = true;

        /// <summary>
        /// MCP servers to inject into every new DotCraft session via the ACP mcpServers field.
        /// These supplement any servers already configured in .craft/config.json.
        /// </summary>
        [JsonPropertyName("mcpServers")]
        public List<McpServerEntry> McpServers { get; set; } = new();

        /// <summary>
        /// Gets the effective workspace path (falls back to project root).
        /// </summary>
        [JsonIgnore]
        public string EffectiveWorkspacePath =>
            string.IsNullOrEmpty(WorkspacePath)
                ? Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath
                : WorkspacePath;

        /// <summary>
        /// Loads settings from file, or creates default settings if file doesn't exist.
        /// </summary>
        public static DotCraftSettings LoadOrCreate()
        {
            if (File.Exists(SettingsPath))
            {
                try
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<DotCraftSettings>(json, JsonOptions);
                    return settings ?? new DotCraftSettings();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DotCraft] Failed to load settings: {ex.Message}. Using defaults.");
                    return new DotCraftSettings();
                }
            }
            return new DotCraftSettings();
        }

        /// <summary>
        /// Saves settings to file.
        /// </summary>
        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DotCraft] Failed to save settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates settings and returns any errors.
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(DotCraftCommand))
            {
                errors.Add("DotCraft command is not configured.");
            }

            if (string.IsNullOrWhiteSpace(DotCraftArguments))
            {
                errors.Add("DotCraft arguments are not configured.");
            }

            if (RequestTimeoutSeconds < 1 || RequestTimeoutSeconds > 300)
            {
                errors.Add("Request timeout must be between 1 and 300 seconds.");
            }

            return errors;
        }

        /// <summary>
        /// Checks whether the effective workspace contains a .craft directory.
        /// Returns true when the workspace is ready; false otherwise, with a human-readable
        /// message that explains what is missing and how to fix it.
        /// </summary>
        public bool ValidateWorkspace(out string errorMessage)
        {
            var workspace = EffectiveWorkspacePath;

            if (string.IsNullOrEmpty(workspace) || !Directory.Exists(workspace))
            {
                errorMessage = $"Workspace directory does not exist: \"{workspace}\".\n" +
                               "Set a valid Workspace Path in Project Settings > DotCraft.";
                return false;
            }

            var craftDir = Path.Combine(workspace, ".craft");
            if (!Directory.Exists(craftDir))
            {
                errorMessage = $"The workspace \"{workspace}\" does not contain a .craft directory.\n" +
                               "Run `dotcraft` in that directory first, " +
                               "or change the Workspace Path in Project Settings > DotCraft to a directory " +
                               "that already has a .craft folder.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        /// <summary>
        /// Resets settings to defaults.
        /// </summary>
        public void ResetToDefaults()
        {
            DotCraftCommand = "dotcraft";
            DotCraftArguments = "-acp";
            WorkspacePath = "";
            EnvironmentVariables = new Dictionary<string, string>();
            AutoReconnect = true;
            VerboseLogging = false;
            RequestTimeoutSeconds = 30;
            MaxHistoryMessages = 1000;
            EnableBuiltinUnityTools = true;
            McpServers = new List<McpServerEntry>();
        }
    }
}
