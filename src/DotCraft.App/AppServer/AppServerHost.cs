using System.ClientModel;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using DotCraft.Agents;
using Microsoft.Extensions.Logging;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Hosting;
using DotCraft.Memory;
using DotCraft.Mcp;
using DotCraft.Modules;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;

namespace DotCraft.AppServer;

/// <summary>
/// Host for AppServer mode.
/// Runs a stdio JSON-RPC 2.0 server that exposes <see cref="ISessionService"/> over the
/// Session Wire Protocol. When <see cref="WebSocketServerConfig"/> is present in configuration,
/// additionally starts a WebSocket listener that accepts multiple concurrent connections,
/// each with an isolated <see cref="AppServerConnection"/> sharing the same session service.
/// </summary>
public sealed class AppServerHost(
    IServiceProvider sp,
    AppConfig config,
    DotCraftPaths paths,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    McpClientManager mcpClientManager,
    ModuleRegistry moduleRegistry,
    ExternalChannel.ExternalChannelRegistry? externalChannelRegistry = null) : IDotCraftHost
{
    private AgentFactory? _agentFactory;

    /// <summary>
    /// Thread-safe set of currently connected transports. Used to broadcast
    /// out-of-band notifications (e.g. <c>plan/updated</c>) to all clients.
    /// </summary>
    private readonly ConcurrentDictionary<IAppServerTransport, AppServerConnection> _activeTransports = new();

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var traceCollector = sp.GetService<TraceCollector>();
        var cronTools = sp.GetService<CronTools>();

        ToolProviderCollector.ScanToolIcons(moduleRegistry, config);
        var toolProviders = ToolProviderCollector.Collect(moduleRegistry, config);

        // SessionScopedApprovalService delegates per-turn approval to the SessionApprovalService
        // that is installed by SessionService for each turn's event stream. The AutoApproveApprovalService
        // acts as the fallback when no turn override is active (should never happen in normal flow).
        var fallbackApproval = new AutoApproveApprovalService();
        var scopedApproval = new SessionScopedApprovalService(fallbackApproval);

        var planStore = new PlanStore(paths.CraftPath);

        _agentFactory = new AgentFactory(
            paths.CraftPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader,
            approvalService: scopedApproval,
            blacklist,
            toolProviders: toolProviders,
            toolProviderContext: new Abstractions.ToolProviderContext
            {
                Config = config,
                ChatClient = new OpenAIClient(
                    new ApiKeyCredential(config.ApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(config.EndPoint) })
                    .GetChatClient(config.Model),
                WorkspacePath = paths.WorkspacePath,
                BotPath = paths.CraftPath,
                MemoryStore = memoryStore,
                SkillsLoader = skillsLoader,
                ApprovalService = scopedApproval,
                PathBlacklist = blacklist,
                CronTools = cronTools,
                McpClientManager = mcpClientManager.Tools.Count > 0 ? mcpClientManager : null,
                TraceCollector = traceCollector
            },
            traceCollector: traceCollector,
            planStore: planStore,
            onPlanUpdated: BroadcastPlanUpdated);

        var agent = _agentFactory.CreateAgentForMode(AgentMode.Agent);
        var sessionService = SessionServiceFactory.Create(_agentFactory, agent, sp);

        // Cron and Heartbeat — owned and executed entirely within the AppServer process.
        // The agent stack (sessionService, agentFactory) lives here, so execution is
        // correct and concurrency-safe. Results are delivered via system/jobResult wire
        // notifications to connected CLI clients.
        var cronService = sp.GetRequiredService<CronService>();
        // quiet=true suppresses verbose progress lines; results are delivered via
        // system/jobResult wire notifications instead of console output.
        var runner = new AgentRunner(paths.WorkspacePath, sessionService, quiet: true);

        cronService.OnJob = async job =>
        {
            var sessionKey = $"cron:{job.Id}";
            string? result;
            try
            {
                result = await runner.RunAsync(job.Payload.Message, sessionKey, cancellationToken);
            }
            catch (Exception ex)
            {
                result = null;
                AnsiConsole.MarkupLine($"[grey][[AppServer]][/] [red]Cron job {job.Id} failed: {Markup.Escape(ex.Message)}[/]");
            }

            // Deliver result: non-CLI channels (QQ, WeCom) require Gateway mode and
            // MessageRouter. For CLI-originated or unattributed jobs, broadcast via wire.
            var channel = job.Payload.Channel;
            if (job.Payload.Deliver && result != null
                && (channel == null || string.Equals(channel, "cli", StringComparison.OrdinalIgnoreCase)))
            {
                BroadcastJobResult("cron", job.Id, job.Name, result, error: null);
            }
        };

        using var heartbeatService = new HeartbeatService(
            paths.CraftPath,
            onHeartbeat: async (prompt, sessionKey, ct) =>
            {
                string? result;
                try
                {
                    result = await runner.RunAsync(prompt, sessionKey, ct);
                }
                catch (Exception ex)
                {
                    result = null;
                    AnsiConsole.MarkupLine($"[grey][[AppServer]][/] [red]Heartbeat run failed: {Markup.Escape(ex.Message)}[/]");
                }

                if (result != null)
                    BroadcastJobResult("heartbeat", jobId: null, jobName: null, result, error: null);

                return result;
            },
            intervalSeconds: config.Heartbeat.IntervalSeconds,
            enabled: config.Heartbeat.Enabled);

        if (config.Cron.Enabled)
        {
            cronService.Start();
            AnsiConsole.MarkupLine($"[grey][[AppServer]][/] Cron service started ({cronService.ListJobs().Count} jobs)");
        }

        if (config.Heartbeat.Enabled)
        {
            heartbeatService.Start();
            AnsiConsole.MarkupLine($"[grey][[AppServer]][/] Heartbeat started (interval: {config.Heartbeat.IntervalSeconds}s)");
        }

        var appServerConfig = config.GetSection<AppServerConfig>("AppServer");

        switch (appServerConfig.Mode)
        {
            case AppServerMode.WebSocket:
                // -------------------------------------------------------------------
                // Pure WebSocket mode: no stdio transport; the WebSocket server is
                // the main loop. Stdout remains available for normal console output.
                // -------------------------------------------------------------------
                await RunWebSocketOnlyAsync(appServerConfig.WebSocket, sessionService, cancellationToken);
                break;

            case AppServerMode.StdioAndWebSocket:
                // -------------------------------------------------------------------
                // Dual mode: stdio main loop + WebSocket listener running in parallel.
                // -------------------------------------------------------------------
                await RunStdioWithWebSocketAsync(appServerConfig.WebSocket, sessionService, cancellationToken);
                break;

            default:
                // -------------------------------------------------------------------
                // Stdio-only mode (default): standard subprocess JSON-RPC over stdio.
                // -------------------------------------------------------------------
                await RunStdioOnlyAsync(sessionService, cancellationToken);
                break;
        }

        cronService.Stop();
        AnsiConsole.MarkupLine("[grey][[AppServer]][/] AppServer stopped");
    }

    // -------------------------------------------------------------------------
    // Run modes
    // -------------------------------------------------------------------------

    private async Task RunStdioOnlyAsync(
        ISessionService sessionService,
        CancellationToken cancellationToken)
    {
        await using var transport = StdioTransport.CreateStdio();
        transport.Start();

        var connection = new AppServerConnection();
        _activeTransports.TryAdd(transport, connection);

        var handler = new AppServerRequestHandler(
            sessionService, connection, transport, serverVersion: AppVersion.Informational);

        AnsiConsole.MarkupLine("[green][[AppServer]][/] DotCraft AppServer started (stdio JSON-RPC 2.0)");

        try
        {
            await RunLoopAsync(transport, connection, handler, cancellationToken);
        }
        finally
        {
            _activeTransports.TryRemove(transport, out _);
        }
    }

    private async Task RunWebSocketOnlyAsync(
        WebSocketServerConfig wsConfig,
        ISessionService sessionService,
        CancellationToken cancellationToken)
    {
        var (wsApp, wsUrl) = BuildWebSocketApp(wsConfig, sessionService, cancellationToken, externalChannelRegistry);

        AnsiConsole.MarkupLine(
            $"[green][[AppServer]][/] DotCraft AppServer started (WebSocket at ws://{wsConfig.Host}:{wsConfig.Port}/ws)");

        // The WebSocket server IS the main loop — RunAsync blocks until shutdown.
        await wsApp.RunAsync(wsUrl);
    }

    private async Task RunStdioWithWebSocketAsync(
        WebSocketServerConfig wsConfig,
        ISessionService sessionService,
        CancellationToken cancellationToken)
    {
        // Build the WebSocket app and start it explicitly so that bind failures
        // surface immediately (fail-fast) instead of being deferred to finally.
        var (wsApp, wsUrl) = BuildWebSocketApp(wsConfig, sessionService, cancellationToken, externalChannelRegistry);
        wsApp.Urls.Add(wsUrl);
        await wsApp.StartAsync(cancellationToken);

        AnsiConsole.MarkupLine(
            $"[green][[AppServer]][/] WebSocket listener started at ws://{wsConfig.Host}:{wsConfig.Port}/ws");

        await using var transport = StdioTransport.CreateStdio();
        transport.Start();

        var connection = new AppServerConnection();
        _activeTransports.TryAdd(transport, connection);

        var handler = new AppServerRequestHandler(
            sessionService, connection, transport, serverVersion: AppVersion.Informational);

        AnsiConsole.MarkupLine("[green][[AppServer]][/] DotCraft AppServer started (stdio + WebSocket)");

        try
        {
            await RunLoopAsync(transport, connection, handler, cancellationToken);
        }
        finally
        {
            _activeTransports.TryRemove(transport, out _);
            // Stop the WebSocket server when stdio exits
            await wsApp.StopAsync(CancellationToken.None);
        }
    }

    // -------------------------------------------------------------------------
    // WebSocket server
    // -------------------------------------------------------------------------

    private (WebApplication App, string Url) BuildWebSocketApp(
        WebSocketServerConfig wsConfig,
        ISessionService sessionService,
        CancellationToken hostCt,
        ExternalChannel.ExternalChannelRegistry? channelRegistry = null)
    {
        // Refuse to start if the binding is non-loopback without a token (spec §15.4)
        var isLoopback = wsConfig.Host is "127.0.0.1" or "::1" or "[::1]" or "localhost";
        if (!isLoopback && string.IsNullOrEmpty(wsConfig.Token))
            throw new InvalidOperationException(
                "WebSocket listener bound to a non-loopback address requires a bearer token (AppServer.WebSocket.Token).");

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        var app = builder.Build();
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30)
        });

        // WebSocket upgrade endpoint
        app.Map("/ws", async context =>
        {
            // Token authentication: require ?token= when a token is configured (spec §15.4)
            if (!string.IsNullOrEmpty(wsConfig.Token))
            {
                var supplied = context.Request.Query["token"].FirstOrDefault();
                if (!string.Equals(supplied, wsConfig.Token, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Unauthorized");
                    return;
                }
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("WebSocket upgrade required");
                return;
            }

            using WebSocket ws = await context.WebSockets.AcceptWebSocketAsync();
            await using var wsTransport = new WebSocketTransport(ws);
            wsTransport.Start();

            var wsConnection = new AppServerConnection();
            _activeTransports.TryAdd(wsTransport, wsConnection);
            try
            {
                var wsHandler = new AppServerRequestHandler(
                    sessionService, wsConnection, wsTransport, serverVersion: AppVersion.Informational);

                // ── Channel adapter routing (external-channel-adapter.md §4.2) ──
                //
                // Process the first message (must be 'initialize') manually. After the
                // handshake, if the client declared channelAdapter capability, route the
                // connection to the matching ExternalChannelHost and exit this handler.
                if (channelRegistry != null)
                {
                    var firstMsg = await wsTransport.ReadMessageAsync(hostCt);
                    if (firstMsg == null)
                        return; // Client disconnected before sending anything

                    if (firstMsg.IsRequest && firstMsg.Method == AppServerMethods.Initialize)
                    {
                        // Process the initialize request normally
                        await ProcessRequestAsync(wsTransport, wsHandler, firstMsg, hostCt);

                        // Check if this is a channel adapter connection
                        if (wsConnection.IsChannelAdapter)
                        {
                            var channelName = wsConnection.ChannelAdapterName!;

                            if (channelRegistry.TryGet(channelName, out var host) && host != null)
                            {
                                // Wait for the 'initialized' notification before handover
                                var initdMsg = await wsTransport.ReadMessageAsync(hostCt);
                                if (initdMsg is { IsNotification: true, Method: AppServerMethods.Initialized })
                                {
                                    wsHandler.HandleInitializedNotification();
                                }

                                // Hand over transport and connection to the ExternalChannelHost.
                                // The host takes over the message loop; this handler returns.
                                host.AttachTransport(wsTransport, wsConnection);

                                // Block this handler until the transport's reader loop finishes
                                // (i.e. the WebSocket closes). This keeps the WebSocket and
                                // transport alive (they are 'using' scoped) without performing
                                // any additional ReceiveAsync calls on the raw WebSocket.
                                await wsTransport.Completed;
                                return;
                            }

                            // Channel name not registered — reject with system/event
                            AnsiConsole.MarkupLine(
                                $"[yellow][[AppServer]][/] Rejected channel adapter '{channelName}': " +
                                "not registered in ExternalChannels configuration.");

                            await wsTransport.WriteMessageAsync(new
                            {
                                jsonrpc = "2.0",
                                method = AppServerMethods.SystemEvent,
                                @params = new
                                {
                                    kind = "channelRejected",
                                    channelName,
                                    message = $"Channel '{channelName}' is not registered in server configuration."
                                }
                            }, hostCt);

                            return; // Close connection
                        }

                        // Not a channel adapter — fall through to normal RunLoopAsync
                        // (initialize already processed, loop will handle subsequent messages)
                        await RunLoopAsync(wsTransport, wsConnection, wsHandler, hostCt);
                        return;
                    }

                    // First message was not initialize — process normally and enter loop
                    if (firstMsg.IsNotification)
                    {
                        HandleNotification(firstMsg, wsHandler);
                    }
                    else if (firstMsg.IsRequest)
                    {
                        await ProcessRequestAsync(wsTransport, wsHandler, firstMsg, hostCt);
                    }
                }

                await RunLoopAsync(wsTransport, wsConnection, wsHandler, hostCt);
            } // end try
            finally
            {
                _activeTransports.TryRemove(wsTransport, out _);
            }
        });

        // Health probes (spec §15.2)
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/readyz", () => Results.Ok(new { status = "ok" }));

        return (app, $"http://{wsConfig.Host}:{wsConfig.Port}");
    }

    // -------------------------------------------------------------------------
    // Main message loop
    // -------------------------------------------------------------------------

    // Fix 9: Bounded concurrency gate — at most 32 concurrent requests.
    // When full, new requests receive -32001 (server overloaded).
    private static readonly SemaphoreSlim RequestGate = new(32, 32);

    private static async Task RunLoopAsync(
        IAppServerTransport transport,
        AppServerConnection connection,
        AppServerRequestHandler handler,
        CancellationToken ct)
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
                break; // EOF — client disconnected

            if (msg.IsNotification)
            {
                HandleNotification(msg, handler);
                continue;
            }

            if (!msg.IsRequest)
                continue; // Ignore unexpected responses (approval responses are routed by transport)

            // Reject immediately if the server is at capacity.
            if (!await RequestGate.WaitAsync(0, ct))
            {
                var overloadErr = AppServerErrors.ServerOverloaded().ToError();
                await transport.WriteMessageAsync(
                    AppServerRequestHandler.BuildErrorResponse(msg.Id, overloadErr), ct);
                continue;
            }

            // Process each request concurrently so turn/interrupt can be handled while
            // a long-running turn/start is streaming events.
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

        // Clean up active subscriptions when the client disconnects
        connection.CancelAllSubscriptions();
    }

    private static async Task ProcessRequestAsync(
        IAppServerTransport transport,
        AppServerRequestHandler handler,
        AppServerIncomingMessage msg,
        CancellationToken ct)
    {
        object? result;
        try
        {
            result = await handler.HandleRequestAsync(msg, ct);
        }
        catch (AppServerException ex)
        {
            await transport.WriteMessageAsync(AppServerRequestHandler.BuildErrorResponse(msg.Id, ex.ToError()), ct);
            return;
        }
        catch (OperationCanceledException)
        {
            // Request cancelled — no response needed
            return;
        }
        catch (Exception ex)
        {
            var internalErr = AppServerErrors.InternalError(ex.Message).ToError();
            await transport.WriteMessageAsync(AppServerRequestHandler.BuildErrorResponse(msg.Id, internalErr), ct);
            await Console.Error.WriteLineAsync($"[AppServer] Internal error: {ex}");
            return;
        }

        // null result means the handler already sent the response inline (turn/start)
        if (result != null)
        {
            await transport.WriteMessageAsync(
                AppServerRequestHandler.BuildResponse(msg.Id, result), ct);
        }
    }

    private static void HandleNotification(AppServerIncomingMessage msg, AppServerRequestHandler handler)
    {
        switch (msg.Method)
        {
            case AppServerMethods.Initialized:
                handler.HandleInitializedNotification();
                break;
            // Other client notifications (none defined in v1) are silently ignored
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_agentFactory != null)
            await _agentFactory.DisposeAsync();
    }

    /// <summary>
    /// Broadcasts a <c>system/jobResult</c> JSON-RPC notification to all connected transports.
    /// Called when a server-managed cron or heartbeat job completes and the job was created from
    /// a CLI (non-social-channel) context. See spec Section 6.9.
    /// </summary>
    private void BroadcastJobResult(string source, string? jobId, string? jobName, string? result, string? error)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.SystemJobResult,
            @params = new
            {
                source,
                jobId,
                jobName,
                result,
                error
            }
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.SystemJobResult))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
                }
                catch
                {
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

    /// <summary>
    /// Broadcasts a <c>plan/updated</c> JSON-RPC notification to all connected transports.
    /// Called from the <c>onPlanUpdated</c> callback injected into <see cref="AgentFactory"/>.
    /// The callback fires synchronously on the tool execution thread; transport writes are
    /// thread-safe (both stdio and WebSocket transports use internal write locks).
    /// </summary>
    private void BroadcastPlanUpdated(StructuredPlan plan)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.PlanUpdated,
            @params = new
            {
                title = plan.Title,
                overview = plan.Overview,
                todos = plan.Todos.Select(t => new
                {
                    id = t.Id,
                    content = t.Content,
                    priority = t.Priority,
                    status = t.Status
                }).ToArray()
            }
        };

        // Fire-and-forget broadcast to all connected clients.
        // Errors on individual transports (e.g. disconnected) are silently ignored.
        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.PlanUpdated))
                continue;

            _ = Task.Run(async () =>
            {
                try
                {
                    await transport.WriteMessageAsync(notification, CancellationToken.None);
                }
                catch
                {
                    // Transport may have been disposed or disconnected; remove it.
                    _activeTransports.TryRemove(transport, out _);
                }
            });
        }
    }

}
