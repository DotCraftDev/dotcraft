using DotCraft.Configuration;

namespace DotCraft.Acp;

[ConfigSection("Acp", DisplayName = "ACP", Order = 170)]
public sealed class AcpConfig
{
    /// <summary>
    /// Enable ACP (Agent Client Protocol) mode for editor/IDE integration via stdio.
    /// </summary>
    public bool Enabled { get; set; }
}
