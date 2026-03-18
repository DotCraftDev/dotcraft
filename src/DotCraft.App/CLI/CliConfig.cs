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

    /// <summary>
    /// When set, the CLI connects to an already-running AppServer via WebSocket instead of
    /// spawning a subprocess. Format: <c>ws://127.0.0.1:9100/ws</c>.
    /// Takes precedence over the subprocess mode.
    /// </summary>
    public string? AppServerUrl { get; set; }

    /// <summary>
    /// Optional bearer token used when connecting to a WebSocket AppServer that requires
    /// authentication (appserver-protocol.md §15.4). Only relevant when <see cref="AppServerUrl"/> is set.
    /// </summary>
    public string? AppServerToken { get; set; }
}
