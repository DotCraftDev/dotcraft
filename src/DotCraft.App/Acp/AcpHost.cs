using System.ClientModel;
using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Tracing;
using DotCraft.Hosting;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Sessions.Protocol;
using DotCraft.Hooks;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;

namespace DotCraft.Acp;

/// <summary>
/// Host for ACP (Agent Client Protocol) mode.
/// Communicates with the editor/IDE over stdio using JSON-RPC.
/// </summary>
public sealed class AcpHost(
    IServiceProvider sp,
    AppConfig config,
    DotCraftPaths paths,
    SessionStore sessionStore,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    McpClientManager mcpClientManager,
    ModuleRegistry moduleRegistry) : IDotCraftHost
{
    private AgentFactory? _agentFactory;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var cronTools = sp.GetService<CronTools>();
        var traceCollector = sp.GetService<TraceCollector>();
        var tokenUsageStore = sp.GetService<TokenUsageStore>();
        var customCommandLoader = sp.GetService<CustomCommandLoader>();
        var hookRunner = sp.GetService<HookRunner>();

        ToolProviderCollector.ScanToolIcons(moduleRegistry, config);
        var toolProviders = ToolProviderCollector.Collect(moduleRegistry, config);

        using var acpLogger = AcpLogger.Create(paths.CraftPath, config.DebugMode);

        await using var transport = AcpTransport.CreateStdio();
        transport.Logger = acpLogger;
        transport.StartReaderLoop();
        var acpApprovalService = new AcpApprovalService(transport);

        // Wrap in SessionScopedApprovalService so SessionService can install per-turn overrides
        var scopedApproval = new SessionScopedApprovalService(acpApprovalService);

        // Create client proxy early (capabilities will be set during initialize)
        var clientProxy = new AcpClientProxy(transport, null);

        var planStore = new PlanStore(paths.CraftPath);

        AcpHandler? handler = null;

        _agentFactory = new AgentFactory(
            paths.CraftPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader, scopedApproval, blacklist,
            toolProviders: toolProviders,
            toolProviderContext: new Abstractions.ToolProviderContext
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
                TraceCollector = traceCollector,
                AcpExtensionProxy = clientProxy
            },
            traceCollector: traceCollector,
            customCommandLoader: customCommandLoader,
            planStore: planStore,
            onPlanUpdated: plan =>
            {
                var sessionId = TracingChatClient.CurrentSessionKey;
                if (handler != null && !string.IsNullOrEmpty(sessionId))
                    handler.SendPlanUpdate(sessionId, plan);
            },
            onConsolidatorStatus: AnsiConsole.MarkupLine,
            hookRunner: hookRunner);

        var agent = _agentFactory.CreateAgentForMode(AgentMode.Agent);
        var threadStore = sp.GetRequiredService<ThreadStore>();
        var sessionGate = sp.GetRequiredService<SessionGate>();
        var sessionService = new SessionService(
            _agentFactory, agent, threadStore, sessionGate,
            hookRunner, traceCollector);
        handler = new AcpHandler(
            transport, sessionStore, _agentFactory, agent,
            acpApprovalService, paths.WorkspacePath,
            customCommandLoader, traceCollector,
            tokenUsageStore: tokenUsageStore,
            logger: acpLogger,
            planStore: planStore,
            clientProxy: clientProxy,
            hookRunner: hookRunner,
            sessionService: sessionService);

        AnsiConsole.MarkupLine("[green][[ACP]][/] DotCraft ACP agent started (stdio)");
        await handler.RunAsync(cancellationToken);
        AnsiConsole.MarkupLine("[grey][[ACP]][/] ACP agent stopped");
    }

    public async ValueTask DisposeAsync()
    {
        if (_agentFactory != null)
            await _agentFactory.DisposeAsync();
    }
}
