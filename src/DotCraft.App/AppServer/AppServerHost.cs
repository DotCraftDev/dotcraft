using System.ClientModel;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using DotCraft.Abstractions;
using DotCraft.Agents;
using Microsoft.Extensions.Logging;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Logging;
using DotCraft.Hosting;
using DotCraft.Lsp;
using DotCraft.Memory;
using DotCraft.Mcp;
using DotCraft.Modules;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Orchestrator;
using DotCraft.Automations.Protocol;
using DotCraft.Tracing;
using DotCraft.ExternalChannel;
using DotCraft.Channels;
using DotCraft.Gateway;
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
    LspServerManager lspServerManager,
    ModuleRegistry moduleRegistry,
    ExternalChannelRegistry? externalChannelRegistry = null) : IDotCraftHost
{
    private AgentFactory? _agentFactory;

    /// <summary>
    /// Routes <c>ext/acp/*</c> from the agent to the wire client bound per thread (spec §11.2).
    /// </summary>
    private WireAcpExtensionProxy? _wireAcpExtensionProxy;

    /// <summary>
    /// Cron service instance owned by this AppServer process. Set during RunAsync and passed to
    /// request handlers so wire clients can manage cron jobs via the cron/* wire methods (spec §16).
    /// </summary>
    private CronService? _cronService;

    /// <summary>
    /// Heartbeat service instance owned by this AppServer process. Set during RunAsync and passed
    /// to request handlers so wire clients can trigger heartbeats via heartbeat/trigger (spec §17).
    /// </summary>
    private HeartbeatService? _heartbeatService;

    private IAutomationsRequestHandler? _automationsHandler;

    private ChannelRunner? _channelRunner;

    /// <summary>
    /// DashBoard URL when <see cref="ChannelRunner"/> hosts it; exposed via wire <c>initialize</c>.
    /// </summary>
    private string? _dashboardUrl;
    private IReadOnlyList<ConfigSchemaSection> _configSchema = [];
    private readonly IAppConfigMonitor _appConfigMonitor = sp.GetRequiredService<IAppConfigMonitor>();

    /// <summary>
    /// Thread-safe set of currently connected transports. Used to broadcast
    /// out-of-band notifications (e.g. <c>plan/updated</c>) to all clients.
    /// </summary>
    private readonly ConcurrentDictionary<IAppServerTransport, AppServerConnection> _activeTransports = new();

    private IReadOnlyList<IAppServerProtocolExtension> ProtocolExtensions =>
        sp.GetServices<IAppServerProtocolExtension>().ToArray();

    private ModuleRegistryChannelListContributor CreateChannelListContributor() =>
        new(moduleRegistry, _cronService, _heartbeatService);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _appConfigMonitor.Changed += OnAppConfigChanged;
        skillsLoader.SetDisabledSkills(config.Skills.DisabledSkills);

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

        _wireAcpExtensionProxy = new WireAcpExtensionProxy();

        _agentFactory = new AgentFactory(
            paths.CraftPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader,
            approvalService: scopedApproval,
            blacklist,
            toolProviders: toolProviders,
            toolProviderContext: new ToolProviderContext
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
                LspServerManager = lspServerManager,
                TraceCollector = traceCollector,
                AcpExtensionProxy = _wireAcpExtensionProxy
            },
            traceCollector: traceCollector,
            planStore: planStore,
            onPlanUpdated: BroadcastPlanUpdated);

        if (sp.GetService<IChannelRuntimeToolProvider>() is ExternalChannelToolProvider externalChannelToolProvider)
        {
            externalChannelToolProvider.ConfigureReservedToolNames(
                _agentFactory.CreateToolsForMode(AgentMode.Agent).Select(tool => tool.Name));
        }

        var agent = _agentFactory.CreateAgentForMode(AgentMode.Agent);
        var sessionService = SessionServiceFactory.Create(_agentFactory, agent, sp);
        sessionService.ThreadCreatedForBroadcast = BroadcastThreadStarted;
        sessionService.ThreadDeletedForBroadcast = BroadcastThreadDeleted;
        sessionService.ThreadRenamedForBroadcast = BroadcastThreadRenamed;
        var commitMessageSuggest = new CommitMessageSuggestService(sessionService, paths.WorkspacePath);
        mcpClientManager.StatusChanged += OnMcpStatusChanged;

        // Cron and Heartbeat — owned and executed entirely within the AppServer process.
        // The agent stack (sessionService, agentFactory) lives here, so execution is
        // correct and concurrency-safe. Results are delivered via system/jobResult wire
        // notifications to connected CLI clients.
        var cronService = sp.GetRequiredService<CronService>();
        _cronService = cronService;
        // quiet=true suppresses verbose progress lines; results are delivered via
        // system/jobResult wire notifications instead of console output.
        var runner = new AgentRunner(paths.WorkspacePath, sessionService, quiet: true);

        var messageRouter = sp.GetRequiredService<MessageRouter>();

        cronService.CronJobPersistedAfterExecution = (job, id, removed) =>
        {
            if (removed)
                BroadcastCronStateChanged(new CronJobWireInfo { Id = id }, removed: true);
            else if (job != null)
                BroadcastCronStateChanged(CronJobWireMapping.ToWire(job), removed: false);
        };

        using var heartbeatService = new HeartbeatService(
            paths.CraftPath,
            onHeartbeat: async (prompt, sessionKey, threadDisplayName, ct) =>
            {
                try
                {
                    var run = await runner.RunAsync(prompt, sessionKey, threadDisplayName, ct);
                    if (run != null && run.Error == null && run.Result != null)
                        BroadcastJobResult("heartbeat", jobId: null, jobName: null, run.Result, error: null, run.ThreadId, run.InputTokens, run.OutputTokens);
                    else if (run != null && run.Error != null)
                        BroadcastJobResult("heartbeat", null, null, result: null, error: run.Error, run.ThreadId, run.InputTokens, run.OutputTokens);
                    return run;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[grey][[AppServer]][/] [red]Heartbeat run failed: {Markup.Escape(ex.Message)}[/]");
                    return null;
                }
            },
            intervalSeconds: config.Heartbeat.IntervalSeconds,
            enabled: config.Heartbeat.Enabled,
            logger: sp.GetService<ILoggerFactory>()?.CreateLogger<HeartbeatService>());
        _heartbeatService = heartbeatService;

        _automationsHandler = sp.GetService<IAutomationsRequestHandler>();
        var orchestrator = sp.GetService<AutomationOrchestrator>();
        AutomationOrchestrator? automationOrchestratorStarted = null;
        if (orchestrator != null)
        {
            _ = new AutomationsEventDispatcher(orchestrator, (task, _) =>
                BroadcastAutomationTaskUpdated(task));

            // Desktop and other AppServer clients need the poll loop to dispatch local tasks;
            // Gateway mode does this via AutomationsChannelService — here we reuse the same session.
            var automationSessionClient = new AutomationSessionClient(sessionService, paths);
            orchestrator.SetSessionClient(automationSessionClient);
            await orchestrator.StartAsync(cancellationToken);
            automationOrchestratorStarted = orchestrator;
        }

        // Native channels, external channels, and DashBoard share WebHostPool + MessageRouter (Gateway parity).
        _dashboardUrl = null;
        _channelRunner = ChannelRunner.TryCreateForAppServer(sp, config, paths, moduleRegistry);
        if (_channelRunner != null)
        {
            _channelRunner.Initialize(sessionService, heartbeatService, cronService);
            await _channelRunner.StartWebPoolAsync();
            _dashboardUrl = _channelRunner.DashBoardUrl;
        }

        cronService.OnJob = async job =>
        {
            var sessionKey = $"cron:{job.Id}";
            AgentRunResult? run;
            try
            {
                run = await runner.RunAsync(job.Payload.Message, sessionKey, job.Name, cancellationToken);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[grey][[AppServer]][/] [red]Cron job {job.Id} failed: {Markup.Escape(ex.Message)}[/]");
                return new CronOnJobResult(null, null, ex.Message, false, null, null);
            }

            var channel = job.Payload.Channel;
            var isCliChannel = channel == null
                || string.Equals(channel, "cli", StringComparison.OrdinalIgnoreCase);
            if (job.Payload.Deliver && isCliChannel)
            {
                if (run != null && run.Error == null)
                    BroadcastJobResult("cron", job.Id, job.Name, run.Result, error: null, run.ThreadId, run.InputTokens, run.OutputTokens);
                else if (run != null && run.Error != null)
                    BroadcastJobResult("cron", job.Id, job.Name, result: null, error: run.Error, run.ThreadId, run.InputTokens, run.OutputTokens);
            }
            else if (job.Payload.Deliver
                     && !isCliChannel
                     && !string.IsNullOrEmpty(channel))
            {
                var target = job.Payload.To ?? job.Payload.CreatorId ?? "";
                var content = run?.Error == null
                    ? (run?.Result ?? "")
                    : $"[Cron] {job.Name}\n{run.Error}";
                if (!string.IsNullOrEmpty(content) || run?.Error != null)
                    await messageRouter.DeliverAsync(
                        channel,
                        target,
                        new ChannelOutboundMessage
                        {
                            Kind = "text",
                            Text = content
                        });
            }

            var ok = run != null && run.Error == null;
            return new CronOnJobResult(run?.ThreadId, run?.Result, run?.Error, ok, run?.InputTokens, run?.OutputTokens);
        };

        if (config.Heartbeat.NotifyAdmin)
        {
            heartbeatService.OnResult = async result =>
                await messageRouter.BroadcastToAdminsAsync($"[Heartbeat] {result}");
        }

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

        _channelRunner?.BeginChannelLoops(cancellationToken);

        _configSchema = ConfigSchemaBuilder.BuildAll(ConfigSchemaRegistrations.GetAllConfigTypes());
        var appServerConfig = config.GetSection<AppServerConfig>("AppServer");

        try
        {
            switch (appServerConfig.Mode)
            {
                case AppServerMode.WebSocket:
                    // -------------------------------------------------------------------
                    // Pure WebSocket mode: no stdio transport; the WebSocket server is
                    // the main loop. Stdout remains available for normal console output.
                    // -------------------------------------------------------------------
                    await RunWebSocketOnlyAsync(appServerConfig.WebSocket, sessionService, commitMessageSuggest, cancellationToken);
                    break;

                case AppServerMode.StdioAndWebSocket:
                    // -------------------------------------------------------------------
                    // Dual mode: stdio main loop + WebSocket listener running in parallel.
                    // -------------------------------------------------------------------
                    await RunStdioWithWebSocketAsync(appServerConfig.WebSocket, sessionService, commitMessageSuggest, cancellationToken);
                    break;

                default:
                    // -------------------------------------------------------------------
                    // Stdio-only mode (default): standard subprocess JSON-RPC over stdio.
                    // -------------------------------------------------------------------
                    await RunStdioOnlyAsync(sessionService, commitMessageSuggest, cancellationToken);
                    break;
            }
        }
        finally
        {
            _appConfigMonitor.Changed -= OnAppConfigChanged;
            if (_channelRunner != null)
            {
                try
                {
                    await _channelRunner.DisposeAsync();
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine(
                        $"[grey][[AppServer]][/] [yellow]Channel runner shutdown: {Markup.Escape(ex.Message)}[/]");
                }

                _channelRunner = null;
            }

            if (automationOrchestratorStarted != null)
                await automationOrchestratorStarted.StopAsync();
        }

        cronService.Stop();
        AnsiConsole.MarkupLine("[grey][[AppServer]][/] AppServer stopped");
    }

    // -------------------------------------------------------------------------
    // Run modes
    // -------------------------------------------------------------------------

    private async Task RunStdioOnlyAsync(
        ISessionService sessionService,
        ICommitMessageSuggestService commitMessageSuggest,
        CancellationToken cancellationToken)
    {
        await using var transport = StdioTransport.CreateStdio();
        transport.Start();

        var connection = new AppServerConnection();
        _activeTransports.TryAdd(transport, connection);

        var handler = new AppServerRequestHandler(
            sessionService, connection, transport, CreateChannelListContributor(),
            serverVersion: AppVersion.Informational,
            cronService: _cronService, heartbeatService: _heartbeatService,
            skillsLoader: skillsLoader, workspaceCraftPath: paths.CraftPath,
            hostWorkspacePath: paths.WorkspacePath,
            automationsHandler: _automationsHandler,
            broadcastCronStateChanged: BroadcastCronStateChanged,
            commitMessageSuggest: commitMessageSuggest,
            dashboardUrl: _dashboardUrl,
            wireAcpExtensionProxy: _wireAcpExtensionProxy,
            channelStatusProvider: _channelRunner,
            mcpClientManager: mcpClientManager,
            broadcastMcpStatusChanged: BroadcastMcpStatusChanged,
            protocolExtensions: ProtocolExtensions,
            onExternalChannelUpserted: (channel, ct) =>
                _channelRunner?.ApplyExternalChannelUpsertAsync(channel, ct) ?? Task.CompletedTask,
            onExternalChannelRemoved: (channelName, ct) =>
                _channelRunner?.ApplyExternalChannelRemoveAsync(channelName, ct) ?? Task.CompletedTask,
            streamDebugLogger: sp.GetService<SessionStreamDebugLogger>(),
            configSchema: _configSchema,
            appConfigMonitor: _appConfigMonitor);

        AnsiConsole.MarkupLine("[green][[AppServer]][/] DotCraft AppServer started (stdio JSON-RPC 2.0)");

        try
        {
            await RunLoopAsync(transport, connection, handler, _wireAcpExtensionProxy, cancellationToken);
        }
        finally
        {
            _activeTransports.TryRemove(transport, out _);
        }
    }

    private async Task RunWebSocketOnlyAsync(
        WebSocketServerConfig wsConfig,
        ISessionService sessionService,
        ICommitMessageSuggestService commitMessageSuggest,
        CancellationToken cancellationToken)
    {
        var (wsApp, wsUrl) = BuildWebSocketApp(wsConfig, sessionService, commitMessageSuggest, cancellationToken, externalChannelRegistry);

        AnsiConsole.MarkupLine(
            $"[green][[AppServer]][/] DotCraft AppServer started (WebSocket at ws://{wsConfig.Host}:{wsConfig.Port}/ws)");

        // The WebSocket server IS the main loop — RunAsync blocks until shutdown.
        await wsApp.RunAsync(wsUrl);
    }

    private async Task RunStdioWithWebSocketAsync(
        WebSocketServerConfig wsConfig,
        ISessionService sessionService,
        ICommitMessageSuggestService commitMessageSuggest,
        CancellationToken cancellationToken)
    {
        // Build the WebSocket app and start it explicitly so that bind failures
        // surface immediately (fail-fast) instead of being deferred to finally.
        var (wsApp, wsUrl) = BuildWebSocketApp(wsConfig, sessionService, commitMessageSuggest, cancellationToken, externalChannelRegistry);
        wsApp.Urls.Add(wsUrl);
        await wsApp.StartAsync(cancellationToken);

        AnsiConsole.MarkupLine(
            $"[green][[AppServer]][/] WebSocket listener started at ws://{wsConfig.Host}:{wsConfig.Port}/ws");

        await using var transport = StdioTransport.CreateStdio();
        transport.Start();

        var connection = new AppServerConnection();
        _activeTransports.TryAdd(transport, connection);

        var handler = new AppServerRequestHandler(
            sessionService, connection, transport, CreateChannelListContributor(),
            serverVersion: AppVersion.Informational,
            cronService: _cronService, heartbeatService: _heartbeatService,
            skillsLoader: skillsLoader, workspaceCraftPath: paths.CraftPath,
            hostWorkspacePath: paths.WorkspacePath,
            automationsHandler: _automationsHandler,
            broadcastCronStateChanged: BroadcastCronStateChanged,
            commitMessageSuggest: commitMessageSuggest,
            dashboardUrl: _dashboardUrl,
            wireAcpExtensionProxy: _wireAcpExtensionProxy,
            channelStatusProvider: _channelRunner,
            mcpClientManager: mcpClientManager,
            broadcastMcpStatusChanged: BroadcastMcpStatusChanged,
            protocolExtensions: ProtocolExtensions,
            onExternalChannelUpserted: (channel, ct) =>
                _channelRunner?.ApplyExternalChannelUpsertAsync(channel, ct) ?? Task.CompletedTask,
            onExternalChannelRemoved: (channelName, ct) =>
                _channelRunner?.ApplyExternalChannelRemoveAsync(channelName, ct) ?? Task.CompletedTask,
            streamDebugLogger: sp.GetService<SessionStreamDebugLogger>(),
            configSchema: _configSchema,
            appConfigMonitor: _appConfigMonitor);

        AnsiConsole.MarkupLine("[green][[AppServer]][/] DotCraft AppServer started (stdio + WebSocket)");

        try
        {
            await RunLoopAsync(transport, connection, handler, _wireAcpExtensionProxy, cancellationToken);
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
        ICommitMessageSuggestService commitMessageSuggest,
        CancellationToken hostCt,
        ExternalChannelRegistry? channelRegistry = null)
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
                    sessionService, wsConnection, wsTransport, CreateChannelListContributor(),
                    serverVersion: AppVersion.Informational,
                    cronService: _cronService, heartbeatService: _heartbeatService,
                    skillsLoader: skillsLoader, workspaceCraftPath: paths.CraftPath,
                    hostWorkspacePath: paths.WorkspacePath,
                    automationsHandler: _automationsHandler,
                    broadcastCronStateChanged: BroadcastCronStateChanged,
                    commitMessageSuggest: commitMessageSuggest,
                    dashboardUrl: _dashboardUrl,
                    wireAcpExtensionProxy: _wireAcpExtensionProxy,
                    channelStatusProvider: _channelRunner,
                    mcpClientManager: mcpClientManager,
                    broadcastMcpStatusChanged: BroadcastMcpStatusChanged,
                    protocolExtensions: ProtocolExtensions,
                    onExternalChannelUpserted: (channel, ct) =>
                        _channelRunner?.ApplyExternalChannelUpsertAsync(channel, ct) ?? Task.CompletedTask,
                    onExternalChannelRemoved: (channelName, ct) =>
                        _channelRunner?.ApplyExternalChannelRemoveAsync(channelName, ct) ?? Task.CompletedTask,
                    streamDebugLogger: sp.GetService<SessionStreamDebugLogger>(),
                    configSchema: _configSchema,
                    appConfigMonitor: _appConfigMonitor);

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
                        await ProcessRequestAsync(wsTransport, wsHandler, wsConnection, firstMsg, hostCt);

                        // Check if this is a channel adapter connection
                        if (wsConnection.IsChannelAdapter)
                        {
                            var channelName = wsConnection.ChannelAdapterName!;

                            if (channelRegistry.TryGet(channelName, out var host) && host != null)
                            {
                                // Subprocess channels are registered for discovery but must not accept
                                // WebSocket adapter attach; only websocket-mode entries use /ws handover.
                                if (!host.AcceptsWebSocketAdapterAttach)
                                {
                                    AnsiConsole.MarkupLine(
                                        $"[yellow][[AppServer]][/] Rejected channel adapter '{channelName}': " +
                                        "channel uses subprocess transport; connect the adapter via stdio, not WebSocket.");

                                    await wsTransport.WriteMessageAsync(new
                                    {
                                        jsonrpc = "2.0",
                                        method = AppServerMethods.SystemEvent,
                                        @params = new
                                        {
                                            kind = "channelRejected",
                                            channelName,
                                            message =
                                                $"Channel '{channelName}' is subprocess-only; WebSocket adapter attach is not supported."
                                        }
                                    }, hostCt);

                                    return;
                                }

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
                        await RunLoopAsync(wsTransport, wsConnection, wsHandler, _wireAcpExtensionProxy, hostCt);
                        return;
                    }

                    // First message was not initialize — process normally and enter loop
                    if (firstMsg.IsNotification)
                    {
                        HandleNotification(firstMsg, wsHandler);
                    }
                    else if (firstMsg.IsRequest)
                    {
                        await ProcessRequestAsync(wsTransport, wsHandler, wsConnection, firstMsg, hostCt);
                    }
                }

                await RunLoopAsync(wsTransport, wsConnection, wsHandler, _wireAcpExtensionProxy, hostCt);
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
        WireAcpExtensionProxy? wireAcpProxy,
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
                    await ProcessRequestAsync(transport, handler, connection, msg, ct);
                }
                finally
                {
                    RequestGate.Release();
                }
            }, ct);
        }

        // Clean up active subscriptions when the client disconnects
        connection.CancelAllSubscriptions();
        wireAcpProxy?.UnbindTransport(transport);
    }

    private static async Task ProcessRequestAsync(
        IAppServerTransport transport,
        AppServerRequestHandler handler,
        AppServerConnection connection,
        AppServerIncomingMessage msg,
        CancellationToken ct)
    {
        var previousTransport = AppServerRequestContext.CurrentTransport;
        var previousConnection = AppServerRequestContext.CurrentConnection;
        AppServerRequestContext.CurrentTransport = transport;
        AppServerRequestContext.CurrentConnection = connection;
        try
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
        finally
        {
            AppServerRequestContext.CurrentTransport = previousTransport;
            AppServerRequestContext.CurrentConnection = previousConnection;
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
        mcpClientManager.StatusChanged -= OnMcpStatusChanged;
        _appConfigMonitor.Changed -= OnAppConfigChanged;
        if (_agentFactory != null)
            await _agentFactory.DisposeAsync();
    }

    private void OnAppConfigChanged(object? sender, AppConfigChangedEventArgs e)
    {
        _ = sender;
        BroadcastWorkspaceConfigChanged(e);
    }

    private void OnMcpStatusChanged(object? sender, McpServerStatusChangedEventArgs e)
    {
        BroadcastMcpStatusChanged(new McpStatusInfoWire
        {
            Name = e.Status.Name,
            Enabled = e.Status.Enabled,
            StartupState = e.Status.StartupState,
            ToolCount = e.Status.ToolCount,
            ResourceCount = e.Status.ResourceCount,
            ResourceTemplateCount = e.Status.ResourceTemplateCount,
            LastError = e.Status.LastError,
            Transport = e.Status.Transport
        });
    }

    /// <summary>
    /// Broadcasts a <c>system/jobResult</c> JSON-RPC notification to all connected transports.
    /// Called when a server-managed cron or heartbeat job completes and the job was created from
    /// a CLI (non-social-channel) context. See spec Section 6.9.
    /// </summary>
    private void BroadcastJobResult(
        string source,
        string? jobId,
        string? jobName,
        string? result,
        string? error,
        string? threadId = null,
        int? inputTokens = null,
        int? outputTokens = null)
    {
        object? tokenUsage = null;
        if (inputTokens.HasValue || outputTokens.HasValue)
        {
            tokenUsage = new
            {
                inputTokens = inputTokens ?? 0,
                outputTokens = outputTokens ?? 0
            };
        }

        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.SystemJobResult,
            @params = new
            {
                source,
                jobId,
                jobName,
                threadId,
                result,
                error,
                tokenUsage
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

    private void BroadcastCronStateChanged(CronJobWireInfo job, bool removed)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.CronStateChanged,
            @params = new { job, removed }
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.CronStateChanged))
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

    private void BroadcastMcpStatusChanged(McpStatusInfoWire server)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.McpStatusUpdated,
            @params = new { server }
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.McpStatusUpdated))
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

    private void BroadcastWorkspaceConfigChanged(AppConfigChangedEventArgs change)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.WorkspaceConfigChanged,
            @params = new WorkspaceConfigChangedParams
            {
                Source = change.Source,
                Regions = [.. change.Regions],
                ChangedAt = change.ChangedAt
            }
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.SupportsConfigChange || !connection.ShouldSendNotification(AppServerMethods.WorkspaceConfigChanged))
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
    /// Broadcasts <c>thread/started</c> to all connected transports when any channel creates a thread
    /// in the shared <see cref="SessionService"/> (so Desktop sidebar updates without polling).
    /// </summary>
    private void BroadcastThreadStarted(SessionThread thread)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.ThreadStarted,
            @params = new { thread = thread.ToWire() }
        };

        var skipTransport = AppServerRequestContext.CurrentTransport;

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.ThreadStarted))
                continue;

            if (skipTransport != null && ReferenceEquals(transport, skipTransport))
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
    /// Broadcasts <c>thread/renamed</c> to all connected transports when a thread's display name changes
    /// (Wire <c>thread/rename</c>, first-message title, or any <see cref="ISessionService.RenameThreadAsync"/> caller).
    /// </summary>
    private void BroadcastThreadRenamed(SessionThread thread)
    {
        if (string.IsNullOrEmpty(thread.DisplayName))
            return;

        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.ThreadRenamed,
            @params = new { threadId = thread.Id, displayName = thread.DisplayName }
        };

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.ThreadRenamed))
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
    /// Broadcasts <c>thread/deleted</c> to all connected transports after permanent thread removal
    /// (Wire <c>thread/delete</c>, DashBoard, etc.).
    /// </summary>
    private void BroadcastThreadDeleted(string threadId)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method = AppServerMethods.ThreadDeleted,
            @params = new { threadId }
        };

        var skipTransport = AppServerRequestContext.CurrentTransport;

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.ThreadDeleted))
                continue;

            if (skipTransport != null && ReferenceEquals(transport, skipTransport))
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
                content = plan.Content,
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

    /// <summary>
    /// Broadcasts an <c>automation/task/updated</c> JSON-RPC notification to all connected transports.
    /// Called by <see cref="AutomationsEventDispatcher"/> when a task status changes.
    /// </summary>
    private void BroadcastAutomationTaskUpdated(AutomationTask task)
    {
        var notification = AutomationsEventDispatcher.BuildNotification(task, paths.WorkspacePath);

        foreach (var (transport, connection) in _activeTransports)
        {
            if (!connection.ShouldSendNotification(AppServerMethods.AutomationTaskUpdated))
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

}
