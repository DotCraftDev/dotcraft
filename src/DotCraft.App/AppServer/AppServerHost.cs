using System.ClientModel;
using System.Net.WebSockets;
using DotCraft.Agents;
using Microsoft.Extensions.Logging;
using DotCraft.Common;
using DotCraft.Configuration;
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
    ModuleRegistry moduleRegistry) : IDotCraftHost
{
    private AgentFactory? _agentFactory;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var traceCollector = sp.GetService<TraceCollector>();
        var cronTools = sp.GetService<Cron.CronTools>();

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
            planStore: planStore);

        var agent = _agentFactory.CreateAgentForMode(AgentMode.Agent);
        var sessionService = SessionServiceFactory.Create(_agentFactory, agent, sp);

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

        AnsiConsole.MarkupLine("[grey][[AppServer]][/] AppServer stopped");
    }

    // -------------------------------------------------------------------------
    // Run modes
    // -------------------------------------------------------------------------

    private static async Task RunStdioOnlyAsync(
        ISessionService sessionService,
        CancellationToken cancellationToken)
    {
        await using var transport = StdioTransport.CreateStdio();
        transport.Start();

        var connection = new AppServerConnection();
        var handler = new AppServerRequestHandler(
            sessionService, connection, transport, serverVersion: AppVersion.Informational);

        AnsiConsole.MarkupLine("[green][[AppServer]][/] DotCraft AppServer started (stdio JSON-RPC 2.0)");

        await RunLoopAsync(transport, connection, handler, cancellationToken);
    }

    private static async Task RunWebSocketOnlyAsync(
        WebSocketServerConfig wsConfig,
        ISessionService sessionService,
        CancellationToken cancellationToken)
    {
        var (wsApp, wsUrl) = BuildWebSocketApp(wsConfig, sessionService, cancellationToken);

        AnsiConsole.MarkupLine(
            $"[green][[AppServer]][/] DotCraft AppServer started (WebSocket at ws://{wsConfig.Host}:{wsConfig.Port}/ws)");

        // The WebSocket server IS the main loop — RunAsync blocks until shutdown.
        await wsApp.RunAsync(wsUrl);
    }

    private static async Task RunStdioWithWebSocketAsync(
        WebSocketServerConfig wsConfig,
        ISessionService sessionService,
        CancellationToken cancellationToken)
    {
        // Start the WebSocket listener first, then enter the stdio loop.
        var (wsApp, wsUrl) = BuildWebSocketApp(wsConfig, sessionService, cancellationToken);
        var wsRunTask = wsApp.RunAsync(wsUrl);

        AnsiConsole.MarkupLine(
            $"[green][[AppServer]][/] WebSocket listener started at ws://{wsConfig.Host}:{wsConfig.Port}/ws");

        await using var transport = StdioTransport.CreateStdio();
        transport.Start();

        var connection = new AppServerConnection();
        var handler = new AppServerRequestHandler(
            sessionService, connection, transport, serverVersion: AppVersion.Informational);

        AnsiConsole.MarkupLine("[green][[AppServer]][/] DotCraft AppServer started (stdio + WebSocket)");

        try
        {
            await RunLoopAsync(transport, connection, handler, cancellationToken);
        }
        finally
        {
            // Stop the WebSocket server when stdio exits
            await wsApp.StopAsync(CancellationToken.None);
            try { await wsRunTask; } catch { /* ignored */ }
        }
    }

    // -------------------------------------------------------------------------
    // WebSocket server
    // -------------------------------------------------------------------------

    private static (WebApplication App, string Url) BuildWebSocketApp(
        WebSocketServerConfig wsConfig,
        ISessionService sessionService,
        CancellationToken hostCt)
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
            var wsHandler = new AppServerRequestHandler(
                sessionService, wsConnection, wsTransport, serverVersion: AppVersion.Informational);

            await RunLoopAsync(wsTransport, wsConnection, wsHandler, hostCt);
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
}
