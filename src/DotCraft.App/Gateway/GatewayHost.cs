using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.DashBoard;
using DotCraft.Tracing;
using DotCraft.Heartbeat;
using DotCraft.Hooks;
using DotCraft.Hosting;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DotCraft.Gateway;

/// <summary>
/// Hosts multiple channel services concurrently sharing a single CronService, HeartbeatService, and DashBoardServer instance.
/// </summary>
public sealed class GatewayHost : IDotCraftHost
{
    private readonly IServiceProvider _sp;
    private readonly AppConfig _config;
    private readonly DotCraftPaths _paths;
    private readonly SkillsLoader _skillsLoader;
    private readonly CronService _cronService;
    private readonly IReadOnlyList<IChannelService> _channels;
    private readonly List<IChannelService> _allChannels; // native + external, populated during RunAsync
    private readonly MessageRouter _router;
    private readonly ModuleRegistry _moduleRegistry;
    private readonly ExternalChannel.ExternalChannelRegistry _externalChannelRegistry;
    private AgentFactory? _sharedAgentFactory;
    private ISessionService? _sharedSessionService;

    public GatewayHost(
        IServiceProvider sp,
        AppConfig config,
        DotCraftPaths paths,
        SkillsLoader skillsLoader,
        CronService cronService,
        IEnumerable<IChannelService> channels,
        MessageRouter router,
        ModuleRegistry moduleRegistry,
        ExternalChannel.ExternalChannelRegistry externalChannelRegistry)
    {
        _sp = sp;
        _config = config;
        _paths = paths;
        _skillsLoader = skillsLoader;
        _cronService = cronService;
        _channels = channels.ToList();
        _allChannels = new List<IChannelService>(_channels);
        _router = router;
        _moduleRegistry = moduleRegistry;
        _externalChannelRegistry = externalChannelRegistry;

        foreach (var ch in _channels)
        {
            _router.RegisterChannel(ch);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        // Scan for tool icons at startup
        ToolProviderCollector.ScanToolIcons(_moduleRegistry, _config);

        var traceStore = _sp.GetService<TraceStore>();
        var tokenUsageStore = _sp.GetService<TokenUsageStore>();
        var orchestratorProviders = _sp.GetServices<IOrchestratorSnapshotProvider>().ToList();

        // --- Phase 1: Register all web-hosting channels with the pool ---
        // Each IWebHostingChannel gets a shared WebApplicationBuilder keyed by (scheme, host, port).
        // Channels that share the same address will share one Kestrel server automatically.
        await using var pool = new WebHostPool();

        foreach (var wc in _channels.OfType<IWebHostingChannel>())
            pool.Register(wc);

        // Register dashboard into the pool (may share an app with an existing channel).
        var dashboardEnabled = _config.DashBoard.Enabled && traceStore != null;
        if (dashboardEnabled)
        {
            var dashHost = _config.DashBoard.Host;
            var dashPort = _config.DashBoard.Port;

            // If no channel is already bound to the dashboard address, suppress Kestrel
            // request logging (mirrors the original DashBoardServer behaviour).
            // When sharing with a channel we leave the builder's logging untouched.
            var dashStandalone = !_channels.OfType<IWebHostingChannel>()
                .Any(wc => wc.ListenScheme == "http" &&
                           wc.ListenHost == dashHost &&
                           wc.ListenPort == dashPort);

            var dashBuilder = pool.GetOrCreateBuilder("http", dashHost, dashPort);
            if (dashStandalone)
                dashBuilder.Logging.ClearProviders();
        }

        // --- Phase 2: Build all WebApplication instances ---
        pool.BuildAll();

        // --- Phase 3: Build shared agent runner (required before HeartbeatService) ---
        var sharedAgentRunner = BuildSharedAgentRunner();

        // --- Phase 3.5: Create external channel hosts (requires SessionService from Phase 3) ---
        ExternalChannel.ExternalChannelManager? externalChannelManager = null;
        if (ExternalChannel.ExternalChannelManager.HasEnabledChannels(_config) && _sharedSessionService != null)
        {
            var nativeChannelNames = _channels.Select(ch => ch.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            externalChannelManager = new ExternalChannel.ExternalChannelManager(
                _config, _sharedSessionService, nativeChannelNames, _externalChannelRegistry);

            foreach (var extCh in externalChannelManager.Channels)
            {
                _allChannels.Add(extCh);
                _router.RegisterChannel(extCh);
            }
        }

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
                result = await sharedAgentRunner(job.Payload.Message, sessionKey, cancellationToken);
            }
            else
            {
                result = await sharedAgentRunner(job.Payload.Message, sessionKey, cancellationToken);
            }

            if (job.Payload.Deliver && result != null)
            {
                var channel = job.Payload.Channel ?? "unknown";
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

        // Inject shared services into channels BEFORE ConfigureApps so channel adapters
        // (e.g. WeComChannelAdapter) can reference them at construction time.
        foreach (var ch in _allChannels)
        {
            ch.HeartbeatService = heartbeatService;
            ch.CronService = _cronService;
        }

        // --- Phase 4: Configure apps (map middleware and routes on each WebApplication) ---
        pool.ConfigureApps();

        // Mount dashboard routes onto its resolved WebApplication.
        if (dashboardEnabled)
        {
            var capturedOrchestrators = orchestratorProviders.Count > 0 ? orchestratorProviders : null;
            var dashApp = pool.GetApp("http", _config.DashBoard.Host, _config.DashBoard.Port);
            dashApp.MapDashBoardAuth(_config);
            dashApp.UseDashBoardAuth(_config);
            var capturedSvc = _sharedSessionService;
            dashApp.MapDashBoard(traceStore!, _paths, tokenUsageStore,
                orchestratorProviders: capturedOrchestrators,
                configTypes: ConfigSchemaRegistrations.GetAllConfigTypes(),
                sessionHandler: capturedSvc != null
                    ? new DelegateDashBoardSessionHandler(id => capturedSvc.DeleteThreadPermanentlyAsync(id))
                    : null);

            var dashboardUrl = $"http://{_config.DashBoard.Host}:{_config.DashBoard.Port}";
            AnsiConsole.MarkupLine(
                $"[green]DashBoard started at[/] [link={dashboardUrl}/dashboard]{dashboardUrl}/dashboard[/]");
        }

        // --- Phase 5: Start all web servers ---
        await pool.StartAllAsync();

        if (_config.Heartbeat.Enabled)
        {
            heartbeatService.Start();
            AnsiConsole.MarkupLine($"[green][[Gateway]][/] Heartbeat started (interval: {_config.Heartbeat.IntervalSeconds}s)");
        }

        if (_config.Cron.Enabled)
        {
            _cronService.Start();
            AnsiConsole.MarkupLine($"[green][[Gateway]][/] Cron service started ({_cronService.ListJobs().Count} jobs)");
        }

        PrintStartupSummary();

        // --- Phase 6: Start all channel tasks concurrently ---
        // Web-hosting channels now just wait for cancellation; non-web channels run their own loops.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var channelTasks = _allChannels
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

        // Web servers are stopped by the pool's DisposeAsync (triggered by 'await using').
        AnsiConsole.MarkupLine("[grey][[Gateway]] All channels stopped.[/]");
    }

    private AgentRunSessionDelegate BuildSharedAgentRunner()
    {
        var memoryStore = _sp.GetRequiredService<MemoryStore>();
        var pathBlacklist = _sp.GetRequiredService<PathBlacklist>();
        var mcpClientManager = _sp.GetRequiredService<Mcp.McpClientManager>();
        var cronTools = _sp.GetService<CronTools>();
        var traceCollector = _sp.GetService<TraceCollector>();
        var hookRunner = _sp.GetService<HookRunner>();

        // Build a routing approval service that delegates to the originating channel's
        // approval service based on ApprovalContext.Source (channel name), with Console as fallback.
        var channelServiceMap = _channels
            .Where(ch => ch.ApprovalService != null)
            .ToDictionary(ch => ch.Name, ch => ch.ApprovalService!);
        var approvalService = new ChannelRoutingApprovalService(
            channelServiceMap,
            fallback: new ConsoleApprovalService());

        // Collect tool providers from modules
        var toolProviders = ToolProviderCollector.Collect(_moduleRegistry, _config);

        // Prefer the QQ channel client so channel-specific tools (voice, file) are available in cron/heartbeat
        var channelClient = _channels.FirstOrDefault(ch => ch.ChannelClient != null)?.ChannelClient;

        var planStore = new PlanStore(_paths.CraftPath);

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
            planStore: planStore,
            hookRunner: hookRunner);

        var agent = _sharedAgentFactory.CreateAgentForMode(AgentMode.Agent);
        _sharedSessionService = SessionServiceFactory.Create(_sharedAgentFactory, agent, _sp);
        var runner = new AgentRunner(_paths.WorkspacePath, _sharedSessionService);
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

        var groupId = !string.IsNullOrEmpty(payload.CreatorGroupId) && long.TryParse(payload.CreatorGroupId, out var gid) ? gid : 0L;
        return new ApprovalContext { UserId = payload.CreatorId, Source = payload.CreatorSource, GroupId = groupId };
    }

    public async ValueTask DisposeAsync()
    {
        // Dispose channels (native + external)
        foreach (var ch in _allChannels)
            await ch.DisposeAsync();
        
        // Dispose shared agent factory
        if (_sharedAgentFactory != null)
            await _sharedAgentFactory.DisposeAsync();
    }
}
