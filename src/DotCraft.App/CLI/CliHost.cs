using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.DashBoard;
using DotCraft.Tracing;
using DotCraft.Heartbeat;
using DotCraft.Hooks;
using DotCraft.Hosting;
using DotCraft.Mcp;
using DotCraft.Modules;
using DotCraft.Protocol.AppServer;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DotCraft.CLI;

public sealed class CliHost(
    IServiceProvider sp,
    AppConfig config,
    DotCraftPaths paths,
    SkillsLoader skillsLoader,
    CronService cronService,
    McpClientManager mcpClientManager,
    ModuleRegistry moduleRegistry) : IDotCraftHost
{
    private AppServerProcess? _appServerProcess;
    private WebSocketClientConnection? _wsConnection;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var traceStore = sp.GetService<TraceStore>();
        var tokenUsageStore = sp.GetService<TokenUsageStore>();
        var hookRunner = sp.GetService<HookRunner>();
        var cliConfig = config.GetSection<CliConfig>("CLI");

        ToolProviderCollector.ScanToolIcons(moduleRegistry, config);

        ICliSession cliSession;
        CliBackendInfo backendInfo;
        WireAgentRunner? wireRunner;

        if (!string.IsNullOrWhiteSpace(cliConfig.AppServerUrl))
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

            var wsInitResponse = await _wsConnection.Wire.InitializeAsync(
                clientName: "dotcraft-cli",
                clientVersion: AppVersion.Informational,
                approvalSupport: true,
                streamingSupport: true);

            cliSession = new WireCliSession(_wsConnection.Wire, tokenUsageStore);
            wireRunner = new WireAgentRunner(_wsConnection.Wire, paths.WorkspacePath);
            backendInfo = new CliBackendInfo
            {
                ServerVersion = TryGetServerVersion(wsInitResponse),
                ServerUrl = wsUri.ToString()
            };
        }
        else
        {
            // -------------------------------------------------------------------
            // Subprocess mode (default): spawn dotcraft app-server as a subprocess
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
            wireRunner = new WireAgentRunner(_appServerProcess.Wire, paths.WorkspacePath);
            backendInfo = new CliBackendInfo
            {
                ServerVersion = _appServerProcess.ServerVersion,
                ProcessId = _appServerProcess.ProcessId
            };
        }

        DashBoardServer? dashBoardServer = null;
        string? dashBoardUrl = null;
        if (config.DashBoard.Enabled && traceStore != null)
        {
            dashBoardServer = new DashBoardServer();
            dashBoardServer.Start(traceStore, config, paths, tokenUsageStore,
                configTypes: ConfigSchemaRegistrations.GetAllConfigTypes(),
                deleteThread: id => cliSession.DeleteThreadAsync(id));
            dashBoardUrl = $"http://{config.DashBoard.Host}:{config.DashBoard.Port}/dashboard";
        }

        AgentRunSessionDelegate heartbeatDelegate = wireRunner.RunAsync;
        using var heartbeatService = new HeartbeatService(
            paths.CraftPath,
            onHeartbeat: heartbeatDelegate,
            intervalSeconds: config.Heartbeat.IntervalSeconds,
            enabled: false);

        var customCommandLoader = sp.GetService<CustomCommandLoader>();
        var repl = new ReplHost(skillsLoader, cliSession,
            paths.WorkspacePath, paths.CraftPath,
            heartbeatService: heartbeatService, cronService: cronService,
            mcpClientManager: mcpClientManager,
            dashBoardUrl: dashBoardUrl,
            customCommandLoader: customCommandLoader,
            hookRunner: hookRunner,
            backendInfo: backendInfo);

        cronService.OnJob = async job =>
        {
            var sessionKey = $"cron:{job.Id}";
            await wireRunner.RunAsync(job.Payload.Message, sessionKey);
            repl.ReprintPrompt();
        };

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
