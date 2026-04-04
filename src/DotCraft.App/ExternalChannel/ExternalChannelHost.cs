using System.Diagnostics;
using System.Text.Json;
using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Modules;
using DotCraft.Protocol.AppServer;
using DotCraft.Protocol;
using DotCraft.Security;
using Spectre.Console;

namespace DotCraft.ExternalChannel;

/// <summary>
/// Bridge component that wraps a Wire Protocol connection to an external adapter process,
/// exposing it as an <see cref="IChannelService"/> to GatewayHost.
/// <para>
/// For subprocess mode, manages the adapter process lifecycle (spawn, monitor, restart with backoff).
/// For WebSocket mode, waits for the adapter to connect and attach its transport via
/// <see cref="AttachTransport"/>.
/// </para>
/// </summary>
public sealed class ExternalChannelHost(
    ExternalChannelEntry config,
    ISessionService sessionService,
    string serverVersion,
    ModuleRegistry moduleRegistry,
    string hostWorkspacePath)
    : IChannelService
{
    private readonly ExternalChannelEntry _config = config ?? throw new ArgumentNullException(nameof(config));
    private readonly ISessionService _sessionService = sessionService ?? throw new ArgumentNullException(nameof(sessionService));
    private readonly ModuleRegistry _moduleRegistry = moduleRegistry ?? throw new ArgumentNullException(nameof(moduleRegistry));
    private readonly string _workspaceCraftPath = Path.Combine(hostWorkspacePath, ".craft");

    // Current transport/connection/handler — replaced on restart or reconnect
    private IAppServerTransport? _transport;
    private AppServerConnection? _connection;
    private AppServerRequestHandler? _handler;

    // Subprocess management
    private Process? _adapterProcess;
    private CancellationTokenSource? _runCts;

    // WebSocket mode: signaled when an adapter attaches via AppServerHost
    private TaskCompletionSource<(IAppServerTransport Transport, AppServerConnection Connection)>?
        _wsAttachTcs;

    // Restart backoff
    private int _consecutiveFailures;
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
    private const int MaxConsecutiveFailures = 5;

    // Heartbeat
    private Timer? _heartbeatTimer;
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HeartbeatTimeout = TimeSpan.FromSeconds(5);

    // State
    private volatile bool _stopped;
    private volatile bool _permanentlyFailed;

    // ─────────────────────────────────────────────────────────────────────────
    // IChannelService implementation
    // ─────────────────────────────────────────────────────────────────────────

    public string Name => _config.Name;

    public HeartbeatService? HeartbeatService { get; set; }

    public CronService? CronService { get; set; }

    /// <summary>
    /// External channels handle approval end-to-end via Wire Protocol.
    /// No server-side approval service is needed.
    /// </summary>
    public IApprovalService? ApprovalService => null;

    /// <summary>
    /// Platform client lives out-of-process, so this is always null.
    /// </summary>
    public object? ChannelClient => null;

    /// <summary>
    /// Starts the external channel adapter.
    /// For subprocess mode, spawns the process and enters the message loop.
    /// For WebSocket mode, waits for the adapter to connect and then enters the message loop.
    /// Blocks until stopped or cancelled.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _runCts.Token;

        AnsiConsole.MarkupLine(
            $"[green][[ExternalChannel]][/] Starting external channel [yellow]{Name}[/] ({_config.Transport})");

        try
        {
            while (!ct.IsCancellationRequested && !_permanentlyFailed)
            {
                try
                {
                    if (_config.Transport == ExternalChannelTransport.Subprocess)
                    {
                        await RunSubprocessCycleAsync(ct);
                    }
                    else
                    {
                        await RunWebSocketCycleAsync(ct);
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _consecutiveFailures++;

                    if (_consecutiveFailures >= MaxConsecutiveFailures)
                    {
                        _permanentlyFailed = true;
                        AnsiConsole.MarkupLine(
                            $"[red][[ExternalChannel]][/] Channel [yellow]{Name}[/] permanently failed " +
                            $"after {_consecutiveFailures} consecutive failures: {ex.Message}");
                        break;
                    }

                    var backoff = CalculateBackoff(_consecutiveFailures);
                    AnsiConsole.MarkupLine(
                        $"[yellow][[ExternalChannel]][/] Channel [yellow]{Name}[/] failed " +
                        $"(attempt {_consecutiveFailures}/{MaxConsecutiveFailures}): {ex.Message}. " +
                        $"Retrying in {backoff.TotalSeconds:F0}s...");

                    await Task.Delay(backoff, ct);
                }
            }
        }
        finally
        {
            _stopped = true;
            StopHeartbeatTimer();
            AnsiConsole.MarkupLine(
                $"[grey][[ExternalChannel]][/] Channel [yellow]{Name}[/] stopped");
        }
    }

    public async Task StopAsync()
    {
        _stopped = true;
        StopHeartbeatTimer();

        // Cancel the run loop
        if (_runCts is { } cts)
        {
            await cts.CancelAsync();
        }

        // Kill subprocess if running
        await TerminateSubprocessAsync();

        // Cancel WebSocket attach waiters
        _wsAttachTcs?.TrySetCanceled();

        // Clean up connection subscriptions
        _connection?.CancelAllSubscriptions();

        // Dispose transport
        if (_transport is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }

    /// <summary>
    /// Delivers a message to the adapter via <c>ext/channel/deliver</c>.
    /// Best-effort: returns silently if the adapter is disconnected.
    /// </summary>
    public async Task DeliverMessageAsync(string target, string content)
    {
        if (_stopped || _permanentlyFailed || _transport == null || _connection is not { IsClientReady: true })
            return;

        if (_connection is { SupportsDelivery: false })
            return;

        try
        {
            var response = await _transport.SendClientRequestAsync(
                AppServerMethods.ExtChannelDeliver,
                new { target, content },
                timeout: TimeSpan.FromSeconds(10));

            // The response.Result is a JsonElement? containing the adapter's reply
            if (response.Result is { } resultElement &&
                resultElement.TryGetProperty("delivered", out var delivered) &&
                delivered.ValueKind == JsonValueKind.False)
            {
                var errorMsg = resultElement.TryGetProperty("error", out var err) ? err.GetString() : "unknown";
                AnsiConsole.MarkupLine(
                    $"[yellow][[ExternalChannel]][/] Delivery to [yellow]{Name}[/] target '{target}' " +
                    $"failed: {errorMsg}");
            }
        }
        catch (Exception ex)
        {
            // Best-effort delivery — log but don't throw
            AnsiConsole.MarkupLine(
                $"[yellow][[ExternalChannel]][/] Delivery to [yellow]{Name}[/] failed: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WebSocket mode: transport attachment
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="AppServer.AppServerHost"/> when a WebSocket client completes the
    /// <c>initialize</c> handshake with a matching <c>channelAdapter.channelName</c>.
    /// The transport and connection are handed over to this host, which takes over
    /// the message loop.
    /// </summary>
    public void AttachTransport(IAppServerTransport transport, AppServerConnection connection)
    {
        _wsAttachTcs?.TrySetResult((transport, connection));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Subprocess cycle
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunSubprocessCycleAsync(CancellationToken ct)
    {
        // Spawn the adapter process
        var process = SpawnAdapterProcess();
        _adapterProcess = process;

        // Create transport from process streams
        // Note: StdioTransport reads from the process's stdout (our input),
        //       and writes to the process's stdin (our output).
        await using var transport = StdioTransport.Create(
            process.StandardOutput.BaseStream,
            process.StandardInput.BaseStream);
        transport.Start();

        _transport = transport;
        _connection = new AppServerConnection();
        _handler = new AppServerRequestHandler(
            _sessionService, _connection, transport,
            new ModuleRegistryChannelListContributor(_moduleRegistry, CronService, HeartbeatService),
            serverVersion,
            cronService: CronService,
            heartbeatService: HeartbeatService,
            workspaceCraftPath: _workspaceCraftPath,
            hostWorkspacePath: hostWorkspacePath);

        // Forward stderr to DotCraft's diagnostic log
        _ = ForwardStderrAsync(process, ct);

        AnsiConsole.MarkupLine(
            $"[green][[ExternalChannel]][/] Adapter [yellow]{Name}[/] spawned (PID {process.Id})");

        // Run the message loop
        await RunMessageLoopAsync(transport, _connection, _handler, ct);

        // Terminate the subprocess after the message loop exits.
        // When the loop exits due to heartbeat-timeout (transport disposed), the process
        // may still be running. Kill it first to avoid hanging on WaitForExitAsync.
        await TerminateSubprocessAsync();

        // Process exited — check if it was expected
        if (!ct.IsCancellationRequested && !process.HasExited)
        {
            await process.WaitForExitAsync(ct);
        }

        if (!ct.IsCancellationRequested && process.HasExited)
        {
            var exitCode = process.ExitCode;
            AnsiConsole.MarkupLine(
                $"[yellow][[ExternalChannel]][/] Adapter [yellow]{Name}[/] exited with code {exitCode}");

            if (exitCode != 0)
                throw new InvalidOperationException(
                    $"Adapter process exited with code {exitCode}");
        }

        // Reset on success
        _consecutiveFailures = 0;
    }

    private Process SpawnAdapterProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _config.Command!,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (_config.Args is { Count: > 0 })
        {
            foreach (var arg in _config.Args)
                startInfo.ArgumentList.Add(arg);
        }

        if (!string.IsNullOrEmpty(_config.WorkingDirectory))
            startInfo.WorkingDirectory = _config.WorkingDirectory;

        if (_config.Env is { Count: > 0 })
        {
            foreach (var (key, value) in _config.Env)
                startInfo.Environment[key] = value;
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                $"Failed to start adapter process: {_config.Command}");

        return process;
    }

    private static async Task ForwardStderrAsync(Process process, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(ct);
                if (line == null)
                    break;
                await Console.Error.WriteLineAsync(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* stderr forwarding is best-effort */ }
    }

    private async Task TerminateSubprocessAsync()
    {
        if (_adapterProcess is not { HasExited: false } process)
            return;

        try
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch { /* best-effort */ }
        finally
        {
            process.Dispose();
            _adapterProcess = null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WebSocket cycle
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunWebSocketCycleAsync(CancellationToken ct)
    {
        // Wait for an adapter to connect and attach its transport
        _wsAttachTcs = new TaskCompletionSource<(IAppServerTransport, AppServerConnection)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        AnsiConsole.MarkupLine(
            $"[grey][[ExternalChannel]][/] Waiting for WebSocket adapter [yellow]{Name}[/] to connect...");

        var (transport, connection) = await _wsAttachTcs.Task.WaitAsync(ct);

        _transport = transport;
        _connection = connection;
        _handler = new AppServerRequestHandler(
            _sessionService, connection, transport,
            new ModuleRegistryChannelListContributor(_moduleRegistry, CronService, HeartbeatService),
            serverVersion,
            cronService: CronService,
            heartbeatService: HeartbeatService,
            workspaceCraftPath: _workspaceCraftPath,
            hostWorkspacePath: hostWorkspacePath);

        AnsiConsole.MarkupLine(
            $"[green][[ExternalChannel]][/] WebSocket adapter [yellow]{Name}[/] connected " +
            $"(client: {connection.ClientInfo?.Name ?? "unknown"})");

        // The initialize handshake was already completed by AppServerHost before routing here,
        // and the 'initialized' notification has also been consumed. Start heartbeat probing
        // explicitly since it won't be triggered via HandleNotification in WebSocket mode.
        StartHeartbeatTimer();
        await RunMessageLoopAsync(transport, connection, _handler, ct);

        // Connection closed — reset for next connection
        _consecutiveFailures = 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Message loop (shared between subprocess and WebSocket modes)
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly SemaphoreSlim RequestGate = new(32, 32);

    private async Task RunMessageLoopAsync(
        IAppServerTransport transport,
        AppServerConnection connection,
        AppServerRequestHandler handler,
        CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                AppServerIncomingMessage? msg;
                try
                {
                    msg = await transport.ReadMessageAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (msg == null)
                    break; // EOF — adapter disconnected

                if (msg.IsNotification)
                {
                    HandleNotification(msg, handler);
                    continue;
                }

                if (!msg.IsRequest)
                    continue;

                // Reject if at capacity
                if (!await RequestGate.WaitAsync(0, ct))
                {
                    var overloadErr = AppServerErrors.ServerOverloaded().ToError();
                    await transport.WriteMessageAsync(
                        AppServerRequestHandler.BuildErrorResponse(msg.Id, overloadErr), ct);
                    continue;
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessRequestAsync(transport, handler, msg, ct);
                    }
                    finally
                    {
                        RequestGate.Release();
                    }
                }, ct);
            }
        }
        finally
        {
            StopHeartbeatTimer();
            connection.CancelAllSubscriptions();
        }
    }

    private static async Task ProcessRequestAsync(
        IAppServerTransport transport,
        AppServerRequestHandler handler,
        AppServerIncomingMessage msg,
        CancellationToken ct)
    {
        var previousTransport = AppServerRequestContext.CurrentTransport;
        AppServerRequestContext.CurrentTransport = transport;
        try
        {
            object? result;
            try
            {
                result = await handler.HandleRequestAsync(msg, ct);
            }
            catch (AppServerException ex)
            {
                await transport.WriteMessageAsync(
                    AppServerRequestHandler.BuildErrorResponse(msg.Id, ex.ToError()), ct);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                var internalErr = AppServerErrors.InternalError(ex.Message).ToError();
                await transport.WriteMessageAsync(
                    AppServerRequestHandler.BuildErrorResponse(msg.Id, internalErr), ct);
                await Console.Error.WriteLineAsync(
                    $"[ExternalChannel:{handler}] Internal error: {ex}");
                return;
            }

            // null result means the handler already sent the response inline (turn/start)
            if (result != null)
            {
                await transport.WriteMessageAsync(
                    AppServerRequestHandler.BuildResponse(msg.Id, result), ct);
            }
        }
        finally
        {
            AppServerRequestContext.CurrentTransport = previousTransport;
        }
    }

    private void HandleNotification(AppServerIncomingMessage msg, AppServerRequestHandler handler)
    {
        switch (msg.Method)
        {
            case AppServerMethods.Initialized:
                handler.HandleInitializedNotification();
                // Start heartbeat probing after adapter is ready
                StartHeartbeatTimer();
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Heartbeat
    // ─────────────────────────────────────────────────────────────────────────

    private void StartHeartbeatTimer()
    {
        StopHeartbeatTimer();
        _heartbeatTimer = new Timer(
            _ => _ = SendHeartbeatAsync(),
            state: null,
            dueTime: HeartbeatInterval,
            period: HeartbeatInterval);
    }

    private void StopHeartbeatTimer()
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;
    }

    private async Task SendHeartbeatAsync()
    {
        if (_stopped || _transport == null || _connection is not { IsClientReady: true })
            return;

        try
        {
            await _transport.SendClientRequestAsync(
                AppServerMethods.ExtChannelHeartbeat,
                new { },
                timeout: HeartbeatTimeout);

            // Heartbeat succeeded — connection is healthy
        }
        catch (OperationCanceledException) when (!_stopped && _runCts is { IsCancellationRequested: false })
        {
            // SendClientRequestAsync uses CancellationTokenSource.CancelAfter() for timeouts,
            // which throws TaskCanceledException (a subclass of OperationCanceledException).
            // If neither _stopped nor _runCts is cancelled, this is a heartbeat timeout.
            AnsiConsole.MarkupLine(
                $"[red][[ExternalChannel]][/] Heartbeat timeout for [yellow]{Name}[/] — " +
                "connection unhealthy, triggering reconnect");

            // Dispose the transport to trigger reconnect.
            // This causes ReadMessageAsync to return null, exiting RunMessageLoopAsync normally.
            // The StartAsync while-loop then retries the cycle.
            // NOTE: Do NOT cancel _runCts here — that would exit the while-loop permanently.
            if (_transport is IAsyncDisposable disposable)
                await disposable.DisposeAsync();
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                $"[yellow][[ExternalChannel]][/] Heartbeat error for [yellow]{Name}[/]: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Backoff
    // ─────────────────────────────────────────────────────────────────────────

    private static TimeSpan CalculateBackoff(int failures)
    {
        var seconds = Math.Min(
            InitialBackoff.TotalSeconds * Math.Pow(2, failures - 1),
            MaxBackoff.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IAsyncDisposable
    // ─────────────────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _runCts?.Dispose();
    }
}
