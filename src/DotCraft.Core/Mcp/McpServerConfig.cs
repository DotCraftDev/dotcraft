using DotCraft.Configuration;

namespace DotCraft.Mcp;

[ConfigSection("McpServers", DisplayName = "MCP Servers", Order = 95, RootKey = "McpServers")]
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
    /// Environment variables for the command (stdio transport only).
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    /// <summary>
    /// Server URL (http transport only), e.g. "https://mcp.exa.ai/mcp".
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Additional HTTP headers (http transport only).
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new();
}
