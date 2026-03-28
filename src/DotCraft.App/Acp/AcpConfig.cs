using DotCraft.Configuration;

namespace DotCraft.Acp;

[ConfigSection("Acp", DisplayName = "ACP", Order = 170)]
public sealed class AcpConfig
{
    /// <summary>
    /// Enable ACP (Agent Client Protocol) mode for editor/IDE integration via stdio.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Optional path to the <c>dotcraft</c> executable used to spawn the AppServer subprocess.
    /// When null, defaults to the current process path.
    /// </summary>
    public string? AppServerBin { get; set; }

    /// <summary>
    /// When set, the ACP bridge connects to an existing AppServer via WebSocket instead of
    /// spawning a subprocess. Format: <c>ws://127.0.0.1:9100/ws</c>.
    /// </summary>
    public string? AppServerUrl { get; set; }

    /// <summary>
    /// Optional bearer token for WebSocket AppServer authentication.
    /// </summary>
    public string? AppServerToken { get; set; }
}
