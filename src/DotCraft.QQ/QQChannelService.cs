using System.ClientModel;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.DashBoard;
using DotCraft.Diagnostics;
using DotCraft.Sessions;
using DotCraft.Heartbeat;
using DotCraft.Hooks;
using DotCraft.Hosting;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;

namespace DotCraft.QQ;

/// <summary>
/// Gateway channel service for QQ Bot. Manages the QQ WebSocket connection,
/// channel adapter, and agent lifecycle as part of a multi-channel gateway.
/// </summary>
public sealed class QQChannelService(
    IServiceProvider sp,
    AppConfig config,
    DotCraftPaths paths,
    SessionStore sessionStore,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    McpClientManager mcpClientManager,
    QQBotClient qqClient,
    QQPermissionService permissionService,
    QQApprovalService qqApprovalService,
    ModuleRegistry moduleRegistry)
    : IChannelService
{
    private QQChannelAdapter? _adapter;

    public string Name => "qq";

    /// <inheritdoc />
    public HeartbeatService? HeartbeatService { get; set; }

    /// <inheritdoc />
    public CronService? CronService { get; set; }

    /// <inheritdoc />
    public IApprovalService ApprovalService => qqApprovalService;

    /// <inheritdoc />
    public object ChannelClient => qqClient;

    private AgentFactory BuildAgentFactory()
    {
        var cronTools = sp.GetService<CronTools>();
        var traceCollector = sp.GetService<TraceCollector>();
        var hookRunner = sp.GetService<HookRunner>();

        // Collect tool providers from modules
        var toolProviders = ToolProviderCollector.Collect(moduleRegistry, config);

        var planStore = new PlanStore(paths.CraftPath);

        return new AgentFactory(
            paths.CraftPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader, qqApprovalService, blacklist,
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
                ApprovalService = qqApprovalService,
                PathBlacklist = blacklist,
                CronTools = cronTools,
                McpClientManager = mcpClientManager.Tools.Count > 0 ? mcpClientManager : null,
                TraceCollector = traceCollector,
                ChannelClient = qqClient
            },
            traceCollector: traceCollector,
            customCommandLoader: sp.GetService<CustomCommandLoader>(),
            onConsolidatorStatus: AnsiConsole.MarkupLine,
            planStore: planStore,
            onPlanUpdated: plan =>
            {
                if (!DebugModeService.IsEnabled()) return;
                var ctx = QQChatContextScope.Current;
                if (ctx == null) return;
                var text = PlanStore.RenderPlanPlainText(plan);
                if (ctx.IsGroupMessage)
                    _ = qqClient.SendGroupMessageAsync(ctx.GroupId, text);
                else
                    _ = qqClient.SendPrivateMessageAsync(ctx.UserId, text);
            },
            hookRunner: hookRunner);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var agentFactory = BuildAgentFactory();
        var agent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        var traceCollector = sp.GetService<TraceCollector>();
        var tokenUsageStore = sp.GetService<TokenUsageStore>();

        var sessionGate = sp.GetRequiredService<SessionGate>();
        var customCommandLoader = sp.GetService<CustomCommandLoader>();
        _adapter = new QQChannelAdapter(
            qqClient, agent, sessionStore,
            permissionService, sessionGate, qqApprovalService,
            heartbeatService: HeartbeatService,
            cronService: CronService,
            agentFactory: agentFactory,
            traceCollector: traceCollector,
            tokenUsageStore: tokenUsageStore,
            customCommandLoader: customCommandLoader);

        await qqClient.StartAsync(cancellationToken);

        AnsiConsole.MarkupLine(
            $"[green][[Gateway]][/] QQ Bot listening on ws://{config.QQBot.Host}:{config.QQBot.Port}/");

        var tcs = new TaskCompletionSource();
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;

        await StopAsync();
    }

    public async Task StopAsync()
    {
        await qqClient.StopAsync();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAdminTargets()
        => config.QQBot.AdminUsers.Select(id => id.ToString()).ToList();

    public async Task DeliverMessageAsync(string target, string content)
    {
        // target is either "group:<groupId>" for group messages or a plain user id for private
        if (target.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
        {
            var groupIdStr = target["group:".Length..];
            if (long.TryParse(groupIdStr, out var groupId))
            {
                await qqClient.SendGroupMessageAsync(groupId, content);
                return;
            }
        }

        if (long.TryParse(target, out var userId))
            await qqClient.SendPrivateMessageAsync(userId, content);
    }

    public async ValueTask DisposeAsync()
    {
        if (_adapter != null)
            await _adapter.DisposeAsync();
    }
}
