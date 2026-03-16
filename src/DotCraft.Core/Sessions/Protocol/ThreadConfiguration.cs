using DotCraft.Mcp;

namespace DotCraft.Sessions.Protocol;

/// <summary>
/// Per-thread agent configuration. When null on a Thread, workspace defaults apply.
/// </summary>
public sealed class ThreadConfiguration
{
    /// <summary>
    /// Per-thread MCP server connections. Null means use workspace-level MCP configuration.
    /// </summary>
    public McpServerConfig[]? McpServers { get; set; }

    /// <summary>
    /// Agent mode: "agent" (full tools, default), "plan" (read-only tools), etc.
    /// </summary>
    public string Mode { get; set; } = "agent";

    /// <summary>
    /// Active extension prefixes declared by the client during ACP initialization
    /// (e.g., ["_unity"]). Null for non-ACP channels.
    /// </summary>
    public string[]? Extensions { get; set; }

    /// <summary>
    /// Additional tool names to enable beyond the mode's default tool set.
    /// </summary>
    public string[]? CustomTools { get; set; }
}
