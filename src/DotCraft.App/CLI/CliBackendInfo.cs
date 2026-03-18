namespace DotCraft.CLI;

/// <summary>
/// Carries metadata about the AppServer backend selected at startup.
/// Passed from <see cref="CliHost"/> through <see cref="ReplHost"/> to
/// <see cref="StatusPanel.ShowWelcome"/> so the welcome screen can display
/// the active execution mode.
/// </summary>
public sealed record CliBackendInfo
{
    /// <summary>
    /// Server version reported by the AppServer during the <c>initialize</c> handshake.
    /// Null when the version cannot be determined.
    /// </summary>
    public string? ServerVersion { get; init; }

    /// <summary>
    /// OS process ID of the AppServer subprocess.
    /// Null when connected via WebSocket to a remote server.
    /// </summary>
    public int? ProcessId { get; init; }

    /// <summary>
    /// WebSocket URL used to connect to a remote AppServer (e.g. "ws://127.0.0.1:9100/ws").
    /// Non-null only in WebSocket mode; null in subprocess mode.
    /// </summary>
    public string? ServerUrl { get; init; }
}
