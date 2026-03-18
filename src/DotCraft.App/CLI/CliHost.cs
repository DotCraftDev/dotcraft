using System.ClientModel;
using System.Text.Json;
using DotCraft.Abstractions;
using DotCraft.Common;
using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.DashBoard;
using DotCraft.Tracing;
using DotCraft.Heartbeat;
using DotCraft.Hosting;
using DotCraft.Localization;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Hooks;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;

namespace DotCraft.CLI;

public sealed class CliHost(
    IServiceProvider sp,
    AppConfig config,
    DotCraftPaths paths,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    CronService cronService,
    McpClientManager mcpClientManager,
    LanguageService languageService,
    ConsoleApprovalService cliApprovalService,
    ModuleRegistry moduleRegistry) : IDotCraftHost
{
    private AgentFactory? _agentFactory;
    private AppServerProcess? _appServerProcess;
    private WebSocketClientConnection? _wsConnection;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var cronTools = sp.GetService<CronTools>();
        var traceCollector = sp.GetService<TraceCollector>();
        var traceStore = sp.GetService<TraceStore>();
        var tokenUsageStore = sp.GetService<TokenUsageStore>();
        var hookRunner = sp.GetService<HookRunner>();
        var cliConfig = config.GetSection<CliConfig>("CLI");

        // Scan tool icons for CLI-side rendering (needed in both in-process and wire mode)
        ToolProviderCollector.ScanToolIcons(moduleRegistry, config);

        ICliSession cliSession;
        CliBackendInfo backendInfo;
        AgentRunner? runner = null;
        AgentModeManager? modeManager = null;
        ISessionService? sessionServiceForDashboard = null;

        if (cliConfig.InProcess)
        {
            // -------------------------------------------------------------------
            // In-process mode: build the agent stack in the same process
            // -------------------------------------------------------------------
            var toolProviders = ToolProviderCollector.Collect(moduleRegistry, config);

            var planStore = new PlanStore(paths.CraftPath);
            var scopedApproval = new SessionScopedApprovalService(cliApprovalService);

            _agentFactory = new AgentFactory(
                paths.CraftPath, paths.WorkspacePath, config,
                memoryStore, skillsLoader, scopedApproval, blacklist,
                toolProviders: toolProviders,
                toolProviderContext: new ToolProviderContext
                {
                    Config = config,
                    ChatClient = new OpenAIClient(new ApiKeyCredential(config.ApiKey), new OpenAIClientOptions
                    {
                        Endpoint = new Uri(config.EndPoint)
                    }).GetChatClient(config.Model),
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
                customCommandLoader: sp.GetService<CustomCommandLoader>(),
                planStore: planStore,
                onPlanUpdated: StatusPanel.ShowPlanStatus,
                hookRunner: hookRunner);

            modeManager = new AgentModeManager();
            var agent = _agentFactory.CreateAgentForMode(AgentMode.Agent, modeManager);
            var sessionService = SessionServiceFactory.Create(_agentFactory, agent, sp);
            sessionServiceForDashboard = sessionService;

            runner = new AgentRunner(paths.WorkspacePath, sessionService);
            cliSession = new InProcessCliSession(sessionService, _agentFactory, tokenUsageStore, hookRunner);
            backendInfo = new CliBackendInfo { IsWire = false };
        }
        else if (!string.IsNullOrWhiteSpace(cliConfig.AppServerUrl))
        {
            // -------------------------------------------------------------------
            // WebSocket mode: connect to an already-running AppServer via WebSocket
            // -------------------------------------------------------------------
            var wsUri = new Uri(cliConfig.AppServerUrl);
            AnsiConsole.MarkupLine($"[grey][[CLI]][/] Connecting to AppServer at {wsUri}...");

            _wsConnection = await WebSocketClientConnection.ConnectAsync(
                wsUri,
                cliConfig.AppServerToken,
                cancellationToken);

            // Perform the mandatory JSON-RPC initialize/initialized handshake,
            // mirroring AppServerProcess.StartAsync which does the same for subprocess mode.
            var wsInitResponse = await _wsConnection.Wire.InitializeAsync(
                clientName: "dotcraft-cli",
                clientVersion: AppVersion.Informational,
                approvalSupport: true,
                streamingSupport: true);

            cliSession = new WireCliSession(_wsConnection.Wire, tokenUsageStore);
            backendInfo = new CliBackendInfo
            {
                IsWire = true,
                ServerVersion = TryGetServerVersion(wsInitResponse),
                ServerUrl = wsUri.ToString()
            };
        }
        else
        {
            // -------------------------------------------------------------------
            // Wire mode (default): spawn dotcraft app-server as a subprocess
            // -------------------------------------------------------------------
            AnsiConsole.MarkupLine("[grey][[CLI]][/] Starting AppServer subprocess...");

            var dotcraftBin = cliConfig.AppServerBin;
            _appServerProcess = await AppServerProcess.StartAsync(
                dotcraftBin: dotcraftBin,
                workspacePath: paths.WorkspacePath,
                ct: cancellationToken);

            _appServerProcess.OnCrashed += () =>
                AnsiConsole.MarkupLine("[red][[CLI]][/] AppServer subprocess exited unexpectedly.");

            cliSession = new WireCliSession(_appServerProcess.Wire, tokenUsageStore);
            backendInfo = new CliBackendInfo
            {
                IsWire = true,
                ServerVersion = _appServerProcess.ServerVersion,
                ProcessId = _appServerProcess.ProcessId
            };
        }

        DashBoardServer? dashBoardServer = null;
        string? dashBoardUrl = null;
        if (config.DashBoard.Enabled && traceStore != null)
        {
            // Dashboard is only fully functional in in-process mode (requires ISessionService).
            // In wire mode it still starts but without session integration.
            dashBoardServer = new DashBoardServer();
            dashBoardServer.Start(traceStore, config, paths, tokenUsageStore,
                configTypes: ConfigSchemaRegistrations.GetAllConfigTypes(),
                sessionService: sessionServiceForDashboard);
            dashBoardUrl = $"http://{config.DashBoard.Host}:{config.DashBoard.Port}/dashboard";
        }

        AgentRunSessionDelegate heartbeatDelegate = runner != null
            ? runner.RunAsync
            : static (_, _, _) => Task.FromResult<string?>(null);
        using var heartbeatService = new HeartbeatService(
            paths.CraftPath,
            onHeartbeat: heartbeatDelegate,
            intervalSeconds: config.Heartbeat.IntervalSeconds,
            enabled: false);

        var customCommandLoader = sp.GetService<CustomCommandLoader>();
        var repl = new ReplHost(skillsLoader, cliSession,
            paths.WorkspacePath, paths.CraftPath,
            heartbeatService: heartbeatService, cronService: cronService,
            agentFactory: _agentFactory, mcpClientManager: mcpClientManager,
            dashBoardUrl: dashBoardUrl,
            languageService: languageService,
            customCommandLoader: customCommandLoader,
            modeManager: modeManager,
            hookRunner: hookRunner,
            backendInfo: backendInfo);

        if (runner != null)
        {
            cronService.OnJob = async job =>
            {
                var sessionKey = $"cron:{job.Id}";
                await runner.RunAsync(job.Payload.Message, sessionKey);
                repl.ReprintPrompt();
            };
        }

        if (config.Cron.Enabled)
            cronService.Start();

        await repl.RunAsync(cancellationToken);

        cronService.Stop();

        if (dashBoardServer != null)
            await dashBoardServer.DisposeAsync();

        await cliSession.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_agentFactory != null)
            await _agentFactory.DisposeAsync();

        if (_appServerProcess != null)
            await _appServerProcess.DisposeAsync();

        if (_wsConnection != null)
            await _wsConnection.DisposeAsync();
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
        catch
        {
            return null;
        }
    }
}
