using System.Reflection;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Lsp;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Tracing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DotCraft.Hosting;

public sealed class WorkspaceRuntime : IAsyncDisposable
{
    private sealed class StartedState(
        AgentFactory agentFactory,
        WireAcpExtensionProxy wireAcpExtensionProxy,
        WireNodeReplProxy wireNodeReplProxy,
        ISessionService sessionService,
        ICommitMessageSuggestService commitMessageSuggestService,
        WelcomeSuggestionService welcomeSuggestionService,
        AgentRunner agentRunner,
        CronService cronService,
        HeartbeatService heartbeatService,
        IAutomationsRequestHandler? automationsHandler,
        IAppServerChannelListContributor channelListContributor,
        IReadOnlyList<ConfigSchemaSection> configSchema,
        IWorkspaceRuntimeAppServerFeature? appServerFeature)
    {
        public AgentFactory AgentFactory { get; } = agentFactory;

        public WireAcpExtensionProxy WireAcpExtensionProxy { get; } = wireAcpExtensionProxy;

        public WireNodeReplProxy WireNodeReplProxy { get; } = wireNodeReplProxy;

        public ISessionService SessionService { get; } = sessionService;

        public ICommitMessageSuggestService CommitMessageSuggestService { get; } = commitMessageSuggestService;

        public WelcomeSuggestionService WelcomeSuggestionService { get; } = welcomeSuggestionService;

        public AgentRunner AgentRunner { get; } = agentRunner;

        public CronService CronService { get; } = cronService;

        public HeartbeatService HeartbeatService { get; } = heartbeatService;

        public IAutomationsRequestHandler? AutomationsHandler { get; } = automationsHandler;

        public IAppServerChannelListContributor ChannelListContributor { get; } = channelListContributor;

        public IReadOnlyList<ConfigSchemaSection> ConfigSchema { get; } = configSchema;

        public IWorkspaceRuntimeAppServerFeature? AppServerFeature { get; } = appServerFeature;
    }

    private readonly IAppConfigMonitor _appConfigMonitor;
    private readonly IWorkspaceRuntimeAppServerFeatureFactory? _appServerFeatureFactory;
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private StartedState? _started;
    private bool _disposed;

    public WorkspaceRuntime(
        IServiceProvider services,
        AppConfig config,
        DotCraftPaths paths,
        MemoryStore memoryStore,
        SkillsLoader skillsLoader,
        PathBlacklist pathBlacklist,
        McpClientManager mcpClientManager,
        LspServerManager lspServerManager)
    {
        Services = services;
        Config = config;
        Paths = paths;
        MemoryStore = memoryStore;
        SkillsLoader = skillsLoader;
        PathBlacklist = pathBlacklist;
        McpClientManager = mcpClientManager;
        LspServerManager = lspServerManager;
        _appConfigMonitor = services.GetRequiredService<IAppConfigMonitor>();
        _appServerFeatureFactory = services.GetService<IWorkspaceRuntimeAppServerFeatureFactory>();
    }

    public IServiceProvider Services { get; }

    public AppConfig Config { get; }

    public DotCraftPaths Paths { get; }

    public MemoryStore MemoryStore { get; }

    public SkillsLoader SkillsLoader { get; }

    public PathBlacklist PathBlacklist { get; }

    public McpClientManager McpClientManager { get; }

    public LspServerManager LspServerManager { get; }

    public bool IsStarted => _started != null;

    public ISessionService SessionService => EnsureStarted().SessionService;

    public ICommitMessageSuggestService CommitMessageSuggestService => EnsureStarted().CommitMessageSuggestService;

    public IWelcomeSuggestionService WelcomeSuggestionService => EnsureStarted().WelcomeSuggestionService;

    public CronService CronService => EnsureStarted().CronService;

    public HeartbeatService HeartbeatService => EnsureStarted().HeartbeatService;

    public IAutomationsRequestHandler? AutomationsHandler => EnsureStarted().AutomationsHandler;

    public IChannelStatusProvider? ChannelStatusProvider => EnsureStarted().AppServerFeature?.ChannelStatusProvider;

    public IAppServerChannelListContributor ChannelListContributor => EnsureStarted().ChannelListContributor;

    public WireAcpExtensionProxy WireAcpExtensionProxy => EnsureStarted().WireAcpExtensionProxy;

    public WireNodeReplProxy WireNodeReplProxy => EnsureStarted().WireNodeReplProxy;

    public IReadOnlyList<ConfigSchemaSection> ConfigSchema => EnsureStarted().ConfigSchema;

    public string? DashboardUrl => EnsureStarted().AppServerFeature?.DashboardUrl;

    public event Action<AppConfigChangedEventArgs>? WorkspaceConfigChanged;

    public event Action<McpServerStatusChangedEventArgs>? McpStatusChanged;

    public event Action<StructuredPlan>? PlanUpdated;

    public event Action<SessionThread>? ThreadStarted;

    public event Action<SessionThread>? ThreadRenamed;

    public event Action<string>? ThreadDeleted;

    public event Action<string, SessionThreadRuntimeSignal>? ThreadRuntimeSignal;

    public event Action<CronJob?, string, bool>? CronStateChanged;

    public event Action<BackgroundJobResult>? BackgroundJobResultProduced;

    public event Action<IAutomationTaskEventPayload>? AutomationTaskUpdated;

    public async Task StartAsync(ModuleRegistry moduleRegistry, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _lifecycleLock.WaitAsync(ct);
        try
        {
            if (_started != null)
                throw new InvalidOperationException("WorkspaceRuntime has already been started.");

            SkillsLoader.SetDisabledSkills(Config.Skills.DisabledSkills);

            var traceCollector = Services.GetService<TraceCollector>();
            var cronTools = Services.GetService<CronTools>();
            var backgroundTerminalService = Services.GetService<IBackgroundTerminalService>();
            var openAIClientProvider = Services.GetRequiredService<OpenAIClientProvider>();
            var mainModel = openAIClientProvider.ResolveMainModel(Config);

            ToolProviderCollector.ScanToolIcons(moduleRegistry, Config);
            var toolProviders = ToolProviderCollector.Collect(moduleRegistry, Config);

            var fallbackApproval = new AutoApproveApprovalService();
            var scopedApproval = new SessionScopedApprovalService(fallbackApproval);
            var planStore = new PlanStore(Paths.CraftPath);
            var wireAcpExtensionProxy = new WireAcpExtensionProxy();
            var wireNodeReplProxy = new WireNodeReplProxy();

            AgentFactory? agentFactory = null;
            HeartbeatService? heartbeatService = null;
            IWorkspaceRuntimeAppServerFeature? appServerFeature = null;
            WelcomeSuggestionService? welcomeSuggestionService = null;
            try
            {
                agentFactory = new AgentFactory(
                    Paths.CraftPath, Paths.WorkspacePath, Config,
                    MemoryStore, SkillsLoader,
                    approvalService: scopedApproval,
                    PathBlacklist,
                    toolProviders: toolProviders,
                    toolProviderContext: new ToolProviderContext
                    {
                        Config = Config,
                        ChatClient = openAIClientProvider.GetChatClient(Config, mainModel),
                        OpenAIClientProvider = openAIClientProvider,
                        EffectiveMainModel = mainModel,
                        WorkspacePath = Paths.WorkspacePath,
                        BotPath = Paths.CraftPath,
                        MemoryStore = MemoryStore,
                        SkillsLoader = SkillsLoader,
                        ApprovalService = scopedApproval,
                        PathBlacklist = PathBlacklist,
                        BackgroundTerminalService = backgroundTerminalService,
                        CronTools = cronTools,
                        McpClientManager = McpClientManager.Tools.Count > 0 ? McpClientManager : null,
                        LspServerManager = LspServerManager,
                        TraceCollector = traceCollector,
                        AcpExtensionProxy = wireAcpExtensionProxy,
                        NodeReplProxy = wireNodeReplProxy
                    },
                    traceCollector: traceCollector,
                    planStore: planStore,
                    onPlanUpdated: plan => PlanUpdated?.Invoke(plan),
                    openAIClientProvider: openAIClientProvider);

                if (Services.GetService<IChannelRuntimeToolProvider>() is IReservedRuntimeToolNameConfigurator reservedToolNameConfigurator)
                {
                    reservedToolNameConfigurator.ConfigureReservedToolNames(
                        agentFactory.CreateToolsForMode(AgentMode.Agent).Select(tool => tool.Name));
                }

                var agent = agentFactory.CreateAgentForMode(AgentMode.Agent);
                var sessionService = SessionServiceFactory.Create(agentFactory, agent, Services);
                sessionService.ThreadCreatedForBroadcast = thread => ThreadStarted?.Invoke(thread);
                sessionService.ThreadDeletedForBroadcast = threadId => ThreadDeleted?.Invoke(threadId);
                sessionService.ThreadRenamedForBroadcast = thread => ThreadRenamed?.Invoke(thread);
                sessionService.ThreadRuntimeSignalForBroadcast =
                    (threadId, signal) => ThreadRuntimeSignal?.Invoke(threadId, signal);

                var commitMessageSuggestService =
                    new CommitMessageSuggestService(sessionService, Paths.WorkspacePath);
                welcomeSuggestionService = new WelcomeSuggestionService(
                    sessionService,
                    Services.GetRequiredService<SessionPersistenceService>(),
                    MemoryStore,
                    Paths.WorkspacePath,
                    Services.GetService<ILoggerFactory>()
                        ?.CreateLogger<WelcomeSuggestionService>());
                var cronService = Services.GetRequiredService<CronService>();
                var agentRunner = new AgentRunner(Paths.WorkspacePath, sessionService, quiet: true);
                heartbeatService = new HeartbeatService(
                    Paths.CraftPath,
                    onHeartbeat: async (prompt, sessionKey, threadDisplayName, cancellationToken) =>
                    {
                        try
                        {
                            var run = await agentRunner.RunAsync(
                                prompt,
                                sessionKey,
                                threadDisplayName,
                                cancellationToken);
                            var backgroundJobResult = CreateHeartbeatBackgroundJobResult(run);
                            if (backgroundJobResult != null)
                                BackgroundJobResultProduced?.Invoke(backgroundJobResult);
                            return run;
                        }
                        catch (Exception ex)
                        {
                            Spectre.Console.AnsiConsole.MarkupLine(
                                $"[grey][[AppServer]][/] [red]Heartbeat run failed: {Spectre.Console.Markup.Escape(ex.Message)}[/]");
                            return null;
                        }
                    },
                    intervalSeconds: Config.Heartbeat.IntervalSeconds,
                    enabled: Config.Heartbeat.Enabled,
                    logger: Services.GetService<ILoggerFactory>()
                        ?.CreateLogger<HeartbeatService>());

                var channelListContributor =
                    new ModuleRegistryChannelListContributor(moduleRegistry, cronService, heartbeatService);
                var configSchema = BuildConfigSchema();

                if (_appServerFeatureFactory != null)
                {
                    appServerFeature = _appServerFeatureFactory.Create(Services);
                    appServerFeature.AutomationTaskUpdated += OnAutomationTaskUpdated;
                    await appServerFeature.StartAsync(
                        new WorkspaceRuntimeAppServerFeatureContext(
                            Services,
                            Config,
                            Paths,
                            moduleRegistry,
                            sessionService,
                            agentRunner,
                            cronService,
                            heartbeatService,
                            emitCronStateChanged: (job, id, removed) =>
                                CronStateChanged?.Invoke(job, id, removed),
                            emitBackgroundJobResult: result =>
                                BackgroundJobResultProduced?.Invoke(result)),
                        ct);
                }

                _started = new StartedState(
                    agentFactory,
                    wireAcpExtensionProxy,
                    wireNodeReplProxy,
                    sessionService,
                    commitMessageSuggestService,
                    welcomeSuggestionService,
                    agentRunner,
                    cronService,
                    heartbeatService,
                    Services.GetService<IAutomationsRequestHandler>(),
                    channelListContributor,
                    configSchema,
                    appServerFeature);

                _appConfigMonitor.Changed += OnAppConfigChanged;
                McpClientManager.StatusChanged += OnMcpStatusChanged;
            }
            catch
            {
                if (appServerFeature != null)
                {
                    appServerFeature.AutomationTaskUpdated -= OnAutomationTaskUpdated;
                    try
                    {
                        await appServerFeature.DisposeAsync();
                    }
                    catch
                    {
                        // ignored during failed startup cleanup
                    }
                }

                if (welcomeSuggestionService != null)
                {
                    try
                    {
                        await welcomeSuggestionService.DisposeAsync();
                    }
                    catch
                    {
                        // ignored during failed startup cleanup
                    }
                }

                heartbeatService?.Dispose();

                if (agentFactory != null)
                {
                    try
                    {
                        await agentFactory.DisposeAsync();
                    }
                    catch
                    {
                        // ignored during failed startup cleanup
                    }
                }

                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    internal static BackgroundJobResult? CreateHeartbeatBackgroundJobResult(AgentRunResult? run)
    {
        if (run == null)
            return null;

        if (run.Error == null && run.Result != null)
        {
            return new BackgroundJobResult(
                "heartbeat",
                null,
                null,
                run.Result,
                null,
                run.ThreadId,
                run.InputTokens,
                run.OutputTokens);
        }

        if (run.Error != null)
        {
            return new BackgroundJobResult(
                "heartbeat",
                null,
                null,
                null,
                run.Error,
                run.ThreadId,
                run.InputTokens,
                run.OutputTokens);
        }

        return null;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_disposed)
            return;

        await _lifecycleLock.WaitAsync(ct);
        try
        {
            var started = _started;
            if (started == null)
                return;

            _appConfigMonitor.Changed -= OnAppConfigChanged;
            McpClientManager.StatusChanged -= OnMcpStatusChanged;

            List<Exception>? errors = null;

            if (started.AppServerFeature != null)
            {
                started.AppServerFeature.AutomationTaskUpdated -= OnAutomationTaskUpdated;
                try
                {
                    await started.AppServerFeature.StopAsync(ct);
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }

                try
                {
                    await started.AppServerFeature.DisposeAsync();
                }
                catch (Exception ex)
                {
                    (errors ??= []).Add(ex);
                }
            }

            try
            {
                await started.WelcomeSuggestionService.DisposeAsync();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            try
            {
                started.HeartbeatService.Stop();
                started.HeartbeatService.OnResult = null;
                started.HeartbeatService.Dispose();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            try
            {
                started.CronService.OnJob = null;
                started.CronService.CronJobPersistedAfterExecution = null;
                started.CronService.Stop();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            try
            {
                started.SessionService.ThreadCreatedForBroadcast = null;
                started.SessionService.ThreadDeletedForBroadcast = null;
                started.SessionService.ThreadRenamedForBroadcast = null;
                started.SessionService.ThreadRuntimeSignalForBroadcast = null;
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            try
            {
                await started.AgentFactory.DisposeAsync();
            }
            catch (Exception ex)
            {
                (errors ??= []).Add(ex);
            }

            _started = null;

            if (errors is { Count: 1 })
                throw errors[0];
            if (errors is { Count: > 1 })
                throw new AggregateException(errors);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task ApplyExternalChannelUpsertAsync(ExternalChannelEntry entry, CancellationToken ct = default)
    {
        var feature = EnsureStarted().AppServerFeature;
        if (feature != null)
            await feature.ApplyExternalChannelUpsertAsync(entry, ct);
    }

    public async Task ApplyExternalChannelRemoveAsync(string channelName, CancellationToken ct = default)
    {
        var feature = EnsureStarted().AppServerFeature;
        if (feature != null)
            await feature.ApplyExternalChannelRemoveAsync(channelName, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await StopAsync();
        _lifecycleLock.Dispose();
        _disposed = true;
    }

    private StartedState EnsureStarted()
    {
        ThrowIfDisposed();
        return _started ?? throw new InvalidOperationException("WorkspaceRuntime has not been started.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WorkspaceRuntime));
    }

    private void OnAppConfigChanged(object? sender, AppConfigChangedEventArgs e)
    {
        _ = sender;
        WorkspaceConfigChanged?.Invoke(e);
    }

    private void OnMcpStatusChanged(object? sender, McpServerStatusChangedEventArgs e)
    {
        _ = sender;
        McpStatusChanged?.Invoke(e);
    }

    private void OnAutomationTaskUpdated(IAutomationTaskEventPayload task)
    {
        AutomationTaskUpdated?.Invoke(task);
    }

    private static IReadOnlyList<ConfigSchemaSection> BuildConfigSchema()
    {
        var types = new List<Type>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] assemblyTypes;
            try
            {
                assemblyTypes = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                assemblyTypes = ex.Types.Where(t => t != null).Cast<Type>().ToArray();
            }
            catch
            {
                continue;
            }

            types.AddRange(assemblyTypes.Where(type =>
                type.IsClass
                && !type.IsAbstract
                && type.GetCustomAttributes(typeof(ConfigSectionAttribute), inherit: false).Length > 0));
        }

        var configTypes = types
            .Distinct()
            .ToArray();

        var schema = new List<ConfigSchemaSection>();
        foreach (var configType in configTypes)
        {
            try
            {
                schema.AddRange(ConfigSchemaBuilder.BuildAll([configType]));
            }
            catch
            {
                // Some test-loaded assemblies may contain incomplete config metadata.
                // Skip invalid sections and preserve the rest of the runtime contract.
            }
        }

        return schema
            .OrderBy(section => section.Order)
            .ToArray();
    }
}

public interface IWorkspaceRuntimeFactory
{
    WorkspaceRuntime Create(IServiceProvider services);
}

internal sealed class WorkspaceRuntimeFactory : IWorkspaceRuntimeFactory
{
    public WorkspaceRuntime Create(IServiceProvider services)
    {
        return new WorkspaceRuntime(
            services,
            services.GetRequiredService<AppConfig>(),
            services.GetRequiredService<DotCraftPaths>(),
            services.GetRequiredService<MemoryStore>(),
            services.GetRequiredService<SkillsLoader>(),
            services.GetRequiredService<PathBlacklist>(),
            services.GetRequiredService<McpClientManager>(),
            services.GetRequiredService<LspServerManager>());
    }
}
