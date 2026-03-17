using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DotCraft.Common;
using DotCraft.Protocol.AppServer;

namespace DotCraft.CLI;

/// <summary>
/// Manages the lifecycle of a <c>dotcraft app-server</c> subprocess.
/// Spawns the subprocess, wraps its stdio with <see cref="AppServerWireClient"/>, and
/// performs the <c>initialize</c>/<c>initialized</c> handshake on startup.
///
/// Graceful shutdown: closes the subprocess's stdin so the server can detect EOF and
/// exit cleanly, then falls back to <see cref="Process.Kill"/> if it does not exit
/// within a short timeout.
/// </summary>
public sealed class AppServerProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task _stderrForwarderTask;

    /// <summary>
    /// The underlying JSON-RPC 2.0 wire client connected to the subprocess's stdio streams.
    /// </summary>
    public AppServerWireClient Wire { get; }

    /// <summary>
    /// Whether the subprocess is still running.
    /// </summary>
    public bool IsRunning => !_process.HasExited;

    /// <summary>
    /// OS process ID of the AppServer subprocess.
    /// </summary>
    public int ProcessId => _process.Id;

    /// <summary>
    /// Server version string reported by the AppServer during the <c>initialize</c> handshake,
    /// e.g. "0.0.1.0+1d845e83". Null if the server did not include <c>serverInfo.version</c>.
    /// </summary>
    public string? ServerVersion { get; private set; }

    /// <summary>
    /// Invoked when the subprocess exits unexpectedly (i.e., not as part of a normal dispose).
    /// </summary>
    public event Action? OnCrashed;

    private bool _disposed;

    private AppServerProcess(Process process, AppServerWireClient wire, Task stderrForwarderTask)
    {
        _process = process;
        Wire = wire;
        _stderrForwarderTask = stderrForwarderTask;

        process.Exited += (_, _) =>
        {
            if (!_disposed)
                OnCrashed?.Invoke();
        };
        process.EnableRaisingEvents = true;
    }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Spawns <c>&lt;dotcraftBin&gt; app-server</c> as a child process, starts the wire client,
    /// and performs the initialization handshake.
    /// </summary>
    /// <param name="dotcraftBin">
    /// Path to the <c>dotcraft</c> executable.
    /// When null, defaults to the current process's executable path
    /// (<see cref="Environment.ProcessPath"/>).
    /// </param>
    /// <param name="workspacePath">
    /// Working directory for the subprocess. Null means the current directory.
    /// </param>
    /// <param name="ct">Cancellation token for the startup sequence.</param>
    public static async Task<AppServerProcess> StartAsync(
        string? dotcraftBin = null,
        string? workspacePath = null,
        CancellationToken ct = default)
    {
        dotcraftBin ??= Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine dotcraft executable path.");

        var psi = new ProcessStartInfo(dotcraftBin, "app-server")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            // Redirect stderr so diagnostic output from the subprocess does not bleed into the
            // CLI's own console and confuse the JSON-RPC reader on stdout.
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (workspacePath != null)
            psi.WorkingDirectory = workspacePath;

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start AppServer subprocess: {dotcraftBin} app-server");

        // Forward subprocess stderr to the CLI process's stderr so server-side diagnostics
        // remain visible without corrupting the stdout JSON-RPC stream.
        var stderrForwarderTask = ForwardStderrAsync(process);

        var wire = new AppServerWireClient(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream);

        wire.Start();

        var appServer = new AppServerProcess(process, wire, stderrForwarderTask);

        // Handshake: send initialize → wait for response → send initialized
        var initResponse = await wire.InitializeAsync(
            clientName: "dotcraft-cli",
            clientVersion: AppVersion.Informational,
            approvalSupport: true,
            streamingSupport: true);

        // Extract server version from the initialize response for display in the welcome screen.
        // Response shape: { "result": { "serverInfo": { "version": "..." } } }
        appServer.ServerVersion = TryGetServerVersion(initResponse);

        return appServer;
    }

    // -------------------------------------------------------------------------
    // Shutdown
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Dispose the wire client first (closes its writer stream which signals EOF to the server)
        await Wire.DisposeAsync();

        // Wait up to 3 seconds for the server to exit cleanly on EOF
        var deadline = Task.Delay(TimeSpan.FromSeconds(3));
        var exited = _process.WaitForExitAsync();
        if (await Task.WhenAny(exited, deadline) != exited)
        {
            // Graceful exit timed out — force-kill the process tree
            try { _process.Kill(entireProcessTree: true); } catch { }
        }

        // Drain stderr forwarder
        try { await _stderrForwarderTask; } catch { }

        _process.Dispose();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task ForwardStderrAsync(Process process)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync() is { } line)
            {
                await Console.Error.WriteLineAsync($"[AppServer] {line}");
            }
        }
        catch
        {
            // Subprocess exited — stop forwarding
        }
    }

    private static string? TryGetServerVersion(JsonDocument response)
    {
        try
        {
            return response.RootElement
                .GetProperty("result")
                .GetProperty("serverInfo")
                .GetProperty("version")
                .GetString();
        }
        catch (Exception) when (true)
        {
            return null;
        }
    }
}
