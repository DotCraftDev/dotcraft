using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Configuration;

/// <summary>
/// Configuration for external channel adapters.
/// Loaded via <c>AppConfig.GetSection&lt;ExternalChannelsConfig&gt;("ExternalChannels")</c>.
/// Each key in the dictionary is the canonical channel name (e.g. "telegram").
/// </summary>
/// <remarks>
/// Example JSON in config.json:
/// <code>
/// {
///   "ExternalChannels": {
///     "telegram": {
///       "enabled": true,
///       "transport": "subprocess",
///       "command": "python",
///       "args": ["-m", "dotcraft_telegram"],
///       "env": { "TELEGRAM_BOT_TOKEN": "..." }
///     },
///     "discord": {
///       "enabled": true,
///       "transport": "websocket"
///     }
///   }
/// }
/// </code>
/// </remarks>
[ConfigSection("ExternalChannels", DisplayName = "External Channels", Order = 250)]
public sealed class ExternalChannelsConfig
{
    /// <summary>
    /// Channel entries keyed by canonical channel name.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Entries { get; set; }

    /// <summary>
    /// Deserializes and returns all channel entries.
    /// </summary>
    public Dictionary<string, ExternalChannelEntry> GetChannels()
    {
        if (Entries is null or { Count: 0 })
            return [];

        var result = new Dictionary<string, ExternalChannelEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, element) in Entries)
        {
            var entry = element.Deserialize<ExternalChannelEntry>(AppConfig.SerializerOptions);
            if (entry is not null)
            {
                entry.Name = name;
                result[name] = entry;
            }
        }
        return result;
    }
}

/// <summary>
/// Transport mode for an external channel adapter.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExternalChannelTransport>))]
public enum ExternalChannelTransport
{
    /// <summary>DotCraft spawns the adapter as a child process (stdio JSONL).</summary>
    Subprocess,

    /// <summary>The adapter connects to DotCraft's AppServer WebSocket endpoint.</summary>
    Websocket
}

/// <summary>
/// Configuration for a single external channel adapter.
/// </summary>
public sealed class ExternalChannelEntry
{
    /// <summary>
    /// The canonical channel name. Set programmatically from the dictionary key,
    /// not deserialized from JSON.
    /// </summary>
    [JsonIgnore]
    public string Name { get; set; } = string.Empty;

    /// <summary>Whether this channel is active.</summary>
    public bool Enabled { get; set; }

    /// <summary>Transport mode: "subprocess" or "websocket".</summary>
    public ExternalChannelTransport Transport { get; set; } = ExternalChannelTransport.Subprocess;

    /// <summary>
    /// Command to start the adapter process. Required for subprocess mode.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Command { get; set; }

    /// <summary>
    /// Additional command-line arguments for the subprocess.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Args { get; set; }

    /// <summary>
    /// Working directory for the subprocess. Defaults to workspace root.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Additional environment variables passed to the subprocess.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Env { get; set; }
}
