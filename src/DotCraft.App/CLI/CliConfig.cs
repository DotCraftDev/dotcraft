using DotCraft.Configuration;

namespace DotCraft.CLI;

/// <summary>
/// Configuration section for the CLI module.
/// </summary>
[ConfigSection("CLI", DisplayName = "CLI", Order = 10)]
public sealed class CliConfig
{
    /// <summary>
    /// When <c>true</c>, the CLI runs the agent stack in the same process instead of
    /// spawning a <c>dotcraft app-server</c> subprocess. Useful for debugging and development.
    /// Default: <c>false</c> (use the AppServer subprocess).
    /// </summary>
    public bool InProcess { get; set; }

    /// <summary>
    /// Optional explicit path to the <c>dotcraft</c> executable used as the AppServer subprocess.
    /// When null, defaults to the current process's executable path (<c>Environment.ProcessPath</c>).
    /// </summary>
    public string? AppServerBin { get; set; }
}
