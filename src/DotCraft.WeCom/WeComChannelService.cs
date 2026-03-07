using System.ClientModel;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.DashBoard;
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
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;

namespace DotCraft.WeCom;

/// <summary>
/// Gateway channel service for WeCom Bot. Manages the ASP.NET Core HTTP server,
/// channel adapter, and agent lifecycle as part of a multi-channel gateway.
/// </summary>
public sealed class WeComChannelService(
    IServiceProvider sp,
    AppConfig config,
    DotCraftPaths paths,
    SessionStore sessionStore,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    McpClientManager mcpClientManager,
    WeComBotRegistry registry,
    WeComPermissionService permissionService,
    WeComApprovalService wecomApprovalService,
    ModuleRegistry moduleRegistry)
    : IChannelService
{
    private WebApplication? _webApp;
    private WeComChannelAdapter? _adapter;

    public string Name => "wecom";

    /// <inheritdoc />
    public HeartbeatService? HeartbeatService { get; set; }

    /// <inheritdoc />
    public CronService? CronService { get; set; }

    /// <inheritdoc />
    public IApprovalService ApprovalService => wecomApprovalService;

    /// <inheritdoc />
    public object? ChannelClient => null;

    private AgentFactory BuildAgentFactory()
    {
        var cronTools = sp.GetService<CronTools>();
        var traceCollector = sp.GetService<TraceCollector>();
        var hookRunner = sp.GetService<HookRunner>();

        // Collect tool providers from modules
        var toolProviders = ToolProviderCollector.Collect(moduleRegistry, config);

        return new AgentFactory(
            paths.CraftPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader, wecomApprovalService, blacklist,
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
                ApprovalService = wecomApprovalService,
                PathBlacklist = blacklist,
                CronTools = cronTools,
                McpClientManager = mcpClientManager.Tools.Count > 0 ? mcpClientManager : null,
                TraceCollector = traceCollector
            },
            traceCollector: traceCollector,
            customCommandLoader: sp.GetService<CustomCommandLoader>(),
            onConsolidatorStatus: AnsiConsole.MarkupLine,
            hookRunner: hookRunner);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var agentFactory = BuildAgentFactory();
        var agent = agentFactory.CreateDefaultAgent();
        var traceCollector = sp.GetService<TraceCollector>();
        var tokenUsageStore = sp.GetService<TokenUsageStore>();

        var sessionGate = sp.GetRequiredService<SessionGate>();
        var customCommandLoader = sp.GetService<CustomCommandLoader>();
        _adapter = new WeComChannelAdapter(
            agent, sessionStore, registry,
            permissionService, wecomApprovalService, sessionGate,
            heartbeatService: HeartbeatService,
            cronService: CronService,
            agentFactory: agentFactory,
            traceCollector: traceCollector,
            tokenUsageStore: tokenUsageStore,
            customCommandLoader: customCommandLoader);

        var builder = WebApplication.CreateBuilder();
        _webApp = builder.Build();

        var logger = new WeComServerLogger();
        var server = new WeComBotServer(registry, logger: logger);
        server.MapRoutes(_webApp);

        var url = $"https://{config.WeComBot.Host}:{config.WeComBot.Port}";
        AnsiConsole.MarkupLine($"[green][[Gateway]][/] WeCom Bot listening on {Markup.Escape(url)}");
        foreach (var path in registry.GetAllPaths())
        {
            AnsiConsole.MarkupLine($"[grey]  - {Markup.Escape(url + path)}[/]");
        }

        _ = _webApp.RunAsync(url);

        // Wait for cancellation
        var tcs = new TaskCompletionSource();
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;

        await StopAsync();
    }

    public async Task StopAsync()
    {
        if (_webApp != null)
            await _webApp.StopAsync();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAdminTargets() => [""];

    public Task DeliverMessageAsync(string target, string content)
    {
        // Prefer the per-chat webhook URL cached at runtime from incoming messages.
        // Fall back to the global config webhook if the target chatId hasn't been seen yet.
        var webhookUrl = (!string.IsNullOrWhiteSpace(target) ? registry.GetWebhookUrl(target) : null)
            ?? config.WeCom.WebhookUrl;

        if (!string.IsNullOrWhiteSpace(webhookUrl))
        {
            var wecomTools = new WeComTools(webhookUrl);
            return wecomTools.SendTextAsync(content);
        }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_adapter != null)
            await _adapter.DisposeAsync();
        if (_webApp != null)
            await _webApp.DisposeAsync();
    }
}
