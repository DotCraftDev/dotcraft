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
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;

namespace DotCraft.WeCom;

/// <summary>
/// Gateway channel service for WeCom Bot. Manages the ASP.NET Core HTTP server,
/// channel adapter, and agent lifecycle as part of a multi-channel gateway.
/// Implements <see cref="IWebHostingChannel"/> so <see cref="WebHostPool"/> can merge it with
/// other services that share the same address (scheme + host + port).
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
    : IChannelService, IWebHostingChannel
{
    private WebApplication? _webApp;
    private WeComChannelAdapter? _adapter;
    private AgentFactory? _agentFactory;

    public string Name => "wecom";

    /// <inheritdoc />
    public HeartbeatService? HeartbeatService { get; set; }

    /// <inheritdoc />
    public CronService? CronService { get; set; }

    /// <inheritdoc />
    public IApprovalService ApprovalService => wecomApprovalService;

    /// <inheritdoc />
    public object? ChannelClient => null;

    #region IWebHostingChannel

    /// <inheritdoc />
    // WeCom requires HTTPS; this prevents accidental merging with HTTP-only services.
    public string ListenScheme => "https";

    /// <inheritdoc />
    public string ListenHost => string.IsNullOrWhiteSpace(config.WeComBot.Host) ? "0.0.0.0" : config.WeComBot.Host;

    /// <inheritdoc />
    public int ListenPort => config.WeComBot.Port <= 0 ? 9000 : config.WeComBot.Port;

    /// <inheritdoc />
    public void ConfigureBuilder(WebApplicationBuilder builder)
    {
        // WeCom Bot server has no additional DI registrations needed on the builder.
        // Agent factory and adapter are initialised in ConfigureApp where the full
        // service provider is available.
    }

    /// <inheritdoc />
    public void ConfigureApp(WebApplication app)
    {
        _webApp = app;

        _agentFactory = BuildAgentFactory();
        var agent = _agentFactory.CreateAgentForMode(AgentMode.Agent);
        var traceCollector = sp.GetService<TraceCollector>();
        var tokenUsageStore = sp.GetService<TokenUsageStore>();

        var sessionGate = sp.GetRequiredService<SessionGate>();
        var activeRunRegistry = sp.GetRequiredService<ActiveRunRegistry>();
        var customCommandLoader = sp.GetService<CustomCommandLoader>();
        var httpClient = new HttpClient(new SocketsHttpHandler
        {
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestHeaders = { { "User-Agent", "DotCraft/1.0" } }
        };
        _adapter = new WeComChannelAdapter(
            agent, sessionStore, registry,
            permissionService, wecomApprovalService, sessionGate, activeRunRegistry,
            heartbeatService: HeartbeatService,
            cronService: CronService,
            agentFactory: _agentFactory,
            traceCollector: traceCollector,
            tokenUsageStore: tokenUsageStore,
            customCommandLoader: customCommandLoader,
            httpClient: httpClient);

        var logger = new WeComServerLogger();
        var server = new WeComBotServer(registry, httpClient: httpClient, logger: logger);
        server.MapRoutes(app);

        var url = $"https://{ListenHost}:{ListenPort}";
        AnsiConsole.MarkupLine($"[green][[Gateway]][/] WeCom Bot listening on {Markup.Escape(url)}");
        foreach (var path in registry.GetAllPaths())
        {
            AnsiConsole.MarkupLine($"[grey]  - {Markup.Escape(url + path)}[/]");
        }
    }

    #endregion

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
            planStore: planStore,
            onPlanUpdated: plan =>
            {
                if (!DebugModeService.IsEnabled()) return;
                var pusher = WeComPusherScope.Current;
                if (pusher == null) return;
                var md = PlanStore.RenderPlanMarkdown(plan);
                _ = pusher.PushMarkdownAsync(md);
            },
            hookRunner: hookRunner);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Web server lifecycle is managed by WebHostPool in GatewayHost.
        // This task just holds open until the cancellation token fires.
        var tcs = new TaskCompletionSource();
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;
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
        if (_agentFactory != null)
            await _agentFactory.DisposeAsync();
    }
}
