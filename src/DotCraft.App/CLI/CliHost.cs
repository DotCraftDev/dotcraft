using System.ClientModel;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.DashBoard;
using DotCraft.Sessions;
using DotCraft.Heartbeat;
using DotCraft.Hosting;
using DotCraft.Localization;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Hooks;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace DotCraft.CLI;

public sealed class CliHost(
    IServiceProvider sp,
    AppConfig config,
    DotCraftPaths paths,
    SessionStore sessionStore,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    CronService cronService,
    McpClientManager mcpClientManager,
    LanguageService languageService,
    ConsoleApprovalService cliApprovalService,
    ModuleRegistry moduleRegistry) : IDotCraftHost
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var cronTools = sp.GetService<CronTools>();
        var traceCollector = sp.GetService<TraceCollector>();
        var traceStore = sp.GetService<TraceStore>();
        var tokenUsageStore = sp.GetService<TokenUsageStore>();
        var hookRunner = sp.GetService<HookRunner>();

        // Scan for tool icons at startup
        ToolProviderCollector.ScanToolIcons(moduleRegistry, config);

        // Collect tool providers from modules
        var toolProviders = ToolProviderCollector.Collect(moduleRegistry, config);

        var planStore = new PlanStore(paths.CraftPath);

        var agentFactory = new AgentFactory(
            paths.CraftPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader, cliApprovalService, blacklist,
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
                ApprovalService = cliApprovalService,
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

        var modeManager = new AgentModeManager();
        var agent = agentFactory.CreateAgentForMode(AgentMode.Agent, modeManager);
        var sessionGate = sp.GetRequiredService<SessionGate>();
        var runner = new AgentRunner(agent, sessionStore, agentFactory, traceCollector, sessionGate, hookRunner);

        DashBoardServer? dashBoardServer = null;
        string? dashBoardUrl = null;
        if (config.DashBoard.Enabled && traceStore != null)
        {
            dashBoardServer = new DashBoardServer();
            dashBoardServer.Start(traceStore, config, paths, tokenUsageStore);
            dashBoardUrl = $"http://{config.DashBoard.Host}:{config.DashBoard.Port}/dashboard";
        }

        using var heartbeatService = new HeartbeatService(
            paths.CraftPath,
            onHeartbeat: runner.RunAsync,
            intervalSeconds: config.Heartbeat.IntervalSeconds,
            enabled: false);

        var customCommandLoader = sp.GetService<CustomCommandLoader>();
        var repl = new ReplHost(agent, sessionStore, skillsLoader,
            paths.WorkspacePath, paths.CraftPath, config,
            heartbeatService: heartbeatService, cronService: cronService,
            agentFactory: agentFactory, mcpClientManager: mcpClientManager,
            dashBoardUrl: dashBoardUrl,
            languageService: languageService, tokenUsageStore: tokenUsageStore,
            customCommandLoader: customCommandLoader,
            modeManager: modeManager,
            planStore: planStore,
            hookRunner: hookRunner);

        cronService.OnJob = async job =>
        {
            var sessionKey = $"cron:{job.Id}";
            await runner.RunAsync(job.Payload.Message, sessionKey);
            repl.ReprintPrompt();
        };

        if (config.Cron.Enabled)
            cronService.Start();

        await repl.RunAsync(cancellationToken);

        cronService.Stop();

        if (dashBoardServer != null)
            await dashBoardServer.DisposeAsync();
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
