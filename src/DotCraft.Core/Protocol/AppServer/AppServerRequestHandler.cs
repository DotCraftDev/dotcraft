using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Commands.Core;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Logging;
using DotCraft.Localization;
using DotCraft.Mcp;
using DotCraft.Skills;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Dispatches incoming JSON-RPC requests to the appropriate <see cref="ISessionService"/>
/// method and returns a JSON-RPC response object ready for serialization.
///
/// Each public Handle* method maps directly to one of the wire protocol methods
/// defined in the Session Wire Protocol Specification.
/// </summary>
public sealed class AppServerRequestHandler(
    ISessionService sessionService,
    AppServerConnection connection,
    IAppServerTransport transport,
    IAppServerChannelListContributor channelListContributor,
    string serverVersion = "0.1.0",
    SessionApprovalDecision defaultApprovalDecision = SessionApprovalDecision.Reject,
    CronService? cronService = null,
    HeartbeatService? heartbeatService = null,
    SkillsLoader? skillsLoader = null,
    string? workspaceCraftPath = null,
    string? hostWorkspacePath = null,
    IAutomationsRequestHandler? automationsHandler = null,
    Action<CronJobWireInfo, bool>? broadcastCronStateChanged = null,
    Action<McpStatusInfoWire>? broadcastMcpStatusChanged = null,
    ICommitMessageSuggestService? commitMessageSuggest = null,
    string? dashboardUrl = null,
    WireAcpExtensionProxy? wireAcpExtensionProxy = null,
    CommandRegistry? commandRegistry = null,
    IChannelStatusProvider? channelStatusProvider = null,
    McpClientManager? mcpClientManager = null,
    IEnumerable<IAppServerProtocolExtension>? protocolExtensions = null,
    SessionStreamDebugLogger? streamDebugLogger = null)
{
    private readonly WireAcpExtensionProxy? _wireAcpExtensionProxy = wireAcpExtensionProxy;
    private readonly CommandRegistry _commandRegistry = commandRegistry
        ?? CommandRegistry.CreateDefault(
            !string.IsNullOrWhiteSpace(workspaceCraftPath) ? new CustomCommandLoader(workspaceCraftPath) : null);

    private readonly IAppServerChannelListContributor _channelListContributor = channelListContributor;

    private readonly IChannelStatusProvider? _channelStatusProvider = channelStatusProvider;

    private readonly string? _dashboardUrl = dashboardUrl;

    private readonly Action<CronJobWireInfo, bool>? _broadcastCronStateChanged = broadcastCronStateChanged;
    private readonly Action<McpStatusInfoWire>? _broadcastMcpStatusChanged = broadcastMcpStatusChanged;
    private readonly McpClientManager? _mcpClientManager = mcpClientManager;

    /// <summary>
    /// Fallback decision used by <see cref="AppServerEventDispatcher"/> when non-interactive
    /// approval resolution cannot be derived from thread policy.
    /// </summary>
    private readonly SessionApprovalDecision _defaultApprovalDecision = defaultApprovalDecision;

    /// <summary>
    /// When the wire client omits or sends an empty <c>identity.workspacePath</c>, substitute this
    /// host workspace root (AppServer / Gateway process workspace).
    /// </summary>
    private readonly string? _hostWorkspacePath = hostWorkspacePath;

    private readonly IReadOnlyDictionary<string, IAppServerMethodHandler> _extensionMethods =
        BuildExtensionMethodMap(protocolExtensions);

    private readonly IReadOnlyList<IAppServerCapabilityContributor> _capabilityContributors =
        protocolExtensions?.Cast<IAppServerCapabilityContributor>().ToArray()
        ?? [];

    private static readonly HashSet<string> ReservedMethodNames =
    [
        AppServerMethods.Initialize,
        AppServerMethods.ChannelList,
        AppServerMethods.ChannelStatus,
        AppServerMethods.ModelList,
        AppServerMethods.ThreadStart,
        AppServerMethods.ThreadResume,
        AppServerMethods.ThreadList,
        AppServerMethods.ThreadRead,
        AppServerMethods.ThreadSubscribe,
        AppServerMethods.ThreadUnsubscribe,
        AppServerMethods.ThreadPause,
        AppServerMethods.ThreadArchive,
        AppServerMethods.ThreadUnarchive,
        AppServerMethods.ThreadDelete,
        AppServerMethods.ThreadRename,
        AppServerMethods.ThreadModeSet,
        AppServerMethods.ThreadConfigUpdate,
        AppServerMethods.TurnStart,
        AppServerMethods.TurnInterrupt,
        AppServerMethods.WorkspaceCommitMessageSuggest,
        AppServerMethods.WorkspaceConfigUpdate,
        AppServerMethods.CronList,
        AppServerMethods.CronRemove,
        AppServerMethods.CronEnable,
        AppServerMethods.HeartbeatTrigger,
        AppServerMethods.SkillsList,
        AppServerMethods.SkillsRead,
        AppServerMethods.SkillsSetEnabled,
        AppServerMethods.CommandList,
        AppServerMethods.CommandExecute,
        AppServerMethods.AutomationTaskList,
        AppServerMethods.AutomationTaskRead,
        AppServerMethods.AutomationTaskCreate,
        AppServerMethods.AutomationTaskApprove,
        AppServerMethods.AutomationTaskReject,
        AppServerMethods.AutomationTaskDelete,
        AppServerMethods.McpList,
        AppServerMethods.McpGet,
        AppServerMethods.McpUpsert,
        AppServerMethods.McpRemove,
        AppServerMethods.McpStatusList,
        AppServerMethods.McpTest,
        AppServerMethods.ExternalChannelList,
        AppServerMethods.ExternalChannelGet,
        AppServerMethods.ExternalChannelUpsert,
        AppServerMethods.ExternalChannelRemove
    ];

    // -------------------------------------------------------------------------
    // Main dispatch
    // -------------------------------------------------------------------------

    /// <summary>
    /// Dispatches an incoming request to the appropriate handler.
    /// Returns the JSON-RPC response to send to the client.
    /// Throws <see cref="AppServerException"/> for protocol errors.
    /// Domain exceptions from <see cref="ISessionService"/> are translated to spec-defined
    /// error codes (Section 8.3): -32010 ThreadNotFound, -32011 ThreadNotActive, -32012 TurnInProgress.
    /// </summary>
    public async Task<object?> HandleRequestAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var method = msg.Method ?? string.Empty;

        // initialize is the only method allowed before the handshake
        if (method != AppServerMethods.Initialize && !connection.IsInitialized)
            throw AppServerErrors.NotInitialized();

        // After initialize response, block all requests until the client sends the
        // `initialized` notification (IsClientReady). This prevents premature operations
        // before the client has finished processing server capabilities.
        if (method != AppServerMethods.Initialize && connection.IsInitialized && !connection.IsClientReady)
            throw AppServerErrors.InvalidRequest("Server is awaiting the 'initialized' notification before handling requests.");

        try
        {
            // Route to the appropriate handler
            return await (method switch
            {
                AppServerMethods.Initialize => HandleInitializeAsync(msg, ct),
                AppServerMethods.ChannelList => HandleChannelListAsync(msg, ct),
                AppServerMethods.ChannelStatus => HandleChannelStatusAsync(msg, ct),
                AppServerMethods.ModelList => HandleModelListAsync(msg, ct),
                AppServerMethods.McpList => HandleMcpListAsync(msg, ct),
                AppServerMethods.McpGet => HandleMcpGetAsync(msg, ct),
                AppServerMethods.McpUpsert => HandleMcpUpsertAsync(msg, ct),
                AppServerMethods.McpRemove => HandleMcpRemoveAsync(msg, ct),
                AppServerMethods.ExternalChannelList => HandleExternalChannelListAsync(msg, ct),
                AppServerMethods.ExternalChannelGet => HandleExternalChannelGetAsync(msg, ct),
                AppServerMethods.ExternalChannelUpsert => HandleExternalChannelUpsertAsync(msg, ct),
                AppServerMethods.ExternalChannelRemove => HandleExternalChannelRemoveAsync(msg, ct),
                AppServerMethods.McpStatusList => HandleMcpStatusListAsync(msg, ct),
                AppServerMethods.McpTest => HandleMcpTestAsync(msg, ct),
                AppServerMethods.ThreadStart => HandleThreadStartAsync(msg, ct),
                AppServerMethods.ThreadResume => HandleThreadResumeAsync(msg, ct),
                AppServerMethods.ThreadList => HandleThreadListAsync(msg, ct),
                AppServerMethods.ThreadRead => HandleThreadReadAsync(msg, ct),
                AppServerMethods.ThreadSubscribe => HandleThreadSubscribeAsync(msg, ct),
                AppServerMethods.ThreadUnsubscribe => HandleThreadUnsubscribeAsync(msg, ct),
                AppServerMethods.ThreadPause => HandleThreadPauseAsync(msg, ct),
                AppServerMethods.ThreadArchive => HandleThreadArchiveAsync(msg, ct),
                AppServerMethods.ThreadUnarchive => HandleThreadUnarchiveAsync(msg, ct),
                AppServerMethods.ThreadDelete => HandleThreadDeleteAsync(msg, ct),
                AppServerMethods.ThreadRename => HandleThreadRenameAsync(msg, ct),
                AppServerMethods.ThreadModeSet => HandleThreadModeSetAsync(msg, ct),
                AppServerMethods.ThreadConfigUpdate => HandleThreadConfigUpdateAsync(msg, ct),
                AppServerMethods.TurnStart => HandleTurnStartAsync(msg, ct),
                AppServerMethods.TurnInterrupt => HandleTurnInterruptAsync(msg, ct),
                AppServerMethods.CronList => HandleCronListAsync(msg, ct),
                AppServerMethods.CronRemove => HandleCronRemoveAsync(msg, ct),
                AppServerMethods.CronEnable => HandleCronEnableAsync(msg, ct),
                AppServerMethods.HeartbeatTrigger => HandleHeartbeatTriggerAsync(msg, ct),
                AppServerMethods.SkillsList => HandleSkillsListAsync(msg, ct),
                AppServerMethods.SkillsRead => HandleSkillsReadAsync(msg, ct),
                AppServerMethods.SkillsSetEnabled => HandleSkillsSetEnabledAsync(msg, ct),
                AppServerMethods.CommandList => HandleCommandListAsync(msg, ct),
                AppServerMethods.CommandExecute => HandleCommandExecuteAsync(msg, ct),
                AppServerMethods.AutomationTaskList => RouteAutomation(h => h.HandleTaskListAsync(msg, ct)),
                AppServerMethods.AutomationTaskRead => RouteAutomation(h => h.HandleTaskReadAsync(msg, ct)),
                AppServerMethods.AutomationTaskCreate => RouteAutomation(h => h.HandleTaskCreateAsync(msg, ct)),
                AppServerMethods.AutomationTaskApprove => RouteAutomation(h => h.HandleTaskApproveAsync(msg, ct)),
                AppServerMethods.AutomationTaskReject => RouteAutomation(h => h.HandleTaskRejectAsync(msg, ct)),
                AppServerMethods.AutomationTaskDelete => RouteAutomation(h => h.HandleTaskDeleteAsync(msg, ct)),
                AppServerMethods.WorkspaceCommitMessageSuggest => HandleWorkspaceCommitMessageSuggestAsync(msg, ct),
                AppServerMethods.WorkspaceConfigUpdate => HandleWorkspaceConfigUpdateAsync(msg, ct),
                _ => TryHandleExtensionAsync(method, msg, ct)
            });
        }
        catch (KeyNotFoundException ex)
        {
            // Thread or turn not found in persistence or in-memory state
            throw AppServerErrors.ThreadNotFound(ExtractQuotedId(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            throw MapOperationException(ex);
        }
    }

    /// <summary>
    /// Handles the <c>initialized</c> client notification (no response required).
    /// </summary>
    public void HandleInitializedNotification()
    {
        connection.MarkClientReady();
    }

    // -------------------------------------------------------------------------
    // initialize (spec Section 3.2)
    // -------------------------------------------------------------------------

    private Task<object?> HandleInitializeAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AppServerInitializeParams>(msg);
        if (!connection.TryMarkInitialized(p.ClientInfo, p.Capabilities))
            throw AppServerErrors.AlreadyInitialized();

        var capabilities = new AppServerServerCapabilities
        {
            ThreadManagement = true,
            ThreadSubscriptions = true,
            ApprovalFlow = true,
            ModeSwitch = true,
            ConfigOverride = true,
            CronManagement = cronService != null,
            HeartbeatManagement = heartbeatService != null,
            SkillsManagement = skillsLoader != null,
            CommandManagement = true,
            Automations = automationsHandler != null,
            ChannelStatus = _channelStatusProvider != null,
            ModelCatalogManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            WorkspaceConfigManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            McpManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath) && _mcpClientManager != null,
            ExternalChannelManagement = !string.IsNullOrWhiteSpace(workspaceCraftPath),
            McpStatus = _mcpClientManager != null
        };

        var capabilityBuilder = new AppServerCapabilityBuilder(capabilities, workspaceCraftPath);
        foreach (var contributor in _capabilityContributors)
            contributor.ContributeCapabilities(capabilityBuilder);

        var result = new AppServerInitializeResult
        {
            ServerInfo = new AppServerServerInfo
            {
                Name = "dotcraft",
                Version = serverVersion,
                ProtocolVersion = "1"
            },
            Capabilities = capabilities,
            DashboardUrl = _dashboardUrl
        };

        return Task.FromResult<object?>(result);
    }

    // -------------------------------------------------------------------------
    // thread/* methods (spec Section 4)
    // -------------------------------------------------------------------------

    private SessionIdentity NormalizeIdentityWorkspace(SessionIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(identity.WorkspacePath) && !string.IsNullOrEmpty(_hostWorkspacePath))
            return identity with { WorkspacePath = _hostWorkspacePath };
        return identity;
    }

    private async Task<object?> HandleThreadStartAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadStartParams>(msg);
        var identity = NormalizeIdentityWorkspace(p.Identity);

        var historyMode = p.HistoryMode?.ToLowerInvariant() == "client"
            ? HistoryMode.Client
            : HistoryMode.Server;

        var thread = await sessionService.CreateThreadAsync(
            identity,
            p.Config,
            historyMode,
            displayName: p.DisplayName,
            ct: ct);

        if (_wireAcpExtensionProxy != null && connection.HasAcpExtensions)
            _wireAcpExtensionProxy.BindThread(thread.Id, transport, connection);

        // Fix 8: The host sends the thread/start response first, then emits the
        // thread/started notification as required by spec Section 4.1.
        await SendNotificationAfterResponseAsync(
            msg.Id,
            new { thread = thread.ToWire() },
            AppServerMethods.ThreadStarted,
            new { thread = thread.ToWire() },
            ct);

        // Return null to signal the response will be sent inline by SendNotificationAfterResponseAsync
        return null;
    }

    private async Task<object?> HandleThreadResumeAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadResumeParams>(msg);
        var thread = await sessionService.ResumeThreadAsync(p.ThreadId, ct);

        if (_wireAcpExtensionProxy != null && connection.HasAcpExtensions)
            _wireAcpExtensionProxy.BindThread(thread.Id, transport, connection);

        // Gap D: use the client's declared name from initialize instead of hardcoded "appserver".
        var resumedBy = connection.ClientInfo?.Name ?? "appserver";
        var responseResult = new { thread = thread.ToWire() };
        var notifParams = new { thread = thread.ToWire(), resumedBy };

        if (connection.HasSubscription(p.ThreadId))
        {
            // Gap C: connection has a passive subscription — the broker/dispatcher path will
            // emit thread/resumed. Send only the response to avoid duplicating the notification.
            await transport.WriteMessageAsync(BuildResponse(msg.Id, responseResult), ct);
            return null;
        }

        // No subscription: send response then notification inline.
        await SendNotificationAfterResponseAsync(msg.Id, responseResult, AppServerMethods.ThreadResumed, notifParams, ct);
        return null;
    }

    private async Task<object?> HandleThreadListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadListParams>(msg);
        var identity = NormalizeIdentityWorkspace(p.Identity);
        var crossOrigins = ResolveCrossChannelOriginsForThreadList(p);
        var threads = await sessionService.FindThreadsAsync(
            identity,
            p.IncludeArchived ?? false,
            crossOrigins,
            ct);

        if (!string.IsNullOrEmpty(p.ChannelName))
        {
            threads = threads
                .Where(t => string.Equals(t.OriginChannel, p.ChannelName, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        return new ThreadListResult { Data = [.. threads] };
    }

    /// <summary>
    /// Passes through <c>crossChannelOrigins</c> from the client; when omitted or null, no cross-channel list is applied.
    /// </summary>
    private static IReadOnlyList<string>? ResolveCrossChannelOriginsForThreadList(ThreadListParams p) =>
        p.CrossChannelOrigins;

    private Task<object?> HandleChannelListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        _ = ct;

        var channels = new List<ChannelInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string category)
        {
            if (!seen.Add(name))
                return;
            channels.Add(new ChannelInfo { Name = name, Category = category });
        }

        _channelListContributor.AppendBaseChannels(channels, seen);

        if (!string.IsNullOrEmpty(workspaceCraftPath))
        {
            var configPath = Path.Combine(workspaceCraftPath, "config.json");
            if (File.Exists(configPath))
            {
                try
                {
                    var cfg = AppConfig.LoadWithGlobalFallback(configPath);
                    foreach (var entry in cfg.ExternalChannels)
                    {
                        if (!entry.Enabled || string.IsNullOrWhiteSpace(entry.Name))
                            continue;
                        Add(entry.Name, "external");
                    }
                }
                catch
                {
                    // Best-effort: invalid config should not fail channel/list
                }
            }
        }

        static int CategoryOrder(string c) => c switch
        {
            "builtin" => 0,
            "social" => 1,
            "system" => 2,
            "external" => 3,
            _ => 4
        };

        channels.Sort((a, b) =>
        {
            var cmp = CategoryOrder(a.Category).CompareTo(CategoryOrder(b.Category));
            return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return Task.FromResult<object?>(new ChannelListResult { Channels = channels });
    }

    // -------------------------------------------------------------------------
    // channel/status (spec Section 20)
    // -------------------------------------------------------------------------

    private Task<object?> HandleChannelStatusAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        _ = ct;

        if (_channelStatusProvider == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.ChannelStatus);

        var statuses = _channelStatusProvider.GetChannelStatuses();
        return Task.FromResult<object?>(new ChannelStatusResult { Channels = [.. statuses] });
    }

    private async Task<object?> HandleModelListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = GetParams<ModelListParams>(msg);

        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.ModelList);

        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        var config = AppConfig.LoadWithGlobalFallback(configPath);
        var result = await OpenAIModelCatalog.FetchAsync(config, ct);

        return new ModelListResult
        {
            Success = result.Success,
            Models = [.. result.Models.Select(m => new ModelCatalogItem
            {
                Id = m.Id,
                OwnedBy = m.OwnedBy,
                CreatedAt = m.CreatedAt
            })],
            ErrorCode = result.Success ? null : result.ErrorCode.ToString(),
            ErrorMessage = result.Success ? null : result.ErrorMessage
        };
    }

    private async Task<object?> HandleMcpListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        EnsureMcpManagementAvailable();
        var servers = await _mcpClientManager!.ListConfigsAsync(ct);
        return new McpListResult { Servers = servers.Select(MapMcpConfigToWire).ToList() };
    }

    private async Task<object?> HandleMcpGetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<McpGetParams>(msg);
        EnsureMcpManagementAvailable();
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var server = await _mcpClientManager!.GetConfigAsync(p.Name, ct);
        if (server == null)
            throw AppServerErrors.McpServerNotFound(p.Name);

        return new McpGetResult { Server = MapMcpConfigToWire(server) };
    }

    private async Task<object?> HandleMcpUpsertAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<McpUpsertParams>(msg);
        EnsureMcpManagementAvailable();
        var mcpManager = _mcpClientManager!;
        ValidateMcpConfigWire(p.Server);

        var server = MapWireToMcpConfig(p.Server);
        await mcpManager.UpsertAsync(server, ct);
        await SaveWorkspaceMcpServersAsync(workspaceCraftPath!, mcpManager, ct);

        var updated = await mcpManager.GetConfigAsync(server.Name, ct) ?? server;
        var status = (await mcpManager.ListStatusesAsync(ct))
            .FirstOrDefault(s => string.Equals(s.Name, updated.Name, StringComparison.OrdinalIgnoreCase));
        if (status != null)
            _broadcastMcpStatusChanged?.Invoke(MapMcpStatusToWire(status));

        return new McpUpsertResult { Server = MapMcpConfigToWire(updated) };
    }

    private async Task<object?> HandleMcpRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<McpRemoveParams>(msg);
        EnsureMcpManagementAvailable();
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var removed = await _mcpClientManager!.RemoveAsync(p.Name, ct);
        if (!removed)
            throw AppServerErrors.McpServerNotFound(p.Name);

        await SaveWorkspaceMcpServersAsync(workspaceCraftPath!, _mcpClientManager, ct);
        return new McpRemoveResult { Removed = true };
    }

    private Task<object?> HandleExternalChannelListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        _ = ct;
        EnsureExternalChannelManagementAvailable();
        var channels = LoadWorkspaceExternalChannels(workspaceCraftPath!);
        return Task.FromResult<object?>(new ExternalChannelListResult
        {
            Channels = channels.Select(MapExternalChannelToWire).ToList()
        });
    }

    private Task<object?> HandleExternalChannelGetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        var p = GetParams<ExternalChannelGetParams>(msg);
        EnsureExternalChannelManagementAvailable();
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var channel = LoadWorkspaceExternalChannels(workspaceCraftPath!)
            .FirstOrDefault(c => string.Equals(c.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        if (channel == null)
            throw AppServerErrors.ExternalChannelNotFound(p.Name);

        return Task.FromResult<object?>(new ExternalChannelGetResult
        {
            Channel = MapExternalChannelToWire(channel)
        });
    }

    private Task<object?> HandleExternalChannelUpsertAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        var p = GetParams<ExternalChannelUpsertParams>(msg);
        EnsureExternalChannelManagementAvailable();
        ValidateExternalChannelConfigWire(p.Channel);

        var channel = MapWireToExternalChannelConfig(p.Channel);
        EnsureExternalChannelNameAvailable(channel.Name);

        var channels = LoadWorkspaceExternalChannels(workspaceCraftPath!);
        var existingIndex = channels.FindIndex(c => string.Equals(c.Name, channel.Name, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            channels[existingIndex] = channel;
        else
            channels.Add(channel);

        SaveWorkspaceExternalChannels(workspaceCraftPath!, channels);
        return Task.FromResult<object?>(new ExternalChannelUpsertResult
        {
            Channel = MapExternalChannelToWire(channel)
        });
    }

    private Task<object?> HandleExternalChannelRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        var p = GetParams<ExternalChannelRemoveParams>(msg);
        EnsureExternalChannelManagementAvailable();
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var channels = LoadWorkspaceExternalChannels(workspaceCraftPath!);
        var removed = channels.RemoveAll(c => string.Equals(c.Name, p.Name, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
            throw AppServerErrors.ExternalChannelNotFound(p.Name);

        SaveWorkspaceExternalChannels(workspaceCraftPath!, channels);
        return Task.FromResult<object?>(new ExternalChannelRemoveResult { Removed = true });
    }

    private async Task<object?> HandleMcpStatusListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = msg;
        if (_mcpClientManager == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.McpStatusList);

        var statuses = await _mcpClientManager.ListStatusesAsync(ct);
        return new McpStatusListResult { Servers = statuses.Select(MapMcpStatusToWire).ToList() };
    }

    private async Task<object?> HandleMcpTestAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<McpTestParams>(msg);
        if (_mcpClientManager == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.McpTest);

        ValidateMcpConfigWire(p.Server);
        var status = await _mcpClientManager.TestAsync(MapWireToMcpConfig(p.Server), ct);
        return new McpTestResult
        {
            Success = string.Equals(status.StartupState, "ready", StringComparison.OrdinalIgnoreCase),
            ErrorCode = status.LastError == null ? null : "McpServerTestFailed",
            ErrorMessage = status.LastError,
            ToolCount = string.Equals(status.StartupState, "ready", StringComparison.OrdinalIgnoreCase) ? status.ToolCount : null
        };
    }

    private async Task<object?> HandleThreadReadAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadReadParams>(msg);
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        var includeTurns = p.IncludeTurns ?? false;
        return new { thread = thread.ToWire(includeTurns) };
    }

    private Task<object?> HandleThreadSubscribeAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadSubscribeParams>(msg);

        // Start a background subscription that fans out thread events to this connection
        var subCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!connection.TryAddSubscription(p.ThreadId, subCts))
        {
            // Already subscribed — idempotent, just return success
            return Task.FromResult<object?>(new { });
        }

        var events = sessionService.SubscribeThreadAsync(
            p.ThreadId,
            p.ReplayRecent ?? false,
            subCts.Token);

        var dispatcher = new AppServerEventDispatcher(
            events, connection, transport, sessionService,
            defaultApprovalDecision: _defaultApprovalDecision,
            streamDebugLogger: streamDebugLogger);
        _ = dispatcher.RunAsync(subCts.Token)
            .ContinueWith(t =>
            {
                connection.TryCancelSubscription(p.ThreadId);
                if (t.IsFaulted)
                    _ = Console.Error.WriteLineAsync(
                        $"[AppServer] Subscription error for thread {p.ThreadId}: {t.Exception?.GetBaseException().Message}");
            }, TaskContinuationOptions.ExecuteSynchronously);

        return Task.FromResult<object?>(new { });
    }

    private Task<object?> HandleThreadUnsubscribeAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadUnsubscribeParams>(msg);
        connection.TryCancelSubscription(p.ThreadId);
        return Task.FromResult<object?>(new { });
    }

    private async Task<object?> HandleThreadPauseAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadPauseParams>(msg);

        // Gap B: capture previousStatus before the operation so the notification is accurate.
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        var previousStatus = thread.Status;

        await sessionService.PauseThreadAsync(p.ThreadId, ct);

        // Idempotent: if the thread was already paused, no status change occurred.
        if (previousStatus == ThreadStatus.Paused)
            return new { };

        if (connection.HasSubscription(p.ThreadId))
        {
            // Gap C: subscription exists — broker/dispatcher will emit thread/statusChanged.
            await transport.WriteMessageAsync(BuildResponse(msg.Id, new { }), ct);
            return null;
        }

        await SendNotificationAfterResponseAsync(
            msg.Id, new { },
            AppServerMethods.ThreadStatusChanged,
            new { threadId = p.ThreadId, previousStatus, newStatus = ThreadStatus.Paused },
            ct);
        return null;
    }

    private async Task<object?> HandleThreadArchiveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadArchiveParams>(msg);

        // Gap B: capture previousStatus before the operation so the notification is accurate.
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        var previousStatus = thread.Status;

        await sessionService.ArchiveThreadAsync(p.ThreadId, ct);

        // Idempotent: if the thread was already archived, no status change occurred.
        if (previousStatus == ThreadStatus.Archived)
            return new { };

        if (connection.HasSubscription(p.ThreadId))
        {
            // Gap C: subscription exists — broker/dispatcher will emit thread/statusChanged.
            await transport.WriteMessageAsync(BuildResponse(msg.Id, new { }), ct);
            return null;
        }

        await SendNotificationAfterResponseAsync(
            msg.Id, new { },
            AppServerMethods.ThreadStatusChanged,
            new { threadId = p.ThreadId, previousStatus, newStatus = ThreadStatus.Archived },
            ct);
        return null;
    }

    private async Task<object?> HandleThreadUnarchiveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadUnarchiveParams>(msg);

        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        var previousStatus = thread.Status;

        await sessionService.UnarchiveThreadAsync(p.ThreadId, ct);

        if (previousStatus == ThreadStatus.Active)
            return new { };

        if (connection.HasSubscription(p.ThreadId))
        {
            await transport.WriteMessageAsync(BuildResponse(msg.Id, new { }), ct);
            return null;
        }

        await SendNotificationAfterResponseAsync(
            msg.Id, new { },
            AppServerMethods.ThreadStatusChanged,
            new { threadId = p.ThreadId, previousStatus, newStatus = ThreadStatus.Active },
            ct);
        return null;
    }

    private async Task<object?> HandleThreadDeleteAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadDeleteParams>(msg);
        await sessionService.DeleteThreadPermanentlyAsync(p.ThreadId, ct);
        return new { };
    }

    private async Task<object?> HandleThreadRenameAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadRenameParams>(msg);
        if (string.IsNullOrWhiteSpace(p.DisplayName))
            throw AppServerErrors.InvalidParams("'displayName' must not be empty.");
        await sessionService.RenameThreadAsync(p.ThreadId, p.DisplayName, ct);
        return new { };
    }

    private async Task<object?> HandleThreadModeSetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadModeSetParams>(msg);
        await sessionService.SetThreadModeAsync(p.ThreadId, p.Mode, ct);
        return new { };
    }

    private async Task<object?> HandleThreadConfigUpdateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadConfigUpdateParams>(msg);
        await sessionService.UpdateThreadConfigurationAsync(p.ThreadId, p.Config, ct);
        return new { };
    }

    // -------------------------------------------------------------------------
    // turn/* methods (spec Section 5)
    // -------------------------------------------------------------------------

    private async Task<object?> HandleTurnStartAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<TurnStartParams>(msg);

        if (p.Input.Count == 0)
            throw AppServerErrors.InvalidParams("'input' must contain at least one part.");

        var content = await ResolveInputPartsAsync(p.Input, ct);

        // Set ChannelSessionScope so that SessionService.ResolveApprovalSource returns the correct
        // channel name for approval routing, and CronTools captures the right delivery target.
        // For CLI clients: "cli" channel, adapter client name as userId.
        // For external adapters: use the real platform user/chat IDs from SenderContext so that
        // cron payloads store a usable delivery target (e.g. the Telegram chat_id) rather than
        // the adapter's process-level client name.
        var channelScopeInfo = connection.IsChannelAdapter
            ? new ChannelSessionInfo
            {
                Channel = connection.ChannelAdapterName ?? "external",
                UserId = p.Sender?.SenderId ?? connection.ClientInfo?.Name ?? "anonymous",
                GroupId = p.Sender?.GroupId,
                DefaultDeliveryTarget = p.Sender?.GroupId,
            }
            : new ChannelSessionInfo
            {
                Channel = connection.HasAcpExtensions ? "acp" : "cli",
                UserId = connection.ClientInfo?.Name ?? "anonymous"
            };

        // Fix 5: Deserialize client-provided history for historyMode=client threads.
        ChatMessage[]? messages = null;
        if (p.Messages.HasValue && p.Messages.Value.ValueKind != JsonValueKind.Null)
        {
            try
            {
                messages = JsonSerializer.Deserialize<ChatMessage[]>(
                    p.Messages.Value.GetRawText(),
                    SessionWireJsonOptions.Default);
            }
            catch (JsonException ex)
            {
                throw AppServerErrors.InvalidParams($"Failed to deserialize 'messages': {ex.Message}");
            }
        }

        // TCS to receive the initial turn from the event dispatcher
        var initialTurnTcs = new TaskCompletionSource<SessionWireTurn>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // onTurnStarted: send the turn/start response, then signal the dispatcher to
        // proceed with the turn/started notification — guaranteeing correct ordering.
        async Task OnTurnStarted(SessionWireTurn initialTurn)
        {
            // Build and send the turn/start JSON-RPC response before the notification
            var responsePayload = new { turn = initialTurn };
            await transport.WriteMessageAsync(BuildResponse(msg.Id, responsePayload), ct);
            // Signal that the response was sent successfully
            initialTurnTcs.TrySetResult(initialTurn);
        }

        using var channelScope = channelScopeInfo != null ? ChannelSessionScope.Set(channelScopeInfo) : null;

        // Ensure thread is loaded into memory (may only exist on disk after server restart)
        // and per-thread Configuration agent/MCP is hydrated (GetThreadAsync alone does not rebuild agents).
        await sessionService.EnsureThreadLoadedAsync(p.ThreadId, ct);

        var events = sessionService.SubmitInputAsync(p.ThreadId, content, p.Sender, messages, ct);

        // Spec §6.10 (at-most-once delivery guarantee): when the connection already holds an active
        // thread/subscribe subscription for this thread, the subscription dispatcher is the sole
        // notification delivery path. Creating a second AppServerEventDispatcher here would send
        // every turn event twice on the same transport. Instead, we read only the first TurnStarted
        // event from the turn channel (needed to build the turn/start response), send the response,
        // and then drain the turn channel silently so the unbounded channel does not accumulate.
        if (connection.HasSubscription(p.ThreadId))
        {
            await foreach (var evt in events.WithCancellation(ct))
            {
                if (evt.EventType == SessionEventType.TurnStarted && evt.TurnPayload is { } startedTurn)
                {
                    var wireTurn = startedTurn.ToWire(includeItems: false) with { Items = [] };
                    await transport.WriteMessageAsync(BuildResponse(msg.Id, new { turn = wireTurn }), ct);
                    break;
                }
            }

            // Drain the rest of the turn channel in the background so the unbounded channel does
            // not hold memory for the duration of the turn. The subscription dispatcher on the
            // broker side is the authoritative delivery path and handles all further events.
            _ = Task.Run(async () =>
            {
                await foreach (var _ in events.WithCancellation(ct)) { }
            }, ct);

            return null;
        }

        var dispatcher = new AppServerEventDispatcher(
            events, connection, transport, sessionService, OnTurnStarted,
            defaultApprovalDecision: _defaultApprovalDecision,
            streamDebugLogger: streamDebugLogger);

        var dispatchTask = dispatcher.RunAsync(ct);

        // Propagate dispatch failures to the TCS so we don't hang indefinitely
        _ = dispatchTask.ContinueWith(t =>
        {
            if (t.IsFaulted)
                initialTurnTcs.TrySetException(t.Exception!.GetBaseException());
            else if (t.IsCanceled)
                initialTurnTcs.TrySetCanceled(ct);
            else
                initialTurnTcs.TrySetException(new AppServerException(
                    AppServerErrors.InternalErrorCode,
                    "Event dispatch completed without emitting a TurnStarted event."));
        }, TaskContinuationOptions.ExecuteSynchronously);

        // Wait until the response has been sent (or dispatch failed)
        await initialTurnTcs.Task;

        // Return null to signal the host that the response has already been sent inline
        return null;
    }

    private async Task<object?> HandleTurnInterruptAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<TurnInterruptParams>(msg);

        // Issue E: validate thread and turn existence/status before cancelling.
        // GetThreadAsync throws KeyNotFoundException → mapped to -32010 by the outer catch.
        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);

        var turn = thread.Turns.FirstOrDefault(t => t.Id == p.TurnId);
        if (turn == null)
            throw AppServerErrors.TurnNotFound(p.TurnId);

        if (turn.Status != TurnStatus.Running && turn.Status != TurnStatus.WaitingApproval)
            throw AppServerErrors.TurnNotRunning(p.TurnId);

        await sessionService.CancelTurnAsync(p.ThreadId, p.TurnId, ct);
        return new { };
    }

    private async Task<object?> HandleWorkspaceCommitMessageSuggestAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (commitMessageSuggest == null)
            throw AppServerErrors.InvalidRequest("Commit message suggestion is not available on this connection.");
        var p = GetParams<WorkspaceCommitMessageSuggestParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        if (p.Paths is not { Length: > 0 })
            throw AppServerErrors.InvalidParams("'paths' must contain at least one file path.");
        try
        {
            return await commitMessageSuggest.SuggestAsync(p, ct);
        }
        catch (KeyNotFoundException ex)
        {
            throw AppServerErrors.ThreadNotFound(ExtractQuotedId(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            throw AppServerErrors.InvalidRequest(ex.Message);
        }
    }

    private void EnsureMcpManagementAvailable()
    {
        if (_mcpClientManager == null || string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound("mcp/*");
    }

    private void EnsureExternalChannelManagementAvailable()
    {
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound("externalChannel/*");
    }

    private static McpServerConfigWire MapMcpConfigToWire(McpServerConfig config) => new()
    {
        Name = config.Name,
        Enabled = config.Enabled,
        Transport = config.NormalizedTransport,
        Command = string.IsNullOrWhiteSpace(config.Command) ? null : config.Command,
        Args = config.Arguments.Count > 0 ? [.. config.Arguments] : null,
        Env = config.EnvironmentVariables.Count > 0 ? new Dictionary<string, string>(config.EnvironmentVariables) : null,
        EnvVars = config.EnvVars.Count > 0 ? [.. config.EnvVars] : null,
        Cwd = config.Cwd,
        Url = string.IsNullOrWhiteSpace(config.Url) ? null : config.Url,
        BearerTokenEnvVar = config.BearerTokenEnvVar,
        HttpHeaders = config.Headers.Count > 0 ? new Dictionary<string, string>(config.Headers) : null,
        EnvHttpHeaders = config.EnvHttpHeaders.Count > 0 ? new Dictionary<string, string>(config.EnvHttpHeaders) : null,
        StartupTimeoutSec = config.StartupTimeoutSec,
        ToolTimeoutSec = config.ToolTimeoutSec
    };

    private static McpStatusInfoWire MapMcpStatusToWire(McpServerStatusSnapshot status) => new()
    {
        Name = status.Name,
        Enabled = status.Enabled,
        StartupState = status.StartupState,
        ToolCount = status.ToolCount,
        ResourceCount = status.ResourceCount,
        ResourceTemplateCount = status.ResourceTemplateCount,
        LastError = status.LastError,
        Transport = status.Transport
    };

    private static McpServerConfig MapWireToMcpConfig(McpServerConfigWire wire) => new()
    {
        Name = wire.Name.Trim(),
        Enabled = wire.Enabled,
        Transport = NormalizeMcpTransport(wire.Transport),
        Command = wire.Command?.Trim() ?? string.Empty,
        Arguments = wire.Args ?? [],
        EnvironmentVariables = wire.Env ?? new Dictionary<string, string>(),
        EnvVars = wire.EnvVars ?? [],
        Cwd = string.IsNullOrWhiteSpace(wire.Cwd) ? null : wire.Cwd.Trim(),
        Url = wire.Url?.Trim() ?? string.Empty,
        BearerTokenEnvVar = string.IsNullOrWhiteSpace(wire.BearerTokenEnvVar) ? null : wire.BearerTokenEnvVar.Trim(),
        Headers = wire.HttpHeaders ?? new Dictionary<string, string>(),
        EnvHttpHeaders = wire.EnvHttpHeaders ?? new Dictionary<string, string>(),
        StartupTimeoutSec = wire.StartupTimeoutSec,
        ToolTimeoutSec = wire.ToolTimeoutSec
    };

    private static string NormalizeMcpTransport(string? transport) =>
        transport?.Equals("streamableHttp", StringComparison.OrdinalIgnoreCase) == true
            || transport?.Equals("streamable-http", StringComparison.OrdinalIgnoreCase) == true
            || transport?.Equals("http", StringComparison.OrdinalIgnoreCase) == true
                ? "streamableHttp"
                : "stdio";

    private static ExternalChannelConfigWire MapExternalChannelToWire(ExternalChannelEntry config) => new()
    {
        Name = config.Name,
        Enabled = config.Enabled,
        Transport = config.Transport == ExternalChannelTransport.Websocket ? "websocket" : "subprocess",
        Command = string.IsNullOrWhiteSpace(config.Command) ? null : config.Command,
        Args = config.Args is { Count: > 0 } ? [.. config.Args] : null,
        WorkingDirectory = string.IsNullOrWhiteSpace(config.WorkingDirectory) ? null : config.WorkingDirectory,
        Env = config.Env is { Count: > 0 } ? new Dictionary<string, string>(config.Env, StringComparer.Ordinal) : null
    };

    private static ExternalChannelEntry MapWireToExternalChannelConfig(ExternalChannelConfigWire wire) => new()
    {
        Name = wire.Name.Trim(),
        Enabled = wire.Enabled,
        Transport = NormalizeExternalChannelTransport(wire.Transport),
        Command = string.IsNullOrWhiteSpace(wire.Command) ? null : wire.Command.Trim(),
        Args = wire.Args is { Count: > 0 } ? [.. wire.Args.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => a.Trim())] : null,
        WorkingDirectory = string.IsNullOrWhiteSpace(wire.WorkingDirectory) ? null : wire.WorkingDirectory.Trim(),
        Env = wire.Env is { Count: > 0 } ? new Dictionary<string, string>(wire.Env, StringComparer.Ordinal) : null
    };

    private static ExternalChannelTransport NormalizeExternalChannelTransport(string? transport) =>
        transport?.Equals("websocket", StringComparison.OrdinalIgnoreCase) == true
            ? ExternalChannelTransport.Websocket
            : ExternalChannelTransport.Subprocess;

    private static void ValidateMcpConfigWire(McpServerConfigWire server)
    {
        if (string.IsNullOrWhiteSpace(server.Name))
            throw AppServerErrors.McpServerValidationFailed("'server.name' is required.");

        var transport = NormalizeMcpTransport(server.Transport);

        if (transport == "stdio")
        {
            if (string.IsNullOrWhiteSpace(server.Command))
                throw AppServerErrors.McpServerValidationFailed("'server.command' is required for stdio transport.");
            if (!string.IsNullOrWhiteSpace(server.Url))
                throw AppServerErrors.McpServerValidationFailed("'server.url' is not supported for stdio transport.");
            if (!string.IsNullOrWhiteSpace(server.BearerTokenEnvVar))
                throw AppServerErrors.McpServerValidationFailed("'server.bearerTokenEnvVar' is not supported for stdio transport.");
            if (server.HttpHeaders is { Count: > 0 } || server.EnvHttpHeaders is { Count: > 0 })
                throw AppServerErrors.McpServerValidationFailed("HTTP headers are not supported for stdio transport.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(server.Url))
                throw AppServerErrors.McpServerValidationFailed("'server.url' is required for streamableHttp transport.");
            if (!Uri.TryCreate(server.Url, UriKind.Absolute, out _))
                throw AppServerErrors.McpServerValidationFailed("'server.url' must be an absolute URL.");
            if (!string.IsNullOrWhiteSpace(server.Command) ||
                server.Args is { Count: > 0 } ||
                server.Env is { Count: > 0 } ||
                server.EnvVars is { Count: > 0 } ||
                !string.IsNullOrWhiteSpace(server.Cwd))
            {
                throw AppServerErrors.McpServerValidationFailed("stdio-only fields are not supported for streamableHttp transport.");
            }
        }
    }

    private static void ValidateExternalChannelConfigWire(ExternalChannelConfigWire channel)
    {
        if (string.IsNullOrWhiteSpace(channel.Name))
            throw AppServerErrors.ExternalChannelValidationFailed("'channel.name' is required.");

        var transport = NormalizeExternalChannelTransport(channel.Transport);

        if (transport == ExternalChannelTransport.Subprocess)
        {
            if (string.IsNullOrWhiteSpace(channel.Command))
                throw AppServerErrors.ExternalChannelValidationFailed("'channel.command' is required for subprocess transport.");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(channel.Command) ||
                channel.Args is { Count: > 0 } ||
                !string.IsNullOrWhiteSpace(channel.WorkingDirectory) ||
                channel.Env is { Count: > 0 })
            {
                throw AppServerErrors.ExternalChannelValidationFailed("subprocess-only fields are not supported for websocket transport.");
            }
        }
    }

    private static async Task SaveWorkspaceMcpServersAsync(
        string workspaceCraftPath,
        McpClientManager manager,
        CancellationToken ct)
    {
        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        Directory.CreateDirectory(workspaceCraftPath);
        var root = LoadWorkspaceConfigObject(configPath);

        var key = FindCaseInsensitiveKey(root, "McpServers") ?? "McpServers";
        var servers = await manager.ListConfigsAsync(ct);
        var serverObject = new JsonObject();
        foreach (var server in servers.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(server.Name))
                continue;

            var serverNode = JsonSerializer.SerializeToNode(server, AppConfig.SerializerOptions);
            if (serverNode != null)
                serverObject[server.Name] = serverNode;
        }

        root[key] = serverObject;

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, $"{json}{Environment.NewLine}", new UTF8Encoding(false));
    }

    private static List<ExternalChannelEntry> LoadWorkspaceExternalChannels(string workspaceCraftPath)
    {
        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        return AppConfig.Load(configPath).ExternalChannels.Select(c => c.Clone()).ToList();
    }

    private static void SaveWorkspaceExternalChannels(string workspaceCraftPath, IReadOnlyCollection<ExternalChannelEntry> channels)
    {
        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        Directory.CreateDirectory(workspaceCraftPath);
        var root = LoadWorkspaceConfigObject(configPath);

        var key = FindCaseInsensitiveKey(root, "ExternalChannels") ?? "ExternalChannels";
        var channelObject = new JsonObject();
        foreach (var channel in channels.Where(c => !string.IsNullOrWhiteSpace(c.Name))
                     .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            channelObject[channel.Name] = BuildExternalChannelNode(channel);
        }

        root[key] = channelObject;

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, $"{json}{Environment.NewLine}", new UTF8Encoding(false));
    }

    private void EnsureExternalChannelNameAvailable(string name)
    {
        var nativeChannels = new List<ChannelInfo>();
        _channelListContributor.AppendBaseChannels(nativeChannels, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        if (nativeChannels.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw AppServerErrors.ExternalChannelNameConflict(
                $"'{name}' conflicts with a native channel name.");
        }
    }

    private static JsonObject BuildExternalChannelNode(ExternalChannelEntry channel)
    {
        var node = new JsonObject
        {
            ["enabled"] = channel.Enabled,
            ["transport"] = channel.Transport == ExternalChannelTransport.Websocket ? "websocket" : "subprocess"
        };

        if (channel.Transport == ExternalChannelTransport.Subprocess)
        {
            if (!string.IsNullOrWhiteSpace(channel.Command))
                node["command"] = channel.Command;
            if (channel.Args is { Count: > 0 })
                node["args"] = JsonSerializer.SerializeToNode(channel.Args);
            if (!string.IsNullOrWhiteSpace(channel.WorkingDirectory))
                node["workingDirectory"] = channel.WorkingDirectory;
            if (channel.Env is { Count: > 0 })
                node["env"] = JsonSerializer.SerializeToNode(channel.Env);
        }

        return node;
    }

    private Task<object?> HandleWorkspaceConfigUpdateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        if (string.IsNullOrWhiteSpace(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.WorkspaceConfigUpdate);
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind != JsonValueKind.Object)
            throw AppServerErrors.InvalidParams("'model' is required.");

        if (!TryGetCaseInsensitiveProperty(msg.Params.Value, "model", out var modelEl))
            throw AppServerErrors.InvalidParams("'model' is required.");

        var model = modelEl.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => modelEl.GetString(),
            _ => throw AppServerErrors.InvalidParams("'model' must be a string or null.")
        };

        var normalizedModel = NormalizeWorkspaceModel(model);
        SaveWorkspaceModelConfig(workspaceCraftPath, normalizedModel);
        return Task.FromResult<object?>(new WorkspaceConfigUpdateResult
        {
            Model = normalizedModel
        });
    }

    // -------------------------------------------------------------------------
    // cron/* methods (spec Section 16)
    // -------------------------------------------------------------------------

    private Task<object?> HandleCronListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(AppServerMethods.CronList);
        var p = GetParams<CronListParams>(msg);
        var jobs = cronService.ListJobs(includeDisabled: p.IncludeDisabled);
        return Task.FromResult<object?>(new CronListResult
        {
            Jobs = jobs.Select(MapCronJob).ToList()
        });
    }

    private Task<object?> HandleCronRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(AppServerMethods.CronRemove);
        var p = GetParams<CronRemoveParams>(msg);
        if (string.IsNullOrWhiteSpace(p.JobId))
            throw AppServerErrors.InvalidParams("'jobId' is required.");
        var removed = cronService.RemoveJob(p.JobId);
        if (!removed) throw AppServerErrors.CronJobNotFound(p.JobId);
        _broadcastCronStateChanged?.Invoke(new CronJobWireInfo { Id = p.JobId }, true);
        return Task.FromResult<object?>(new CronRemoveResult { Removed = true });
    }

    private Task<object?> HandleCronEnableAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (cronService == null) throw AppServerErrors.MethodNotFound(AppServerMethods.CronEnable);
        var p = GetParams<CronEnableParams>(msg);
        if (string.IsNullOrWhiteSpace(p.JobId))
            throw AppServerErrors.InvalidParams("'jobId' is required.");
        var job = cronService.EnableJob(p.JobId, p.Enabled);
        if (job == null) throw AppServerErrors.CronJobNotFound(p.JobId);
        _broadcastCronStateChanged?.Invoke(MapCronJob(job), false);
        return Task.FromResult<object?>(new CronEnableResult { Job = MapCronJob(job) });
    }

    private static CronJobWireInfo MapCronJob(CronJob job) => CronJobWireMapping.ToWire(job);

    // ── heartbeat/trigger (spec Section 17.2) ────────────────────────────────

    private async Task<object?> HandleHeartbeatTriggerAsync(
        AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (heartbeatService == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.HeartbeatTrigger);

        try
        {
            var result = await heartbeatService.TriggerNowAsync();
            return new HeartbeatTriggerResult { Result = result };
        }
        catch (Exception ex)
        {
            return new HeartbeatTriggerResult { Error = ex.Message };
        }
    }

    // ── skills/* (spec Section 18) ───────────────────────────────────────────

    private Task<object?> HandleSkillsListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (skillsLoader == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.SkillsList);
        var p = GetParams<SkillsListParams>(msg);
        var includeUnavailable = p.IncludeUnavailable ?? true;
        var list = skillsLoader.ListSkills(filterUnavailable: !includeUnavailable);
        var wires = list.Select(MapSkillToWire).ToList();
        return Task.FromResult<object?>(new SkillsListResult { Skills = wires });
    }

    private Task<object?> HandleSkillsReadAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (skillsLoader == null)
            throw AppServerErrors.MethodNotFound(AppServerMethods.SkillsRead);
        var p = GetParams<SkillsReadParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");
        var content = skillsLoader.LoadSkill(p.Name);
        if (content == null)
            throw AppServerErrors.SkillNotFound(p.Name);
        var metadata = skillsLoader.GetSkillMetadata(p.Name);
        return Task.FromResult<object?>(new SkillsReadResult
        {
            Name = p.Name,
            Content = content,
            Metadata = metadata
        });
    }

    private Task<object?> HandleSkillsSetEnabledAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (skillsLoader == null || string.IsNullOrEmpty(workspaceCraftPath))
            throw AppServerErrors.MethodNotFound(AppServerMethods.SkillsSetEnabled);
        var p = GetParams<SkillsSetEnabledParams>(msg);
        if (string.IsNullOrWhiteSpace(p.Name))
            throw AppServerErrors.InvalidParams("'name' is required.");

        var all = skillsLoader.ListSkills(filterUnavailable: false);
        if (all.All(s => !string.Equals(s.Name, p.Name, StringComparison.OrdinalIgnoreCase)))
            throw AppServerErrors.SkillNotFound(p.Name);

        var disabled = all.Where(s => !s.Enabled).Select(s => s.Name).ToList();
        if (p.Enabled)
            disabled.RemoveAll(n => string.Equals(n, p.Name, StringComparison.OrdinalIgnoreCase));
        else if (!disabled.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
            disabled.Add(p.Name);

        SkillsConfigPersistence.WriteWorkspaceDisabledSkills(workspaceCraftPath, disabled);
        skillsLoader.SetDisabledSkills(disabled);

        var updated = skillsLoader.ListSkills(filterUnavailable: false)
            .First(s => string.Equals(s.Name, p.Name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<object?>(new SkillsSetEnabledResult { Skill = MapSkillToWire(updated) });
    }

    private Task<object?> HandleCommandListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        _ = ct;
        var p = GetParams<CommandListParams>(msg);

        Language? overrideLanguage = p.Language?.ToLowerInvariant() switch
        {
            "zh" => Language.Chinese,
            "en" => Language.English,
            _ => null
        };

        var commands = _commandRegistry.ListCommands(language: overrideLanguage)
            .Where(c =>
            {
                var reg = _commandRegistry.GetRegistration(c.Name);
                return reg == null || IsServiceAvailableForRegistration(reg);
            })
            .Select(c => new CommandInfoWire
            {
                Name = c.Name,
                Aliases = c.Aliases,
                Description = c.Description,
                Category = c.Category,
                RequiresAdmin = c.RequiresAdmin
            })
            .ToList();

        return Task.FromResult<object?>(new CommandListResult { Commands = commands });
    }

    private async Task<object?> HandleCommandExecuteAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<CommandExecuteParams>(msg);
        if (string.IsNullOrWhiteSpace(p.ThreadId))
            throw AppServerErrors.InvalidParams("'threadId' is required.");
        if (string.IsNullOrWhiteSpace(p.Command))
            throw AppServerErrors.InvalidParams("'command' is required.");

        var commandName = ExtractCommandName(p.Command);
        var registration = _commandRegistry.GetRegistration(commandName);

        if (registration != null && !IsSenderAllowed(registration, p.Sender))
            throw AppServerErrors.CommandPermissionDenied(commandName);

        if (registration != null && !IsServiceAvailableForRegistration(registration))
            throw AppServerErrors.CommandServiceUnavailable(commandName);

        var thread = await sessionService.GetThreadAsync(p.ThreadId, ct);
        var rawText = BuildRawText(p.Command, p.Arguments);
        var senderId = p.Sender?.SenderId ?? connection.ClientInfo?.Name ?? "anonymous";
        var senderName = p.Sender?.SenderName ?? connection.ClientInfo?.Name ?? "anonymous";
        var source = string.IsNullOrWhiteSpace(thread.OriginChannel)
            ? (connection.IsChannelAdapter ? connection.ChannelAdapterName ?? "external" : "appserver")
            : thread.OriginChannel;

        var context = new CommandContext
        {
            SessionId = p.ThreadId,
            RawText = rawText,
            UserId = senderId,
            UserName = senderName,
            IsAdmin = string.Equals(p.Sender?.SenderRole, "admin", StringComparison.OrdinalIgnoreCase),
            Source = source,
            GroupId = p.Sender?.GroupId,
            ChannelContext = thread.ChannelContext,
            WorkspacePath = thread.WorkspacePath,
            SessionService = sessionService,
            HeartbeatService = heartbeatService,
            CronService = cronService,
            CommandRegistry = _commandRegistry
        };

        var responder = new BufferedCommandResponder();
        var result = await _commandRegistry.TryExecuteAsync(rawText, context, responder);
        return new CommandExecuteResult
        {
            Handled = result.Handled,
            Message = responder.Message ?? result.Message,
            IsMarkdown = responder.IsMarkdown || result.IsMarkdown,
            ExpandedPrompt = result.ExpandedPrompt
        };
    }

    private SkillInfoWire MapSkillToWire(SkillsLoader.SkillInfo s)
    {
        var metadata = skillsLoader!.GetSkillMetadata(s.Name);
        return new SkillInfoWire
        {
            Name = s.Name,
            Description = skillsLoader.GetSkillDescription(s.Name),
            Source = s.Source,
            Available = s.Available,
            UnavailableReason = s.UnavailableReason,
            Enabled = s.Enabled,
            Path = s.Path,
            Metadata = metadata
        };
    }

    private bool IsServiceAvailableForRegistration(CommandRegistration registration)
    {
        return registration.RequiredService?.ToLowerInvariant() switch
        {
            "cron" => cronService != null,
            "heartbeat" => heartbeatService != null,
            _ => true
        };
    }

    private static bool IsSenderAllowed(CommandRegistration registration, SenderContext? sender)
    {
        if (!registration.RequiresAdmin)
            return true;
        return string.Equals(sender?.SenderRole, "admin", StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractCommandName(string rawCommand)
    {
        var trimmed = rawCommand.Trim();
        if (trimmed.Length == 0)
            return rawCommand;

        var whitespaceIndex = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        return whitespaceIndex >= 0 ? trimmed[..whitespaceIndex] : trimmed;
    }

    private static string BuildRawText(string command, List<string>? arguments)
    {
        var normalized = command.StartsWith('/') ? command : $"/{command}";
        if (arguments == null || arguments.Count == 0)
            return normalized;
        return $"{normalized} {string.Join(" ", arguments)}";
    }

    private sealed class BufferedCommandResponder : ICommandResponder
    {
        private readonly List<(string Text, bool IsMarkdown)> _segments = [];

        /// <summary>
        /// All non-empty segments joined with newlines, or null if nothing was sent.
        /// </summary>
        public string? Message
        {
            get
            {
                var parts = _segments
                    .Where(s => !string.IsNullOrWhiteSpace(s.Text))
                    .Select(s => s.Text)
                    .ToList();
                return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
            }
        }

        /// <summary>
        /// True if any segment was sent as markdown.
        /// </summary>
        public bool IsMarkdown => _segments.Any(s => s.IsMarkdown);

        public Task SendTextAsync(string message)
        {
            _segments.Add((message, false));
            return Task.CompletedTask;
        }

        public Task SendMarkdownAsync(string markdown)
        {
            _segments.Add((markdown, true));
            return Task.CompletedTask;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // Fix 10: Shared HttpClient for image URL fetch (best-effort, text-only is the primary path).
    private static readonly HttpClient ImageHttpClient = new();

    /// <summary>
    /// Converts wire input parts to <see cref="AIContent"/>, resolving image and localImage parts
    /// to <see cref="DataContent"/> by reading file bytes or fetching URL bytes.
    /// Falls back to a text placeholder on any I/O error so the turn is not blocked.
    /// </summary>
    private static async Task<List<AIContent>> ResolveInputPartsAsync(
        List<SessionWireInputPart> parts,
        CancellationToken ct)
    {
        var result = new List<AIContent>(parts.Count);
        foreach (var part in parts)
        {
            AIContent content;
            switch (part.Type)
            {
                case "localImage" when part.Path is { } path:
                    content = await ResolveLocalImageAsync(path, ct);
                    break;
                case "image" when part.Url is { } url:
                    content = await ResolveRemoteImageAsync(url, ct);
                    break;
                default:
                    content = part.ToAIContent();
                    break;
            }
            result.Add(content);
        }
        return result;
    }

    private static async Task<AIContent> ResolveLocalImageAsync(string path, CancellationToken ct)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var mediaType = InferMediaType(path);
            return new DataContent(bytes, mediaType);
        }
        catch
        {
            // Best-effort: return placeholder if file cannot be read
            return new TextContent($"[localImage:{path}]");
        }
    }

    private static async Task<AIContent> ResolveRemoteImageAsync(string url, CancellationToken ct)
    {
        try
        {
            // Security: Validate URL scheme to prevent SSRF attacks
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return new TextContent($"[image:invalid-url]");
            }

            // Only allow http and https schemes to prevent file://, ftp://, etc.
            if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                return new TextContent($"[image:blocked-scheme:{uri.Scheme}]");
            }

            // Block requests to localhost/internal networks (basic SSRF protection)
            if (IsInternalAddress(uri.Host))
            {
                return new TextContent($"[image:blocked-internal]");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var response = await ImageHttpClient.GetAsync(url, cts.Token);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
            var mediaType = response.Content.Headers.ContentType?.MediaType
                ?? InferMediaType(url);
            return new DataContent(bytes, mediaType);
        }
        catch
        {
            // Best-effort: return placeholder if URL cannot be fetched
            return new TextContent($"[image:{url}]");
        }
    }

    /// <summary>
    /// Checks if a host points to an internal/reserved address.
    /// This is a basic SSRF protection - blocks localhost, loopback, and private ranges.
    /// </summary>
    private static bool IsInternalAddress(string host)
    {
        // Block localhost variants
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            host == "127.0.0.1" ||
            host == "::1" ||
            host.StartsWith("127.", StringComparison.Ordinal) ||
            host.StartsWith("0.", StringComparison.Ordinal))
        {
            return true;
        }

        // Block common internal hostnames
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
            host.Contains("localhost"))
        {
            return true;
        }

        // Try to parse as IP address and check for private ranges
        if (System.Net.IPAddress.TryParse(host, out var ip))
        {
            var bytes = ip.GetAddressBytes();

            // IPv4 private ranges
            if (bytes.Length == 4)
            {
                // 10.0.0.0/8
                if (bytes[0] == 10) return true;
                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168) return true;
                // 169.254.0.0/16 (link-local)
                if (bytes[0] == 169 && bytes[1] == 254) return true;
            }

            // IPv6 loopback and link-local
            if (bytes.Length == 16)
            {
                // ::1 (loopback)
                if (bytes.AsSpan().ToArray().All(b => b == 0) && bytes[15] == 1) return true;
                // fe80::/10 (link-local)
                if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80) return true;
            }
        }

        return false;
    }

    private static string InferMediaType(string pathOrUrl)
    {
        var ext = Path.GetExtension(pathOrUrl).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
    }

    // -------------------------------------------------------------------------
    // Domain exception → wire error code translation (Gap A, spec Section 8.3)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Translates an <see cref="InvalidOperationException"/> from the domain layer into the
    /// appropriate <see cref="AppServerException"/> with a spec-defined error code.
    /// </summary>
    private static AppServerException MapOperationException(InvalidOperationException ex)
    {
        var msg = ex.Message;
        var id = ExtractQuotedId(msg);

        if (msg.Contains("archived and cannot be resumed") || msg.Contains("is not Active"))
            return AppServerErrors.ThreadNotActive(id);

        if (msg.Contains("already has a running Turn"))
            return AppServerErrors.TurnInProgress(id);

        // historyMode contract violations are caller errors → InvalidParams (-32602)
        if (msg.Contains("client-managed history") || msg.Contains("server-managed history"))
            return AppServerErrors.InvalidParams(msg);

        return AppServerErrors.InternalError(msg);
    }

    /// <summary>
    /// Extracts the first single-quoted identifier from an exception message.
    /// For example: "Thread 'thread_001' not found." → "thread_001".
    /// </summary>
    private static string ExtractQuotedId(string message)
    {
        var start = message.IndexOf('\'');
        if (start < 0) return string.Empty;
        var end = message.IndexOf('\'', start + 1);
        return end > start ? message[(start + 1)..end] : string.Empty;
    }

    private static string? NormalizeOptionalString(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeWorkspaceModel(string? rawModel)
    {
        var trimmed = rawModel?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            string.Equals(trimmed, "default", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return trimmed;
    }

    private static void SaveWorkspaceModelConfig(string workspaceCraftPath, string? model)
    {
        var configPath = Path.Combine(workspaceCraftPath, "config.json");
        Directory.CreateDirectory(workspaceCraftPath);
        var root = LoadWorkspaceConfigObject(configPath);
        var modelKey = FindCaseInsensitiveKey(root, "Model");

        if (string.IsNullOrWhiteSpace(model))
        {
            if (modelKey != null)
                root.Remove(modelKey);
        }
        else
        {
            root[modelKey ?? "Model"] = model;
        }

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, $"{json}{Environment.NewLine}", new UTF8Encoding(false));
    }

    private static JsonObject LoadWorkspaceConfigObject(string configPath)
    {
        if (!File.Exists(configPath))
            return new JsonObject();

        try
        {
            var node = JsonNode.Parse(File.ReadAllText(configPath));
            return node as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static string? FindCaseInsensitiveKey(JsonObject obj, string expectedKey)
    {
        foreach (var kv in obj)
        {
            if (string.Equals(kv.Key, expectedKey, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        }

        return null;
    }

    private static bool TryGetCaseInsensitiveProperty(JsonElement obj, string expectedName, out JsonElement value)
    {
        if (obj.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private Task<object?> TryHandleExtensionAsync(string method, AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (!_extensionMethods.TryGetValue(method, out var handler))
            throw AppServerErrors.MethodNotFound(method);

        return handler.HandleAsync(
            msg,
            new AppServerExtensionContext(
                connection,
                transport,
                workspaceCraftPath,
                _hostWorkspacePath,
                ct));
    }

    private static IReadOnlyDictionary<string, IAppServerMethodHandler> BuildExtensionMethodMap(
        IEnumerable<IAppServerProtocolExtension>? protocolExtensions)
    {
        if (protocolExtensions == null)
            return new Dictionary<string, IAppServerMethodHandler>(StringComparer.Ordinal);

        var methods = new Dictionary<string, IAppServerMethodHandler>(StringComparer.Ordinal);
        foreach (var extension in protocolExtensions)
        {
            foreach (var method in extension.Methods)
            {
                if (string.IsNullOrWhiteSpace(method))
                    throw new InvalidOperationException("AppServer extension method names must be non-empty.");
                if (ReservedMethodNames.Contains(method))
                    throw new InvalidOperationException($"AppServer extension method '{method}' conflicts with a Core protocol method.");
                if (!methods.TryAdd(method, extension))
                    throw new InvalidOperationException($"Duplicate AppServer extension method registration: '{method}'.");
            }
        }

        return methods;
    }

    private Task<object?> RouteAutomation(Func<IAutomationsRequestHandler, Task<object?>> action)
    {
        if (automationsHandler == null)
            throw AppServerErrors.MethodNotFound("automation/*");
        return action(automationsHandler);
    }

    private static T GetParams<T>(AppServerIncomingMessage msg) where T : new()
    {
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind == JsonValueKind.Null)
            return new T();

        try
        {
            return JsonSerializer.Deserialize<T>(
                msg.Params.Value.GetRawText(),
                SessionWireJsonOptions.Default) ?? new T();
        }
        catch (JsonException ex)
        {
            throw AppServerErrors.InvalidParams($"Failed to deserialize params: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a JSON-RPC response followed immediately by a notification on the same connection.
    /// Used by thread lifecycle handlers (Fix 8) to guarantee response-before-notification ordering.
    /// The caller must return null from its handle method to signal the host that the response
    /// has already been sent.
    /// </summary>
    private async Task SendNotificationAfterResponseAsync(
        JsonElement? requestId,
        object responseResult,
        string notificationMethod,
        object notificationParams,
        CancellationToken ct)
    {
        await transport.WriteMessageAsync(BuildResponse(requestId, responseResult), ct);
        await transport.WriteMessageAsync(new
        {
            jsonrpc = "2.0",
            method = notificationMethod,
            @params = notificationParams
        }, ct);
    }

    /// <summary>
    /// Builds a standard JSON-RPC 2.0 success response.
    /// </summary>
    public static object BuildResponse(JsonElement? id, object? result) => new
    {
        jsonrpc = "2.0",
        id,
        result
    };

    /// <summary>
    /// Builds a standard JSON-RPC 2.0 error response.
    /// </summary>
    public static object BuildErrorResponse(JsonElement? id, AppServerError error) => new
    {
        jsonrpc = "2.0",
        id,
        error
    };
}
