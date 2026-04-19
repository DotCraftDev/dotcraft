using DotCraft.Configuration;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.Mcp;

[ConfigSection(
    "McpServers",
    DisplayName = "MCP Servers",
    Order = 95,
    RootKey = "McpServers",
    DefaultReload = ReloadBehavior.Hot,
    HasDefaultReload = true)]
public sealed class McpServerConfig
{
    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Transport type: "stdio" (default) or "http".
    /// </summary>
    [ConfigField(FieldType = "select", Options = new[] { "stdio", "http" })]
    public string Transport { get; set; } = "stdio";

    /// <summary>
    /// Command to launch (stdio transport only).
    /// </summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>
    /// Arguments for the command (stdio transport only).
    /// </summary>
    public List<string> Arguments { get; set; } = [];

    /// <summary>
    /// Wire alias for <see cref="Arguments"/>.
    /// </summary>
    [JsonIgnore]
    public List<string> Args
    {
        get => Arguments;
        set => Arguments = value ?? [];
    }

    /// <summary>
    /// Environment variables for the command (stdio transport only).
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    /// <summary>
    /// Wire alias for <see cref="EnvironmentVariables"/>.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string> Env
    {
        get => EnvironmentVariables;
        set => EnvironmentVariables = value ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// Environment variable names to forward from the host process (stdio transport only).
    /// </summary>
    public List<string> EnvVars { get; set; } = [];

    /// <summary>
    /// Optional working directory for stdio transport.
    /// </summary>
    public string? Cwd { get; set; }

    /// <summary>
    /// Server URL (http transport only), e.g. "https://mcp.exa.ai/mcp".
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Additional HTTP headers (http transport only).
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// Wire alias for <see cref="Headers"/>.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string> HttpHeaders
    {
        get => Headers;
        set => Headers = value ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// HTTP headers whose values are sourced from environment variables (HTTP transport only).
    /// Key = header name, value = env var name.
    /// </summary>
    public Dictionary<string, string> EnvHttpHeaders { get; set; } = new();

    /// <summary>
    /// Bearer token env var name for HTTP transport.
    /// </summary>
    public string? BearerTokenEnvVar { get; set; }

    /// <summary>
    /// Startup timeout in seconds.
    /// </summary>
    public double? StartupTimeoutSec { get; set; }

    /// <summary>
    /// Default tool timeout in seconds.
    /// </summary>
    public double? ToolTimeoutSec { get; set; }

    [JsonIgnore]
    public string NormalizedTransport =>
        Transport.Equals("streamableHttp", StringComparison.OrdinalIgnoreCase) ||
        Transport.Equals("streamable-http", StringComparison.OrdinalIgnoreCase) ||
        Transport.Equals("http", StringComparison.OrdinalIgnoreCase)
            ? "streamableHttp"
            : "stdio";

    public McpServerConfig Clone() =>
        new()
        {
            Name = Name,
            Enabled = Enabled,
            Transport = Transport,
            Command = Command,
            Arguments = [.. Arguments],
            EnvironmentVariables = new Dictionary<string, string>(EnvironmentVariables, StringComparer.Ordinal),
            EnvVars = [.. EnvVars],
            Cwd = Cwd,
            Url = Url,
            Headers = new Dictionary<string, string>(Headers, StringComparer.Ordinal),
            EnvHttpHeaders = new Dictionary<string, string>(EnvHttpHeaders, StringComparer.Ordinal),
            BearerTokenEnvVar = BearerTokenEnvVar,
            StartupTimeoutSec = StartupTimeoutSec,
            ToolTimeoutSec = ToolTimeoutSec
        };
}

/// <summary>
/// Supports both legacy array-form MCP config and the new object-map form:
/// { "McpServers": { "name": { ... } } }.
/// </summary>
public sealed class McpServerConfigListConverter : JsonConverter<List<McpServerConfig>>
{
    public override List<McpServerConfig>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var list = new List<McpServerConfig>();

        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var cfg = item.Deserialize<McpServerConfig>(options);
                if (cfg != null)
                    list.Add(cfg);
            }
            return list;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in root.EnumerateObject())
            {
                var cfg = prop.Value.Deserialize<McpServerConfig>(options) ?? new McpServerConfig();
                if (string.IsNullOrWhiteSpace(cfg.Name))
                    cfg.Name = prop.Name;
                list.Add(cfg);
            }
            return list;
        }

        return [];
    }

    public override void Write(Utf8JsonWriter writer, List<McpServerConfig> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var server in value.Where(s => !string.IsNullOrWhiteSpace(s.Name))
                     .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            writer.WritePropertyName(server.Name);
            JsonSerializer.Serialize(writer, server, options);
        }

        writer.WriteEndObject();
    }
}
