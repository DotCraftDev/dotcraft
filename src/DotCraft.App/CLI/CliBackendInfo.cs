namespace DotCraft.CLI;

/// <summary>
/// Carries metadata about the session backend selected at startup.
/// Passed from <see cref="CliHost"/> through <see cref="ReplHost"/> to
/// <see cref="StatusPanel.ShowWelcome"/> so the welcome screen can display
/// the active execution mode.
/// </summary>
public sealed record CliBackendInfo
{
    /// <summary>
    /// True when the CLI is connected to a subprocess AppServer over the wire protocol.
    /// False when the agent stack runs in-process.
    /// </summary>
    public bool IsWire { get; init; }

    /// <summary>
    /// Server version reported by the AppServer during the <c>initialize</c> handshake.
    /// Null in in-process mode.
    /// </summary>
    public string? ServerVersion { get; init; }

    /// <summary>
    /// OS process ID of the AppServer subprocess.
    /// Null in in-process mode or WebSocket mode.
    /// </summary>
    public int? ProcessId { get; init; }

    /// <summary>
    /// WebSocket URL used to connect to a remote AppServer (e.g. "ws://127.0.0.1:9100/ws").
    /// Non-null only in WebSocket mode; null in subprocess and in-process modes.
    /// </summary>
    public string? ServerUrl { get; init; }
}
