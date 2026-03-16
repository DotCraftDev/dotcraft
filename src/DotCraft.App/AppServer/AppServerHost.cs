using System.ClientModel;
using System.Reflection;
using DotCraft.Agents;
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
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;

namespace DotCraft.AppServer;

/// <summary>
/// Host for AppServer mode.
/// Runs a single-connection stdio JSON-RPC 2.0 server that exposes
/// <see cref="ISessionService"/> over the Session Wire Protocol.
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
            traceCollector: traceCollector);

        var agent = _agentFactory.CreateAgentForMode(AgentMode.Agent);
        var sessionService = SessionServiceFactory.Create(_agentFactory, agent, sp);

        await using var transport = StdioTransport.CreateStdio();
        transport.Start();

        var connection = new AppServerConnection();
        var handler = new AppServerRequestHandler(
            sessionService, connection, transport, serverVersion: GetVersion());

        AnsiConsole.MarkupLine("[green][[AppServer]][/] DotCraft AppServer started (stdio JSON-RPC 2.0)");

        await RunLoopAsync(transport, connection, handler, cancellationToken);

        AnsiConsole.MarkupLine("[grey][[AppServer]][/] AppServer stopped");
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

    private static string GetVersion()
    {
        return typeof(AppServerHost).Assembly
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion
            ?? "0.1.0";
    }

    public async ValueTask DisposeAsync()
    {
        if (_agentFactory != null)
            await _agentFactory.DisposeAsync();
    }
}
