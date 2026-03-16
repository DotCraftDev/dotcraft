using DotCraft.Configuration;

namespace DotCraft.AppServer;

[ConfigSection("AppServer", DisplayName = "AppServer", Order = 180)]
public sealed class AppServerConfig
{
    /// <summary>
    /// Enable AppServer mode. Exposes the Session Wire Protocol over stdio (JSONL).
    /// When enabled, stdout is reserved for JSON-RPC; all diagnostics go to stderr.
    /// </summary>
    public bool Enabled { get; set; }
}
