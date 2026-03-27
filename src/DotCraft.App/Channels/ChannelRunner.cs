using DotCraft.Abstractions;
using DotCraft.AppServer;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.DashBoard;
using DotCraft.ExternalChannel;
using DotCraft.Gateway;
using DotCraft.Heartbeat;
using DotCraft.Hosting;
using DotCraft.Modules;
using DotCraft.Protocol;
using DotCraft.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DotCraft.Channels;

/// <summary>
/// Manages native channels, external channels, <see cref="WebHostPool"/>, and DashBoard mounting
/// for AppServer mode (shared session with the wire host) and for Gateway mode.
/// </summary>
public sealed class ChannelRunner : IAsyncDisposable
{
    private readonly IServiceProvider _sp;
    private readonly AppConfig _config;
    private readonly DotCraftPaths _paths;
    private readonly ModuleRegistry _moduleRegistry;
    private readonly ExternalChannelRegistry _externalChannelRegistry;
    private readonly MessageRouter _router;
    private readonly List<IChannelService> _nativeChannels;
    private readonly List<IChannelService> _allChannels;

    private WebHostPool? _pool;
    private List<Task>? _channelTasks;
    private CancellationTokenSource? _channelCts;
    private bool _stopped;

    /// <summary>
    /// Public URL of the DashBoard UI (…/dashboard), or null when DashBoard is not hosted.
    /// </summary>
    public string? DashBoardUrl { get; private set; }

    public IReadOnlyList<IChannelService> NativeChannels => _nativeChannels;

    public MessageRouter Router => _router;

    private ChannelRunner(
        IServiceProvider sp,
        AppConfig config,
        DotCraftPaths paths,
        ModuleRegistry moduleRegistry,
        ExternalChannelRegistry externalChannelRegistry,
        MessageRouter router,
        List<IChannelService> nativeChannels)
    {
        _sp = sp;
        _config = config;
        _paths = paths;
        _moduleRegistry = moduleRegistry;
        _externalChannelRegistry = externalChannelRegistry;
        _router = router;
        _nativeChannels = nativeChannels;
        _allChannels = new List<IChannelService>(nativeChannels);
    }

    /// <summary>
    /// Creates a runner when native channels, external channels, and/or DashBoard should be hosted in-process.
    /// </summary>
    public static ChannelRunner? TryCreateForAppServer(
        IServiceProvider sp,
        AppConfig config,
        DotCraftPaths paths,
        ModuleRegistry registry)
    {
        var traceStore = sp.GetService<TraceStore>();
        var native = CollectNativeChannels(sp, config, paths, registry);
        var hasExternal = ExternalChannelManager.HasEnabledChannels(config);
        var wantDashboard = config.DashBoard.Enabled && traceStore != null;

        if (native.Count == 0 && !hasExternal && !wantDashboard)
            return null;

        ValidateAppServerPortConflict(config, native);

        var router = sp.GetRequiredService<MessageRouter>();
        var extReg = sp.GetRequiredService<ExternalChannelRegistry>();

        foreach (var ch in native)
            router.RegisterChannel(ch);

        return new ChannelRunner(sp, config, paths, registry, extReg, router, native);
    }

    /// <summary>
    /// Gateway host: constructs with pre-built native channels (may be empty).
    /// Native channels must already be registered on <paramref name="router"/> (see <see cref="GatewayHost"/> ctor).
    /// </summary>
    public static ChannelRunner CreateForGateway(
        IServiceProvider sp,
        AppConfig config,
        DotCraftPaths paths,
        ModuleRegistry moduleRegistry,
        ExternalChannelRegistry externalChannelRegistry,
        MessageRouter router,
        IReadOnlyList<IChannelService> nativeChannels)
    {
        var list = nativeChannels.ToList();
        return new ChannelRunner(sp, config, paths, moduleRegistry, externalChannelRegistry, router, list);
    }

    private static List<IChannelService> CollectNativeChannels(
        IServiceProvider sp,
        AppConfig config,
        DotCraftPaths paths,
        ModuleRegistry registry)
    {
        var subContext = new ModuleContext
        {
            Config = config,
            Paths = paths,
            ServiceProvider = sp
        };

        return registry
            .GetEnabledModules(config)
            .Where(m => m.Name is not ("gateway" or "app-server"))
            .Select(m => m.CreateChannelService(sp, subContext))
            .OfType<IChannelService>()
            .ToList();
    }

    /// <summary>
    /// Ensures DashBoard / native HTTP channels do not bind the same (host, port) as the AppServer WebSocket listener.
    /// </summary>
    public static void ValidateAppServerPortConflict(AppConfig config, IReadOnlyList<IChannelService> nativeChannels)
    {
        var appServer = config.GetSection<AppServerConfig>("AppServer");
        if (appServer.Mode is not (AppServerMode.WebSocket or AppServerMode.StdioAndWebSocket))
            return;

        var wsPort = appServer.WebSocket.Port;
        var wsHost = NormalizeHost(appServer.WebSocket.Host);

        if (config.DashBoard.Enabled && config.DashBoard.Port == wsPort
            && NormalizeHost(config.DashBoard.Host) == wsHost)
        {
            throw new InvalidOperationException(
                $"DashBoard cannot use the same address as AppServer WebSocket ({wsHost}:{wsPort}). Change DashBoard.Port or AppServer.WebSocket.Port.");
        }

        foreach (var ch in nativeChannels)
        {
            if (ch is not IWebHostingChannel wc)
                continue;
            if (wc.ListenPort != wsPort || NormalizeHost(wc.ListenHost) != wsHost)
                continue;
            if (string.Equals(wc.ListenScheme, "http", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Native channel '{ch.Name}' HTTP listener conflicts with AppServer WebSocket on {wsHost}:{wsPort}.");
            }
        }
    }

    private static string NormalizeHost(string host)
    {
        if (host is "::1" or "[::1]")
            return "127.0.0.1";
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return "127.0.0.1";
        return host;
    }

    /// <summary>
    /// Registers native web channels, optional DashBoard builder, and calls <see cref="WebHostPool.BuildAll"/>.
    /// Call <see cref="CompleteAfterSession"/> after <see cref="ISessionService"/> exists (Gateway defers session until after this).
    /// </summary>
    public void BuildPoolThroughBuildAll()
    {
        var traceStore = _sp.GetService<TraceStore>();

        _pool = new WebHostPool();

        foreach (var wc in _nativeChannels.OfType<IWebHostingChannel>())
            _pool.Register(wc);

        var dashboardEnabled = _config.DashBoard.Enabled && traceStore != null;
        if (dashboardEnabled)
        {
            var dashHost = _config.DashBoard.Host;
            var dashPort = _config.DashBoard.Port;

            var dashStandalone = !_nativeChannels.OfType<IWebHostingChannel>()
                .Any(wc => wc.ListenScheme == "http" &&
                           wc.ListenHost == dashHost &&
                           wc.ListenPort == dashPort);

            var dashBuilder = _pool.GetOrCreateBuilder("http", dashHost, dashPort);
            if (dashStandalone)
                dashBuilder.Logging.ClearProviders();
        }

        _pool.BuildAll();
    }

    /// <summary>
    /// External channels, session injection, Kestrel route configuration, and DashBoard routes.
    /// </summary>
    public void CompleteAfterSession(
        ISessionService sessionService,
        HeartbeatService heartbeatService,
        CronService cronService)
    {
        if (_pool == null)
            throw new InvalidOperationException("Call BuildPoolThroughBuildAll first.");

        var traceStore = _sp.GetService<TraceStore>();
        var tokenUsageStore = _sp.GetService<TokenUsageStore>();
        var orchestratorProviders = _sp.GetServices<IOrchestratorSnapshotProvider>().ToList();

        if (ExternalChannelManager.HasEnabledChannels(_config))
        {
            var nativeNames = _nativeChannels.Select(ch => ch.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ecManager = new ExternalChannelManager(
                _config, sessionService, nativeNames, _moduleRegistry, _paths.WorkspacePath, _externalChannelRegistry);

            foreach (var extCh in ecManager.Channels)
            {
                _allChannels.Add(extCh);
                _router.RegisterChannel(extCh);
            }
        }

        foreach (var ch in _allChannels.OfType<ISessionServiceConsumer>())
            ch.SetSessionService(sessionService);

        foreach (var ch in _allChannels)
        {
            ch.HeartbeatService = heartbeatService;
            ch.CronService = cronService;
        }

        _pool.ConfigureApps();

        var dashboardEnabled = _config.DashBoard.Enabled && traceStore != null;
        if (dashboardEnabled && traceStore != null)
        {
            var capturedOrchestrators = orchestratorProviders.Count > 0 ? orchestratorProviders : null;
            var dashApp = _pool.GetApp("http", _config.DashBoard.Host, _config.DashBoard.Port);
            dashApp.MapDashBoardAuth(_config);
            dashApp.UseDashBoardAuth(_config);
            var capturedSvc = sessionService;
            dashApp.MapDashBoard(traceStore, _paths, tokenUsageStore,
                orchestratorProviders: capturedOrchestrators,
                configTypes: ConfigSchemaRegistrations.GetAllConfigTypes(),
                sessionHandler: new DelegateDashBoardSessionHandler(id => capturedSvc.DeleteThreadPermanentlyAsync(id)),
                refreshTraceFromDiskBeforeRead: false);

            var baseUrl = $"http://{_config.DashBoard.Host}:{_config.DashBoard.Port}";
            DashBoardUrl = $"{baseUrl}/dashboard";
            AnsiConsole.MarkupLine(
                $"[green]DashBoard started at[/] [link={DashBoardUrl}]{DashBoardUrl}[/]");
        }
        else
        {
            DashBoardUrl = null;
        }
    }

    /// <summary>
    /// Builds the web pool, attaches external channels, maps DashBoard, and prepares channel tasks (not yet started).
    /// </summary>
    public void Initialize(
        ISessionService sessionService,
        HeartbeatService heartbeatService,
        CronService cronService)
    {
        BuildPoolThroughBuildAll();
        CompleteAfterSession(sessionService, heartbeatService, cronService);
    }

    /// <summary>
    /// Starts all Kestrel listeners in the pool (matches GatewayHost before cron/channel loops).
    /// </summary>
    public async Task StartWebPoolAsync()
    {
        if (_pool == null)
            throw new InvalidOperationException("Call Initialize before StartWebPoolAsync.");

        await _pool.StartAllAsync();
    }

    /// <summary>
    /// Starts <see cref="IChannelService.StartAsync"/> for every channel (fire-and-forget tasks).
    /// Call after shared Cron/Heartbeat services have been started when applicable.
    /// </summary>
    public void BeginChannelLoops(CancellationToken cancellationToken)
    {
        if (_pool == null)
            throw new InvalidOperationException("Call Initialize before BeginChannelLoops.");

        _channelCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _channelTasks = _allChannels
            .Select(ch => RunChannelAsync(ch, _channelCts.Token))
            .ToList();
    }

    /// <summary>
    /// Cancels channel tasks and stops the web pool.
    /// </summary>
    public async Task StopAsync()
    {
        if (_stopped)
            return;

        if (_channelCts != null)
        {
            try
            {
                await _channelCts.CancelAsync();
            }
            catch
            {
                // ignored
            }
        }

        if (_channelTasks is { Count: > 0 })
        {
            try
            {
                await Task.WhenAll(_channelTasks);
            }
            catch
            {
                // ignored
            }

            _channelTasks = null;
        }

        _channelCts?.Dispose();
        _channelCts = null;

        if (_pool != null)
        {
            await _pool.DisposeAsync();
            _pool = null;
        }

        _stopped = true;
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
                $"[grey][[Channels]][/] [red]Channel '{Markup.Escape(channel.Name)}' failed: {Markup.Escape(ex.Message)}[/]");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stopped)
            await StopAsync();

        foreach (var ch in _allChannels)
            await ch.DisposeAsync();
    }
}
