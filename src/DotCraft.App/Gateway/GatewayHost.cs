using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Api;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.DashBoard;
using DotCraft.Heartbeat;
using DotCraft.Hooks;
using DotCraft.Hosting;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DotCraft.Gateway;

/// <summary>
/// Hosts multiple channel services concurrently (QQ, WeCom, API) sharing
/// a single CronService, HeartbeatService, and DashBoardServer instance.
/// </summary>
public sealed class GatewayHost : IDotCraftHost
{
    private readonly IServiceProvider _sp;
    private readonly AppConfig _config;
    private readonly DotCraftPaths _paths;
    private readonly SessionStore _sessionStore;
    private readonly SkillsLoader _skillsLoader;
    private readonly CronService _cronService;
    private readonly IReadOnlyList<IChannelService> _channels;
    private readonly MessageRouter _router;
    private readonly ModuleRegistry _moduleRegistry;
    private AgentFactory? _sharedAgentFactory;

    public GatewayHost(
        IServiceProvider sp,
        AppConfig config,
        DotCraftPaths paths,
        SessionStore sessionStore,
        SkillsLoader skillsLoader,
        CronService cronService,
        IEnumerable<IChannelService> channels,
        MessageRouter router,
        ModuleRegistry moduleRegistry)
    {
        _sp = sp;
        _config = config;
        _paths = paths;
        _sessionStore = sessionStore;
        _skillsLoader = skillsLoader;
        _cronService = cronService;
        _channels = channels.ToList();
        _router = router;
        _moduleRegistry = moduleRegistry;

        foreach (var ch in _channels)
            _router.RegisterChannel(ch);
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Scan for tool icons at startup
        ToolProviderCollector.ScanToolIcons(_moduleRegistry, _config);

        var traceStore = _sp.GetService<TraceStore>();
        var tokenUsageStore = _sp.GetService<TokenUsageStore>();

        // Dashboard startup is owned by GatewayHost.
        // When an API channel is present the dashboard is mounted on its web app to avoid port conflicts.
        // When no API channel exists a standalone DashBoardServer is started instead.
        DashBoardServer? dashBoardServer = null;
        if (_config.DashBoard.Enabled && traceStore != null)
        {
            var apiChannel = _channels.OfType<ApiChannelService>().FirstOrDefault();
            if (apiChannel != null)
            {
                var capturedTraceStore = traceStore;
                var capturedTokenUsageStore = tokenUsageStore;
                apiChannel.OnConfigureApp = app =>
                {
                    app.MapDashBoardAuth(_config);
                    app.UseDashBoardAuth(_config);
                    app.MapDashBoard(capturedTraceStore, _config, _paths, capturedTokenUsageStore);
                };
                var dashboardUrl = $"http://{_config.Api.Host}:{_config.Api.Port}";
                AnsiConsole.MarkupLine(
                    $"[green]DashBoard started at[/] [link={dashboardUrl}/dashboard]{dashboardUrl}/dashboard[/]");
            }
            else
            {
                dashBoardServer = new DashBoardServer();
                dashBoardServer.Start(traceStore, _config, _paths, tokenUsageStore);
            }
        }

        // Build a shared agent runner for heartbeat/cron
        var sharedAgentRunner = BuildSharedAgentRunner();

        // Heartbeat service (shared, notifies all admin channels)
        using var heartbeatService = new HeartbeatService(
            _paths.CraftPath,
            onHeartbeat: sharedAgentRunner,
            intervalSeconds: _config.Heartbeat.IntervalSeconds,
            enabled: _config.Heartbeat.Enabled);

        if (_config.Heartbeat.NotifyAdmin)
        {
            heartbeatService.OnResult = async result =>
                await _router.BroadcastToAdminsAsync($"[Heartbeat] {result}");
        }

        // Cron service (shared, routes delivery via MessageRouter)
        _cronService.OnJob = async job =>
        {
            var sessionKey = $"cron:{job.Id}";
            var approvalContext = BuildApprovalContext(job.Payload);

            string? result;
            if (approvalContext != null)
            {
                using var _ = ApprovalContextScope.Set(approvalContext);
                result = await sharedAgentRunner(job.Payload.Message, sessionKey);
            }
            else
            {
                result = await sharedAgentRunner(job.Payload.Message, sessionKey);
            }

            if (job.Payload.Deliver && result != null)
            {
                var channel = job.Payload.Channel ?? "wecom";
                var target = job.Payload.To ?? job.Payload.CreatorId ?? "";
                try
                {
                    await _router.DeliverAsync(channel, target, result);
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"[Cron] Delivery failed: {ex.Message}");
                }
            }
        };

        // Inject shared services into channels so slash commands (/heartbeat, /cron) work
        foreach (var ch in _channels)
        {
            ch.HeartbeatService = heartbeatService;
            ch.CronService = _cronService;
        }

        if (_config.Heartbeat.Enabled)
        {
            heartbeatService.Start();
            AnsiConsole.MarkupLine(
                $"[green][[Gateway]][/] Heartbeat started (interval: {_config.Heartbeat.IntervalSeconds}s)");
        }

        if (_config.Cron.Enabled)
        {
            _cronService.Start();
            AnsiConsole.MarkupLine(
                $"[green][[Gateway]][/] Cron service started ({_cronService.ListJobs().Count} jobs)");
        }

        PrintStartupSummary();

        // Start all channels concurrently
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var channelTasks = _channels
            .Select(ch => RunChannelAsync(ch, cts.Token))
            .ToList();

        // Wait for Ctrl+C or external cancellation
        await WaitForShutdownSignalAsync(cancellationToken);

        AnsiConsole.MarkupLine("[yellow][[Gateway]] Shutting down...[/]");

        // Signal all channels to stop
        await cts.CancelAsync();

        // Wait for all channel tasks to complete (they stop gracefully on cancellation)
        await Task.WhenAll(channelTasks);

        heartbeatService.Stop();
        _cronService.Stop();

        if (dashBoardServer != null)
            await dashBoardServer.DisposeAsync();

        AnsiConsole.MarkupLine("[grey][[Gateway]] All channels stopped.[/]");
    }

    private Func<string, string, Task<string?>> BuildSharedAgentRunner()
    {
        var memoryStore = _sp.GetRequiredService<MemoryStore>();
        var pathBlacklist = _sp.GetRequiredService<PathBlacklist>();
        var mcpClientManager = _sp.GetRequiredService<DotCraft.Mcp.McpClientManager>();
        var cronTools = _sp.GetService<CronTools>();
        var traceCollector = _sp.GetService<TraceCollector>();
        var hookRunner = _sp.GetService<HookRunner>();

        // Build a routing approval service that delegates to the originating channel's
        // approval service based on ApprovalContext.Source, with Console as fallback.
        var channelServiceMap = _channels
            .Where(ch => ch.ApprovalService != null)
            .ToDictionary(
                ch => ch.Name switch
                {
                    "qq"    => ApprovalSource.QQ,
                    "wecom" => ApprovalSource.WeCom,
                    "api"   => ApprovalSource.Api,
                    _       => ApprovalSource.Console
                },
                ch => ch.ApprovalService!);
        var approvalService = new DotCraft.Security.ChannelRoutingApprovalService(
            channelServiceMap,
            fallback: new DotCraft.Security.ConsoleApprovalService());

        // Collect tool providers from modules
        var toolProviders = ToolProviderCollector.Collect(_moduleRegistry, _config);

        // Prefer the QQ channel client so channel-specific tools (voice, file) are available in cron/heartbeat
        var channelClient = _channels.FirstOrDefault(ch => ch.ChannelClient != null)?.ChannelClient;

        _sharedAgentFactory = new AgentFactory(
            _paths.CraftPath, _paths.WorkspacePath, _config,
            memoryStore, _skillsLoader, approvalService, pathBlacklist,
            toolProviders: toolProviders,
            toolProviderContext: new ToolProviderContext
            {
                Config = _config,
                ChatClient = new OpenAI.OpenAIClient(
                    new System.ClientModel.ApiKeyCredential(_config.ApiKey),
                    new OpenAI.OpenAIClientOptions { Endpoint = new Uri(_config.EndPoint) })
                    .GetChatClient(_config.Model),
                WorkspacePath = _paths.WorkspacePath,
                BotPath = _paths.CraftPath,
                MemoryStore = memoryStore,
                SkillsLoader = _skillsLoader,
                ApprovalService = approvalService,
                PathBlacklist = pathBlacklist,
                CronTools = cronTools,
                McpClientManager = mcpClientManager.Tools.Count > 0 ? mcpClientManager : null,
                TraceCollector = traceCollector,
                ChannelClient = channelClient
            },
            traceCollector: traceCollector,
            customCommandLoader: _sp.GetService<CustomCommandLoader>(),
            onConsolidatorStatus: AnsiConsole.MarkupLine,
            hookRunner: hookRunner);

        var agent = _sharedAgentFactory.CreateDefaultAgent();
        var sessionGate = _sp.GetRequiredService<SessionGate>();
        var runner = new AgentRunner(agent, _sessionStore, _sharedAgentFactory, traceCollector, sessionGate, hookRunner);
        return runner.RunAsync;
    }

    private static async Task RunChannelAsync(IChannelService channel, CancellationToken ct)
    {
        try
        {
            await channel.StartAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine(
                $"[grey][[Gateway]][/] [red]Channel '{Markup.Escape(channel.Name)}' failed: {Markup.Escape(ex.Message)}[/]");
        }
    }

    private void PrintStartupSummary()
    {
        AnsiConsole.MarkupLine("[green][[Gateway]][/] Starting with channels:");
        foreach (var ch in _channels)
        {
            AnsiConsole.MarkupLine($"[grey]  - {ch.Name}[/]");
        }
        AnsiConsole.MarkupLine("[grey]Press Ctrl+C to stop...[/]");
    }

    private static async Task WaitForShutdownSignalAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            tcs.TrySetResult();
        };
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;
    }

    private static ApprovalContext? BuildApprovalContext(CronPayload payload)
    {
        if (string.IsNullOrEmpty(payload.CreatorSource) || string.IsNullOrEmpty(payload.CreatorId))
            return null;

        var source = payload.CreatorSource switch
        {
            "qq"    => ApprovalSource.QQ,
            "wecom" => ApprovalSource.WeCom,
            "api"   => ApprovalSource.Api,
            _       => ApprovalSource.Console
        };
        var groupId = source == ApprovalSource.QQ
            && long.TryParse(payload.CreatorGroupId, out var gid) ? gid : 0L;
        return new ApprovalContext { UserId = payload.CreatorId, Source = source, GroupId = groupId };
    }

    public async ValueTask DisposeAsync()
    {
        // Dispose channels
        foreach (var ch in _channels)
            await ch.DisposeAsync();
        
        // Dispose shared agent factory
        if (_sharedAgentFactory != null)
            await _sharedAgentFactory.DisposeAsync();
    }
}
