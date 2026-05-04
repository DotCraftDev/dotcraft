using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Hooks;
using DotCraft.Memory;
using DotCraft.Mcp;
using DotCraft.Plugins;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Logging;
using DotCraft.Tools;
using DotCraft.Tools.BackgroundTerminals;
using DotCraft.Tracing;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Protocol;

/// <summary>
/// Composite dictionary key that uniquely identifies a Turn across all Threads.
/// Turn IDs (e.g. <c>turn_001</c>) are only unique within a Thread; this struct
/// pairs them with the parent Thread ID so they can safely key concurrent dictionaries.
/// </summary>
internal readonly record struct TurnKey(string ThreadId, string TurnId);

/// <summary>
/// Session Core implementation. Manages Thread/Turn/Item lifecycle, orchestrates agent
/// execution, emits the structured event stream, and delegates persistence to SessionPersistenceService.
/// </summary>
public sealed class SessionService(
    AgentFactory agentFactory,
    AIAgent defaultAgent,
    SessionPersistenceService persistence,
    SessionGate sessionGate,
    IChannelRuntimeToolProvider? channelRuntimeToolProvider = null,
    HookRunner? hookRunner = null,
    TraceCollector? traceCollector = null,
    TokenUsageStore? tokenUsageStore = null,
    TimeSpan? approvalTimeout = null,
    ILogger<SessionService>? logger = null,
    ApprovalStore? approvalStore = null,
    IToolProfileRegistry? toolProfileRegistry = null,
    SessionStreamDebugLogger? sessionStreamDebugLogger = null,
    IBackgroundTerminalService? backgroundTerminalService = null,
    IAppConfigMonitor? appConfigMonitor = null)
    : ISessionService, IThreadAgentRefreshService, ISubAgentSyntheticTurnService
{
    private readonly TimeSpan _approvalTimeout = approvalTimeout ?? TimeSpan.FromMinutes(5);

    // In-memory state
    private readonly ConcurrentDictionary<string, SessionThread> _threads = new();
    private readonly ConcurrentDictionary<string, AIAgent> _threadAgents = new();
    private readonly ConcurrentDictionary<TurnKey, SessionApprovalService> _pendingApprovals = new();
    private readonly ConcurrentDictionary<TurnKey, CancellationTokenSource> _runningTurns = new();
    private readonly ConcurrentDictionary<string, McpClientManager> _threadMcpManagers = new();
    private readonly ConcurrentDictionary<string, AgentModeManager> _threadModeManagers = new();
    private readonly ConcurrentDictionary<string, ThreadEventBroker> _threadEventBrokers = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _threadQueueLocks = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _threadAgentLocks = new();
    private readonly ConcurrentDictionary<string, byte> _materializedThreads = new();
    private readonly ConcurrentDictionary<string, int> _turnsSinceConsolidation = new();
    private readonly ConcurrentDictionary<string, byte> _threadsPendingPermanentDeletion = new();
    private readonly ConcurrentDictionary<string, IReadOnlySet<string>> _threadPluginFunctionToolNames = new();
    private static readonly IReadOnlySet<string> EmptyPluginFunctionToolNames = new HashSet<string>(StringComparer.Ordinal);
    private static readonly HttpClient QueuedInputHttpClient = new();
    private readonly IAppConfigMonitor? _appConfigMonitor = appConfigMonitor;
    private volatile bool _forcePerThreadAgents;

    /// <inheritdoc />
    public Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string>? ThreadDeletedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string, ThreadStatus, ThreadStatus>? ThreadStatusChangedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string, SessionThreadRuntimeSignal>? ThreadRuntimeSignalForBroadcast { get; set; }

    /// <summary>
    /// Optional hook invoked after a session-backed SubAgent edge is created or changes status.
    /// Hosts broadcast <c>subagent/graphChanged</c> so clients can refresh child thread metadata.
    /// </summary>
    public Action<string, string>? SubAgentGraphChangedForBroadcast { get; set; }

    private ApprovalPolicy ResolveApprovalPolicy(ApprovalPolicy threadPolicy)
    {
        if (threadPolicy != ApprovalPolicy.Default)
            return threadPolicy;

        return _appConfigMonitor?.Current.Permissions.DefaultApprovalPolicy ?? ApprovalPolicy.Default;
    }

    /// <inheritdoc />
    public ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            return null;

        var tokens = persistence.LoadContextUsageTokens(threadId);
        return tokens is null ? null : CreateContextUsageSnapshot(tokens.Value);
    }

    private ContextUsageSnapshot CreateContextUsageSnapshot(long tokens)
    {
        var pipeline = agentFactory.CompactionPipeline;
        var threshold = pipeline.EvaluateThreshold(tokens);

        return new ContextUsageSnapshot
        {
            Tokens = threshold.Tokens,
            ContextWindow = pipeline.EffectiveContextWindow,
            AutoCompactThreshold = threshold.AutoThreshold,
            WarningThreshold = threshold.WarningThreshold,
            ErrorThreshold = threshold.ErrorThreshold,
            PercentLeft = threshold.PercentLeft
        };
    }

    private async Task<ContextUsageSnapshot> SaveContextUsageSnapshotAsync(
        string threadId,
        long tokens,
        CancellationToken ct = default)
    {
        var normalizedTokens = Math.Max(0, tokens);
        await persistence.SaveContextUsageTokensAsync(threadId, normalizedTokens, ct);
        return CreateContextUsageSnapshot(normalizedTokens);
    }

    // =========================================================================
    // Thread lifecycle
    // =========================================================================

    /// <inheritdoc/>
    public async Task<SessionThread> CreateThreadAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? threadId = null,
        string? displayName = null,
        CancellationToken ct = default,
        ThreadSource? source = null)
    {
        var thread = new SessionThread
        {
            Id = threadId ?? SessionIdGenerator.NewThreadId(),
            WorkspacePath = identity.WorkspacePath,
            UserId = identity.UserId,
            OriginChannel = identity.ChannelName,
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            HistoryMode = historyMode,
            Configuration = config,
            DisplayName = displayName,
            Source = source ?? ThreadSource.User()
        };

        if (identity.ChannelContext != null)
        {
            thread.ChannelContext = identity.ChannelContext;
            thread.Metadata["channelContext"] = identity.ChannelContext;
        }

        _threadsPendingPermanentDeletion.TryRemove(thread.Id, out _);
        _threads[thread.Id] = thread;
        var broker = GetOrCreateBroker(thread.Id);

        // Create a per-thread agent when custom configuration is provided or when
        // runtime external channel tools may need thread-scoped injection.
        if (config != null || channelRuntimeToolProvider != null)
        {
            using (await AcquireThreadAgentLockAsync(thread.Id, ct))
                _threadAgents[thread.Id] = await BuildAgentForThreadAsync(thread, ct);
        }

        await PersistThreadWithMaterializationAsync(thread, ct);
        await SaveContextUsageSnapshotAsync(thread.Id, 0, ct);

        broker.PublishThreadEvent(SessionEventType.ThreadCreated, thread);
        ThreadCreatedForBroadcast?.Invoke(thread);

        return thread;
    }

    /// <inheritdoc/>
    public async Task<ThreadResetResult> ResetConversationAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? displayName = null,
        CancellationToken ct = default)
    {
        var summaries = await FindThreadsAsync(identity, includeArchived: false, crossChannelOrigins: null, ct);
        var archivedIds = new List<string>();
        foreach (var summary in summaries.Where(s => s.Status is ThreadStatus.Active or ThreadStatus.Paused))
        {
            await ArchiveThreadAsync(summary.Id, ct);
            archivedIds.Add(summary.Id);
        }

        var thread = await CreateThreadAsync(identity, config, historyMode, displayName: displayName, ct: ct);
        return new ThreadResetResult
        {
            Thread = thread,
            ArchivedThreadIds = archivedIds,
            CreatedLazily = true
        };
    }

    /// <inheritdoc/>
    public async Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default)
    {
        if (_threads.TryGetValue(threadId, out var cached))
        {
            if (cached.Status == ThreadStatus.Archived)
                throw new InvalidOperationException($"Thread '{threadId}' is archived and cannot be resumed.");

            if (cached.Status != ThreadStatus.Active)
            {
                var previousStatus = cached.Status;
                cached.Status = ThreadStatus.Active;
                cached.LastActiveAt = DateTimeOffset.UtcNow;
                await PersistThreadIfMaterializedAsync(cached, ct);
                GetOrCreateBroker(threadId).PublishThreadStatusChanged(previousStatus, cached.Status);
            }

            await EnsurePerThreadAgentIfMissingAsync(threadId, cached, ct);

            var resumedByChannel = ChannelSessionScope.Current?.Channel ?? cached.OriginChannel;
            GetOrCreateBroker(threadId).PublishThreadEvent(SessionEventType.ThreadResumed,
                new ThreadResumedPayload { Thread = cached, ResumedBy = resumedByChannel });
            return cached;
        }

        var thread = await persistence.LoadThreadAsync(threadId, ct)
            ?? throw new KeyNotFoundException($"Thread '{threadId}' not found.");

        if (thread.Status == ThreadStatus.Archived)
            throw new InvalidOperationException($"Thread '{threadId}' is archived and cannot be resumed.");

        thread.Status = ThreadStatus.Active;
        thread.LastActiveAt = DateTimeOffset.UtcNow;

        _threads[thread.Id] = thread;
        var broker = GetOrCreateBroker(thread.Id);

        await EnsurePerThreadAgentIfMissingAsync(thread.Id, thread, ct);

        await PersistThreadWithMaterializationAsync(thread, ct);
        var resumedBy = ChannelSessionScope.Current?.Channel ?? thread.OriginChannel;
        broker.PublishThreadEvent(SessionEventType.ThreadResumed,
            new ThreadResumedPayload { Thread = thread, ResumedBy = resumedBy });

        return thread;
    }

    /// <inheritdoc/>
    public async Task PauseThreadAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        if (thread.Status == ThreadStatus.Paused) return;
        var previousStatus = thread.Status;
        thread.Status = ThreadStatus.Paused;
        await PersistThreadStatusAsync(thread, ct);
        GetOrCreateBroker(threadId).PublishThreadStatusChanged(previousStatus, thread.Status);
    }

    /// <inheritdoc/>
    public async Task ArchiveThreadAsync(string threadId, CancellationToken ct = default)
    {
        var root = await GetOrLoadThreadAsync(threadId, ct);
        ThrowIfDirectSubAgentLifecycleOperation(root, "archive");

        foreach (var id in await CollectSubAgentSubtreeIdsAsync(root.Id, ct))
        {
            var thread = await GetOrLoadThreadAsync(id, ct);
            await ArchiveThreadCoreAsync(thread, ct);
        }
    }

    /// <inheritdoc/>
    public async Task UnarchiveThreadAsync(string threadId, CancellationToken ct = default)
    {
        var root = await GetOrLoadThreadAsync(threadId, ct);
        ThrowIfDirectSubAgentLifecycleOperation(root, "unarchive");

        foreach (var id in await CollectSubAgentSubtreeIdsAsync(root.Id, ct))
        {
            var thread = await GetOrLoadThreadAsync(id, ct);
            await UnarchiveThreadCoreAsync(thread, ct);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default)
    {
        var normalizedThreadId = threadId.Trim();
        if (normalizedThreadId.Length == 0)
            throw new ArgumentException("threadId is required.", nameof(threadId));

        var root = await GetOrLoadThreadAsync(normalizedThreadId, ct);
        ThrowIfDirectSubAgentLifecycleOperation(root, "delete");
        var subtreeIds = await CollectSubAgentSubtreeIdsAsync(normalizedThreadId, ct);
        var deleteOrder = subtreeIds.Reverse().ToList();
        foreach (var id in deleteOrder)
            _threadsPendingPermanentDeletion[id] = 0;

        try
        {
            foreach (var id in deleteOrder)
                await DeleteThreadCoreAsync(id, ct);
        }
        catch
        {
            foreach (var id in deleteOrder)
                _threadsPendingPermanentDeletion.TryRemove(id, out _);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        bool includeArchived = false,
        IReadOnlyList<string>? crossChannelOrigins = null,
        CancellationToken ct = default,
        bool includeSubAgents = false)
    {
        var all = await persistence.LoadIndexAsync(ct);
        var hasCross = crossChannelOrigins is { Count: > 0 };
        var mergedById = new Dictionary<string, ThreadSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var summary in all)
            mergedById[summary.Id] = summary;
        foreach (var thread in _threads.Values)
            mergedById[thread.Id] = ThreadSummary.FromThread(thread);
        var merged = mergedById.Values.ToList();

        return merged
            .Where(s =>
            {
                if (!(includeArchived || s.Status != ThreadStatus.Archived))
                    return false;
                if (!string.Equals(s.WorkspacePath, identity.WorkspacePath, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!includeSubAgents && IsSubAgentSummary(s))
                    return false;
                if (includeSubAgents
                    && IsSubAgentSummary(s)
                    && IsHiddenByArchivedParent(s, mergedById, includeArchived))
                    return false;
                if (includeSubAgents
                    && IsSubAgentSummary(s)
                    && (identity.UserId == null || s.UserId == identity.UserId))
                    return true;

                // userId and channelContext apply together for the native identity path only.
                // Cron/heartbeat threads use synthetic userIds (e.g. cron:jobId) while Desktop uses local;
                // they are included only via crossChannelOrigins (workspace + originChannel).
                var identityMatch =
                    (identity.UserId == null || s.UserId == identity.UserId)
                    && (identity.ChannelContext == null
                        ? s.ChannelContext == null
                        : s.ChannelContext == identity.ChannelContext);

                if (identityMatch)
                    return true;

                if (!hasCross)
                    return false;

                return OriginChannelInList(s.OriginChannel, crossChannelOrigins!);
            })
            .OrderByDescending(s => s.LastActiveAt)
            .ToList();
    }

    public async Task UpsertThreadSpawnEdgeAsync(ThreadSpawnEdge edge, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await persistence.UpsertThreadSpawnEdgeAsync(edge, ct);
        SubAgentGraphChangedForBroadcast?.Invoke(edge.ParentThreadId, edge.ChildThreadId);
    }

    public async Task SetThreadSpawnEdgeStatusAsync(
        string parentThreadId,
        string childThreadId,
        string status,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await persistence.SetThreadSpawnEdgeStatusAsync(parentThreadId, childThreadId, status, ct);
        SubAgentGraphChangedForBroadcast?.Invoke(parentThreadId, childThreadId);
    }

    public Task<IReadOnlyList<ThreadSpawnEdge>> ListSubAgentChildrenAsync(
        string parentThreadId,
        bool includeClosed = false,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return persistence.ListSubAgentChildrenAsync(parentThreadId, includeClosed, ct);
    }

    public async Task<SessionTurn> StartSubAgentSyntheticTurnAsync(
        string threadId,
        IList<AIContent> content,
        string runtimeType,
        string? profileName,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        if (thread.Status != ThreadStatus.Active)
            throw new InvalidOperationException($"Thread '{threadId}' is not Active (current status: {thread.Status}). Cannot submit input.");

        if (thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval))
            throw new InvalidOperationException($"Thread '{threadId}' already has a running Turn. Wait for it to complete or cancel it first.");

        var channelInfo = ChannelSessionScope.Current;
        var turnOriginChannel = channelInfo?.Channel ?? thread.OriginChannel;
        var turnChannelContext = channelInfo?.DefaultDeliveryTarget ?? thread.ChannelContext;
        var text = string.Concat(content.OfType<TextContent>().Select(t => t.Text));
        var turn = new SessionTurn
        {
            Id = SessionIdGenerator.NewTurnId(thread.Turns.Count + 1),
            ThreadId = threadId,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            OriginChannel = turnOriginChannel,
            Initiator = new TurnInitiatorContext
            {
                ChannelName = turnOriginChannel,
                UserId = channelInfo?.UserId ?? thread.UserId,
                ChannelContext = turnChannelContext,
                GroupId = channelInfo?.GroupId
            }
        };

        var userItem = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(1),
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new UserMessagePayload
            {
                Text = text,
                ChannelName = turnOriginChannel,
                ChannelContext = turnChannelContext,
                GroupId = channelInfo?.GroupId
            }
        };

        turn.Input = userItem;
        turn.Items.Add(userItem);
        thread.Turns.Add(turn);
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        thread.Metadata["subagent.syntheticRuntime"] = runtimeType;
        if (!string.IsNullOrWhiteSpace(profileName))
            thread.Metadata["subagent.profileName"] = profileName;

        var broker = GetOrCreateBroker(threadId);
        broker.PublishTurnStarted(turn);
        ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnStarted);
        broker.PublishItemEvent(SessionEventType.ItemStarted, turn.Id, userItem);
        broker.PublishItemEvent(SessionEventType.ItemCompleted, turn.Id, userItem);
        await PersistThreadWithMaterializationAsync(thread, ct);
        return turn;
    }

    public async Task<SessionTurn> CompleteSubAgentSyntheticTurnAsync(
        string threadId,
        string turnId,
        string text,
        bool isError,
        SubAgentTokenUsage? tokensUsed,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        var turn = thread.Turns.FirstOrDefault(t => string.Equals(t.Id, turnId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Turn '{turnId}' not found in thread '{threadId}'.");
        if (turn.Status is not (TurnStatus.Running or TurnStatus.WaitingApproval))
            return turn;

        var item = isError
            ? CreateErrorItem(turn, turn.Items.Count + 1, text, "subagent_error", fatal: true)
            : new SessionItem
            {
                Id = SessionIdGenerator.NewItemId(turn.Items.Count + 1),
                TurnId = turn.Id,
                Type = ItemType.AgentMessage,
                Status = ItemStatus.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                Payload = new AgentMessagePayload { Text = text }
            };

        turn.Items.Add(item);
        turn.Status = isError ? TurnStatus.Failed : TurnStatus.Completed;
        turn.CompletedAt = DateTimeOffset.UtcNow;
        turn.Error = isError ? text : null;
        if (tokensUsed != null)
        {
            turn.TokenUsage = new TokenUsageInfo
            {
                InputTokens = tokensUsed.InputTokens,
                OutputTokens = tokensUsed.OutputTokens,
                TotalTokens = tokensUsed.InputTokens + tokensUsed.OutputTokens
            };
        }

        thread.LastActiveAt = DateTimeOffset.UtcNow;
        var broker = GetOrCreateBroker(threadId);
        broker.PublishItemEvent(SessionEventType.ItemStarted, turn.Id, item);
        broker.PublishItemEvent(SessionEventType.ItemCompleted, turn.Id, item);
        if (isError)
        {
            broker.PublishTurnFailed(turn, text);
            ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnFailed);
        }
        else
        {
            broker.PublishTurnCompleted(turn);
            ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnCompleted);
        }

        await PersistThreadWithMaterializationAsync(thread, ct);
        return turn;
    }

    public async Task<SessionTurn> CancelSubAgentSyntheticTurnAsync(
        string threadId,
        string turnId,
        string reason,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        var turn = thread.Turns.FirstOrDefault(t => string.Equals(t.Id, turnId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Turn '{turnId}' not found in thread '{threadId}'.");
        if (turn.Status is not (TurnStatus.Running or TurnStatus.WaitingApproval))
            return turn;

        turn.Status = TurnStatus.Cancelled;
        turn.CompletedAt = DateTimeOffset.UtcNow;
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        var broker = GetOrCreateBroker(threadId);
        broker.PublishTurnCancelled(turn, reason);
        ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnCancelled);
        await PersistThreadWithMaterializationAsync(thread, ct);
        return turn;
    }

    private static bool IsSubAgentSummary(ThreadSummary summary) =>
        string.Equals(summary.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
        || string.Equals(summary.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase);

    private static bool IsSubAgentThread(SessionThread thread) =>
        string.Equals(thread.Source.Kind, ThreadSourceKinds.SubAgent, StringComparison.OrdinalIgnoreCase)
        || string.Equals(thread.OriginChannel, SubAgentThreadOrigin.ChannelName, StringComparison.OrdinalIgnoreCase);

    private static AgentControlToolAccess ResolveAgentControlToolAccess(SessionThread thread) =>
        thread.Configuration?.AgentControlToolAccess
        ?? (IsSubAgentThread(thread)
            ? AgentControlToolAccess.Disabled
            : AgentControlToolAccess.Full);

    private static string? GetSubAgentParentThreadId(SessionThread thread)
    {
        var sourceParent = thread.Source.SubAgent?.ParentThreadId?.Trim();
        if (!string.IsNullOrWhiteSpace(sourceParent))
            return sourceParent;
        var context = thread.ChannelContext?.Trim();
        return string.IsNullOrWhiteSpace(context) ? null : context;
    }

    private static string? GetSubAgentParentThreadId(ThreadSummary summary)
    {
        var sourceParent = summary.Source.SubAgent?.ParentThreadId?.Trim();
        if (!string.IsNullOrWhiteSpace(sourceParent))
            return sourceParent;
        var context = summary.ChannelContext?.Trim();
        return string.IsNullOrWhiteSpace(context) ? null : context;
    }

    private static bool IsHiddenByArchivedParent(
        ThreadSummary summary,
        IReadOnlyDictionary<string, ThreadSummary> summariesById,
        bool includeArchived)
    {
        if (includeArchived)
            return false;
        var parentId = GetSubAgentParentThreadId(summary);
        return !string.IsNullOrWhiteSpace(parentId)
            && summariesById.TryGetValue(parentId, out var parent)
            && parent.Status == ThreadStatus.Archived;
    }

    private static void ThrowIfDirectSubAgentLifecycleOperation(SessionThread thread, string operation)
    {
        if (!IsSubAgentThread(thread))
            return;
        var parentId = GetSubAgentParentThreadId(thread);
        if (string.IsNullOrWhiteSpace(parentId))
            return;
        throw new InvalidOperationException(
            $"SubAgent child thread '{thread.Id}' cannot be {operation}d directly; manage its parent thread '{parentId}' instead.");
    }

    private async Task<IReadOnlyList<string>> CollectSubAgentSubtreeIdsAsync(string rootThreadId, CancellationToken ct)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        async Task VisitAsync(string id)
        {
            ct.ThrowIfCancellationRequested();
            if (!seen.Add(id))
                return;

            result.Add(id);
            var children = await persistence.ListSubAgentChildrenAsync(id, includeClosed: true, ct);
            foreach (var child in children)
                await VisitAsync(child.ChildThreadId);
        }

        await VisitAsync(rootThreadId);
        return result;
    }

    private async Task ArchiveThreadCoreAsync(SessionThread thread, CancellationToken ct)
    {
        if (thread.Status == ThreadStatus.Archived)
            return;
        var previousStatus = thread.Status;
        thread.Status = ThreadStatus.Archived;
        _threadAgents.TryRemove(thread.Id, out _);
        _threadModeManagers.TryRemove(thread.Id, out _);
        _threadPluginFunctionToolNames.TryRemove(thread.Id, out _);
        if (backgroundTerminalService != null)
            await backgroundTerminalService.CleanThreadAsync(thread.Id, ct);
        await PersistThreadStatusAsync(thread, ct);
        PublishThreadStatusChanged(thread.Id, previousStatus, thread.Status);
    }

    private async Task UnarchiveThreadCoreAsync(SessionThread thread, CancellationToken ct)
    {
        if (thread.Status == ThreadStatus.Active)
            return;
        var previousStatus = thread.Status;
        thread.Status = ThreadStatus.Active;
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await PersistThreadStatusAsync(thread, ct);
        PublishThreadStatusChanged(thread.Id, previousStatus, thread.Status);
    }

    private async Task DeleteThreadCoreAsync(string threadId, CancellationToken ct)
    {
        if (_threads.TryGetValue(threadId, out var thread))
        {
            foreach (var turn in thread.Turns.Where(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval))
            {
                var key = new TurnKey(threadId, turn.Id);
                if (_runningTurns.TryRemove(key, out var turnCts))
                    await turnCts.CancelAsync();
                _pendingApprovals.TryRemove(key, out _);
            }
        }

        await persistence.DeleteThreadCascadeAsync(threadId, ct);

        _threads.TryRemove(threadId, out _);
        _threadAgents.TryRemove(threadId, out _);
        _threadModeManagers.TryRemove(threadId, out _);
        _threadEventBrokers.TryRemove(threadId, out _);
        if (_threadQueueLocks.TryRemove(threadId, out var queueLock))
            queueLock.Dispose();
        if (_threadAgentLocks.TryRemove(threadId, out var agentLock))
            agentLock.Dispose();
        _materializedThreads.TryRemove(threadId, out _);
        _turnsSinceConsolidation.TryRemove(threadId, out _);
        _threadPluginFunctionToolNames.TryRemove(threadId, out _);
        if (_threadMcpManagers.TryRemove(threadId, out var mcpManager))
            await mcpManager.DisposeAsync();
        if (backgroundTerminalService != null)
            await backgroundTerminalService.CleanThreadAsync(threadId, ct);

        _threadsPendingPermanentDeletion.TryRemove(threadId, out _);
        ThreadDeletedForBroadcast?.Invoke(threadId);
    }

    private void PublishThreadStatusChanged(string threadId, ThreadStatus previousStatus, ThreadStatus newStatus)
    {
        GetOrCreateBroker(threadId).PublishThreadStatusChanged(previousStatus, newStatus);
        ThreadStatusChangedForBroadcast?.Invoke(threadId, previousStatus, newStatus);
    }

    private static bool OriginChannelInList(string originChannel, IReadOnlyList<string> origins)
    {
        foreach (var o in origins)
        {
            if (string.Equals(o, originChannel, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(
        string threadId,
        bool replayRecent = false,
        CancellationToken ct = default)
    {
        var broker = GetOrCreateBroker(threadId);
        return broker.SubscribeAsync(replayRecent, ct);
    }

    /// <inheritdoc/>
    public async Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default) =>
        await GetOrLoadThreadAsync(threadId, ct);

    /// <inheritdoc/>
    public async Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        await EnsurePerThreadAgentIfMissingAsync(threadId, thread, ct);
        return thread;
    }

    // =========================================================================
    // Turn orchestration
    // =========================================================================

    /// <inheritdoc/>
    public IAsyncEnumerable<SessionEvent> SubmitInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        ChatMessage[]? messages = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null)
    {
        // This method returns immediately; execution happens in a background Task.
        // We use a SessionEventChannel to bridge the background task to the caller.
        var channel = StartTurnAsync(threadId, content, sender, messages, inputSnapshot, ct);
        return channel.ReadAllAsync(ct);
    }

    private SessionEventChannel StartTurnAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender,
        ChatMessage[]? messages,
        SessionInputSnapshot? inputSnapshot,
        CancellationToken callerCt)
    {
        // Step 1: Validate synchronously before starting the background Task
        if (!_threads.TryGetValue(threadId, out var thread))
            throw new KeyNotFoundException($"Thread '{threadId}' not found. Call CreateThreadAsync or ResumeThreadAsync first.");

        if (thread.Status != ThreadStatus.Active)
            throw new InvalidOperationException($"Thread '{threadId}' is not Active (current status: {thread.Status}). Cannot submit input.");

        if (thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval))
            throw new InvalidOperationException($"Thread '{threadId}' already has a running Turn. Wait for it to complete or cancel it first.");

        if (thread.HistoryMode == HistoryMode.Client && messages is not { Length: > 0 })
            throw new InvalidOperationException($"Thread '{threadId}' requires client-managed history, but no messages were provided.");

        if (thread.HistoryMode == HistoryMode.Server && messages is { Length: > 0 })
            throw new InvalidOperationException($"Thread '{threadId}' uses server-managed history and does not accept client-supplied messages.");

        var channelInfo = ChannelSessionScope.Current;
        var turnOriginChannel = channelInfo?.Channel ?? thread.OriginChannel;
        var turnChannelContext = channelInfo?.DefaultDeliveryTarget ?? thread.ChannelContext;
        var triggerInfo = TurnTriggerScope.Current;

        // Step 2: Create Turn and UserMessage Item
        var turnSeq = thread.Turns.Count + 1;
        var turn = new SessionTurn
        {
            Id = SessionIdGenerator.NewTurnId(turnSeq),
            ThreadId = threadId,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            OriginChannel = turnOriginChannel,
            Initiator = new TurnInitiatorContext
            {
                ChannelName = turnOriginChannel,
                UserId = sender?.SenderId ?? channelInfo?.UserId ?? thread.UserId,
                UserName = sender?.SenderName,
                UserRole = sender?.SenderRole,
                ChannelContext = turnChannelContext,
                GroupId = sender?.GroupId ?? channelInfo?.GroupId
            }
        };

        var itemSeq = 0;

        // Extract plain text from content parts for display and persistence
        var text = inputSnapshot?.DisplayText
            ?? string.Concat(content.OfType<TextContent>().Select(t => t.Text));
        var images = ExtractUserMessageImages(content);

        var userItem = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(NextItemSeq()),
            TurnId = turn.Id,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
                Payload = new UserMessagePayload
            {
                Text = text,
                DeliveryMode = inputSnapshot?.DeliveryMode,
                NativeInputParts = inputSnapshot?.NativeInputParts,
                MaterializedInputParts = inputSnapshot?.MaterializedInputParts,
                SenderId = sender?.SenderId,
                SenderName = sender?.SenderName,
                SenderRole = sender?.SenderRole,
                ChannelName = turnOriginChannel,
                ChannelContext = turnChannelContext,
                GroupId = sender?.GroupId ?? channelInfo?.GroupId,
                Images = images.Count > 0 ? images : null,
                TriggerKind = triggerInfo?.Kind,
                TriggerLabel = triggerInfo?.Label,
                TriggerRefId = triggerInfo?.RefId
            }
        };

        turn.Input = userItem;
        turn.Items.Add(userItem);
        thread.Turns.Add(turn);
        thread.LastActiveAt = DateTimeOffset.UtcNow;

        // Set a display name from the first user message so the session list shows a preview.
        var setTitleFromFirstUserMessage = false;
        if (string.IsNullOrEmpty(thread.DisplayName))
        {
            thread.DisplayName = text.Length > 50 ? text[..50] + "..." : text;
            setTitleFromFirstUserMessage = true;
        }

        if (setTitleFromFirstUserMessage)
            ThreadRenamedForBroadcast?.Invoke(thread);

        // Step 3: Create event channel
        var broker = GetOrCreateBroker(threadId);
        var eventChannel = broker.CreateTurnChannel(turn.Id, LogStreamDebugSessionEvent);

        void LogStreamDebugSessionEvent(SessionEvent evt)
        {
            if (sessionStreamDebugLogger == null || evt.EventType != SessionEventType.ItemDelta)
                return;
            if (!sessionStreamDebugLogger.ShouldCapture(evt.ThreadId, evt.TurnId))
                return;

            if (evt.DeltaPayload is { } agentDelta)
            {
                sessionStreamDebugLogger.Log(
                    "session_event_delta",
                    evt.ThreadId,
                    evt.TurnId,
                    new
                    {
                        itemId = evt.ItemId,
                        deltaKind = agentDelta.DeltaKind,
                        deltaChars = agentDelta.TextDelta.Length,
                        deltaText = sessionStreamDebugLogger.IncludeFullText ? agentDelta.TextDelta : null
                    });
            }
        }

        // Step 4: Emit initial events synchronously so the caller sees them before awaiting
        eventChannel.EmitTurnStarted(turn);
        ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnStarted);
        eventChannel.EmitItemStarted(userItem);
        eventChannel.EmitItemCompleted(userItem);

        // Step 5: Run execution in background
        var turnKey = new TurnKey(threadId, turn.Id);
        var cts = new CancellationTokenSource();
        _runningTurns[turnKey] = cts;

        _ = Task.Run(async () =>
        {
            // Link caller cancellation with our internal CTS inside the lambda so it lives
            // for the full duration of the background task rather than being disposed on method return.
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerCt, cts.Token);
            var executionCt = linkedCts.Token;

            IDisposable? gateLock = null;
            IDisposable? approvalOverride = null;
            AIAgent agent = defaultAgent;
            AgentSession? session = null;
            TokenTracker? tokenTracker = null;
            SessionItem? agentMessageItem = null;
            SessionItem? reasoningItem = null;
            var agentText = string.Empty;
            var reasoningText = string.Empty;
            var agentDeltaIndex = 0;
            Dictionary<int, SessionItem>? streamingToolCallItemsByIndex = null;
            Dictionary<int, string>? streamingToolNameByIndex = null;
            Dictionary<string, SessionItem>? streamingToolCallItemsByCallId = null;

            void FinalizeStreamingAgentMessage()
            {
                // Finalize the current AgentMessage so any subsequent text starts a
                // fresh item, preserving the natural interleaving in stored turns.
                if (agentMessageItem == null)
                    return;

                agentMessageItem.Payload = new AgentMessagePayload { Text = agentText };
                agentMessageItem.Status = ItemStatus.Completed;
                agentMessageItem.CompletedAt = DateTimeOffset.UtcNow;
                eventChannel.EmitItemCompleted(agentMessageItem);
                agentMessageItem = null;
                agentText = string.Empty;
            }

            void FinalizeStreamingReasoning()
            {
                if (reasoningItem == null)
                    return;

                reasoningItem.Payload = new ReasoningContentPayload { Text = reasoningText };
                reasoningItem.Status = ItemStatus.Completed;
                reasoningItem.CompletedAt = DateTimeOffset.UtcNow;
                eventChannel.EmitItemCompleted(reasoningItem);
                reasoningItem = null;
                reasoningText = string.Empty;
            }

            async Task PersistCancelledTurnAsync()
            {
                await TrySaveThreadAsync(thread);
                await TryRebuildAndSaveSessionAsync(agent, threadId);
            }

            async Task<ChatMessage?> TryDrainGuidanceMessageAsync(CancellationToken drainCt)
            {
                QueuedTurnInput queued;
                using (await AcquireThreadQueueLockAsync(threadId, drainCt))
                {
                    var queueIndex = thread.QueuedInputs.FindIndex(q =>
                        string.Equals(q.Status, "guidancePending", StringComparison.Ordinal) &&
                        string.Equals(q.ReadyAfterTurnId, turn.Id, StringComparison.Ordinal));
                    if (queueIndex < 0)
                        return null;

                    queued = thread.QueuedInputs[queueIndex];
                }

                var contentParts = await ResolveQueuedInputPartsAsync(queued.MaterializedInputParts.ToList(), drainCt);
                if (contentParts.Count == 0)
                    return null;

                var nativeParts = queued.NativeInputParts.ToList();
                var materializedParts = queued.MaterializedInputParts.ToList();
                var displayText = !string.IsNullOrWhiteSpace(queued.DisplayText)
                    ? queued.DisplayText
                    : SessionWireMapper.BuildDisplayText(nativeParts);
                var images = ExtractUserMessageImages(contentParts);

                var item = new SessionItem
                {
                    Id = SessionIdGenerator.NewItemId(NextItemSeq()),
                    TurnId = turn.Id,
                    Type = ItemType.UserMessage,
                    Status = ItemStatus.Completed,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Payload = new UserMessagePayload
                    {
                        Text = displayText,
                        DeliveryMode = "guidance",
                        NativeInputParts = nativeParts,
                        MaterializedInputParts = materializedParts,
                        SenderId = queued.Sender?.SenderId,
                        SenderName = queued.Sender?.SenderName,
                        SenderRole = queued.Sender?.SenderRole,
                        ChannelName = turn.OriginChannel,
                        ChannelContext = turn.Initiator?.ChannelContext,
                        GroupId = queued.Sender?.GroupId ?? turn.Initiator?.GroupId,
                        Images = images.Count > 0 ? images : null
                    }
                };

                IReadOnlyList<QueuedTurnInput> queueSnapshot;
                using (await AcquireThreadQueueLockAsync(threadId, CancellationToken.None))
                {
                    var queue = thread.QueuedInputs.ToList();
                    var queueIndex = queue.FindIndex(q =>
                        string.Equals(q.Id, queued.Id, StringComparison.Ordinal) &&
                        string.Equals(q.Status, "guidancePending", StringComparison.Ordinal) &&
                        string.Equals(q.ReadyAfterTurnId, turn.Id, StringComparison.Ordinal));
                    if (queueIndex < 0)
                        return null;

                    FinalizeStreamingAgentMessage();
                    FinalizeStreamingReasoning();

                    turn.Items.Add(item);
                    queue.RemoveAt(queueIndex);
                    thread.QueuedInputs = queue;
                    thread.LastActiveAt = DateTimeOffset.UtcNow;
                    await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
                    queueSnapshot = queue.ToList();
                }

                eventChannel.EmitItemStarted(item);
                eventChannel.EmitItemCompleted(item);
                PublishQueueUpdated(thread.Id, queueSnapshot);
                return new ChatMessage(ChatRole.User, contentParts);
            }

            async Task RestoreUndrainedGuidanceAsync()
            {
                IReadOnlyList<QueuedTurnInput> queueSnapshot;
                using (await AcquireThreadQueueLockAsync(threadId, CancellationToken.None))
                {
                    var restored = false;
                    var queue = thread.QueuedInputs.ToList();
                    for (var i = 0; i < queue.Count; i++)
                    {
                        var queued = queue[i];
                        if (!string.Equals(queued.Status, "guidancePending", StringComparison.Ordinal) ||
                            !string.Equals(queued.ReadyAfterTurnId, turn.Id, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        queue[i] = queued with { Status = "queued" };
                        restored = true;
                    }

                    if (!restored)
                        return;

                    thread.QueuedInputs = queue;
                    thread.LastActiveAt = DateTimeOffset.UtcNow;
                    await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
                    queueSnapshot = queue.ToList();
                }

                PublishQueueUpdated(thread.Id, queueSnapshot);
            }

            async Task<IReadOnlyList<ChatMessage>?> TryCompactBeforeSamplingAsync(
                IReadOnlyList<ChatMessage> modelVisibleHistory,
                CancellationToken compactionCt)
            {
                if (session is null || tokenTracker is null || modelVisibleHistory.Count == 0)
                    return null;

                var estimatedTokens = MessageTokenEstimator.Estimate(modelVisibleHistory);
                var persistedTokens = persistence.LoadContextUsageTokens(threadId) ?? 0;
                var tokenHint = Math.Max(
                    estimatedTokens,
                    Math.Max(tokenTracker.LastInputTokens, persistedTokens));
                var pipeline = agentFactory.CompactionPipeline;
                var threshold = pipeline.EvaluateThreshold(tokenHint);
                if (!threshold.AboveAuto)
                    return null;

                eventChannel.EmitSystemEvent(
                    "compacting",
                    percentLeft: threshold.PercentLeft,
                    tokenCount: threshold.Tokens);

                CompactionHistoryResult result;
                try
                {
                    result = await pipeline.TryAutoCompactHistoryAsync(
                        modelVisibleHistory,
                        threadId,
                        tokenHint,
                        thread.LastActiveAt,
                        compactionCt);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Pre-sampling compaction failed for thread {ThreadId}", threadId);
                    eventChannel.EmitSystemEvent(
                        "compactFailed",
                        message: ex.Message,
                        percentLeft: threshold.PercentLeft,
                        tokenCount: threshold.Tokens);
                    return null;
                }

                var status = result.Status;
                switch (status.Outcome)
                {
                    case CompactionOutcome.Micro:
                    case CompactionOutcome.Partial:
                        tokenTracker.Reset();
                        session.SetInMemoryChatHistory(
                            [.. result.Messages],
                            jsonSerializerOptions: SessionPersistenceJsonOptions.Default);
                        await SaveContextUsageSnapshotAsync(
                            threadId,
                            status.ThresholdAfter.Tokens,
                            CancellationToken.None);
                        traceCollector?.RecordContextCompaction(threadId);
                        eventChannel.EmitSystemEvent(
                            "compacted",
                            percentLeft: status.ThresholdAfter.PercentLeft,
                            tokenCount: status.ThresholdAfter.Tokens);
                        {
                            var noticeItem = CreateCompactionNoticeItem(
                                turn,
                                NextItemSeq(),
                                trigger: "auto",
                                status);
                            turn.Items.Add(noticeItem);
                            eventChannel.EmitItemStarted(noticeItem);
                            eventChannel.EmitItemCompleted(noticeItem);
                        }
                        ThreadRuntimeSignalForBroadcast?.Invoke(
                            threadId,
                            SessionThreadRuntimeSignal.ContextCompacted);
                        return result.Messages;

                    case CompactionOutcome.Skipped:
                        eventChannel.EmitSystemEvent(
                            "compactSkipped",
                            message: status.FailureReason,
                            percentLeft: status.ThresholdAfter.PercentLeft,
                            tokenCount: status.ThresholdAfter.Tokens);
                        return null;

                    case CompactionOutcome.Failed:
                        eventChannel.EmitSystemEvent(
                            "compactFailed",
                            message: status.FailureReason,
                            percentLeft: status.ThresholdAfter.PercentLeft,
                            tokenCount: status.ThresholdAfter.Tokens);
                        return null;

                    default:
                        return null;
                }
            }

            try
            {
                // Step 5a: Acquire SessionGate
                try
                {
                    gateLock = await sessionGate.AcquireAsync(threadId, executionCt);
                }
                catch (SessionGateOverflowException ex)
                {
                    logger?.LogWarning("Session gate overflow for thread {ThreadId}: {Message}", threadId, ex.Message);
                    FailTurn(turn, eventChannel, $"Session queue overflow: {ex.Message}");
                    return;
                }

                // Step 5b: Load/create AgentSession
                using (await AcquireThreadAgentLockAsync(threadId, executionCt))
                    agent = _threadAgents.GetValueOrDefault(threadId, defaultAgent);

                // Bind tracing and token tracking before session creation so session metadata
                // captured during CreateSessionAsync / LoadOrCreateSessionAsync is attributed
                // to the correct ephemeral or persisted thread.
                traceCollector?.BindThreadMainSession(threadId);
                TracingChatClient.CurrentSessionKey = threadId;
                TracingChatClient.ResetCallState(threadId);
                tokenTracker = agentFactory.GetOrCreateTokenTracker(threadId);
                TokenTracker.Current = tokenTracker;

                if (thread.HistoryMode == HistoryMode.Client && messages != null)
                {
                    // Client-managed: construct from provided messages
                    session = await agent.CreateSessionAsync(executionCt);
                    var chatHistory = session.GetService<ChatHistoryProvider>();
                    if (chatHistory is InMemoryChatHistoryProvider memProvider)
                    {
                        memProvider.SetMessages(session, [.. messages]);
                    }
                }
                else
                {
                    session = await persistence.LoadOrCreateSessionAsync(agent, threadId, executionCt);
                }

                // Step 5c: Append runtime context to the multimodal content list
                var userMessage = new ChatMessage(ChatRole.User, content.AppendRuntimeContext(turn.Initiator));

                // Step 5d: Run PrePrompt hooks
                if (hookRunner != null)
                {
                    var hookInput = new HookInput { SessionId = threadId, Prompt = text };
                    var hookResult = await hookRunner.RunAsync(HookEvent.PrePrompt, hookInput, executionCt);
                    if (hookResult.Blocked)
                    {
                        var errorMsg = $"Prompt blocked by hook: {hookResult.BlockReason ?? "no reason given"}";
                        var errorItem = CreateErrorItem(turn, NextItemSeq(), errorMsg, "hook_blocked", fatal: true);
                        turn.Items.Add(errorItem);
                        eventChannel.EmitItemStarted(errorItem);
                        eventChannel.EmitItemCompleted(errorItem);
                        FailTurn(turn, eventChannel, errorMsg);
                        return;
                    }
                }

                // Step 5e: Set up approval service override
                var approvalPolicy = ResolveApprovalPolicy(thread.Configuration?.ApprovalPolicy ?? ApprovalPolicy.Default);
                IApprovalService turnApprovalService;
                switch (approvalPolicy)
                {
                    case ApprovalPolicy.AutoApprove:
                        turnApprovalService = new AutoApproveApprovalService();
                        break;
                    case ApprovalPolicy.Interrupt:
                        turnApprovalService = new InterruptOnApprovalService(cts.Cancel);
                        break;
                    default:
                        var sessionApproval = new SessionApprovalService(
                            eventChannel,
                            turn,
                            NextItemSeq,
                            _approvalTimeout,
                            cts.Cancel,
                            approvalStore,
                            ThreadRuntimeSignalForBroadcast);
                        _pendingApprovals[turnKey] = sessionApproval;
                        turnApprovalService = sessionApproval;
                        break;
                }

                approvalOverride = SessionScopedApprovalService.SetOverride(turnApprovalService);

                // Set ApprovalContext for tools that read ApprovalContextScope
                var approvalContextDisposable = sender != null
                    ? ApprovalContextScope.Set(new ApprovalContext
                    {
                        UserId = sender.SenderId,
                        UserRole = sender.SenderRole,
                        GroupId = long.TryParse(sender.GroupId ?? channelInfo?.GroupId, out var groupId) ? groupId : 0,
                        Source = ResolveApprovalSource(channelInfo?.Channel)
                    })
                    : null;

                // Step 5g: Run agent
                var pluginFunctionCallIds = new HashSet<string>(StringComparer.Ordinal);
                long inputTokens = 0, outputTokens = 0;
                long lastUsageInput = 0, lastUsageOutput = 0;
                var pluginFunctionToolNames = GetPluginFunctionToolNames(threadId);

                // SubAgent progress aggregator: lazily created when SpawnAgent tool calls appear
                SubAgentProgressAggregator? progressAggregator = null;

                var effectiveWorkspacePath =
                    !string.IsNullOrWhiteSpace(thread.Configuration?.WorkspaceOverride)
                        ? thread.Configuration.WorkspaceOverride!
                        : agentFactory.ToolProviderContext.WorkspacePath;
                var requireApprovalOutsideWorkspace =
                    thread.Configuration?.RequireApprovalOutsideWorkspace
                    ?? agentFactory.ToolProviderContext.Config.Tools.File.RequireApprovalOutsideWorkspace;
                var effectivePathBlacklist = !string.IsNullOrWhiteSpace(thread.Configuration?.WorkspaceOverride)
                    ? new PathBlacklist([])
                    : agentFactory.ToolProviderContext.PathBlacklist;
                var supportsCommandExecutionStreaming =
                    AppServer.AppServerRequestContext.CurrentConnection?.SupportsCommandExecutionStreaming == true;

                using var pluginFunctionScope = PluginFunctionExecutionScope.Set(
                    new PluginFunctionExecutionContext
                    {
                        ThreadId = threadId,
                        TurnId = turn.Id,
                        OriginChannel = turnOriginChannel,
                        ChannelContext = turn.Initiator?.ChannelContext ?? thread.ChannelContext,
                        SenderId = turn.Initiator?.UserId ?? thread.UserId,
                        GroupId = turn.Initiator?.GroupId,
                        WorkspacePath = effectiveWorkspacePath,
                        RequireApprovalOutsideWorkspace = requireApprovalOutsideWorkspace,
                        ApprovalService = turnApprovalService,
                        PathBlacklist = effectivePathBlacklist,
                        Turn = turn,
                        NextItemSequence = NextItemSeq,
                        EmitItemStarted = eventChannel.EmitItemStarted,
                        EmitItemCompleted = eventChannel.EmitItemCompleted
                    });
                using var commandExecutionScope = CommandExecutionRuntimeScope.Set(
                    new CommandExecutionRuntimeContext
                    {
                        ThreadId = threadId,
                        TurnId = turn.Id,
                        Turn = turn,
                        NextItemSequence = NextItemSeq,
                        EmitItemStarted = eventChannel.EmitItemStarted,
                        EmitItemDelta = eventChannel.EmitItemDelta,
                        EmitItemCompleted = eventChannel.EmitItemCompleted,
                        SupportsCommandExecutionStreaming = supportsCommandExecutionStreaming
                    });
                var currentSubAgentSource = thread.Source.SubAgent;
                using var subAgentSessionScope = SubAgentSessionScope.Set(new SubAgentSessionContext
                {
                    SessionService = this,
                    ParentThread = thread,
                    ParentTurnId = turn.Id,
                    RootThreadId = currentSubAgentSource?.RootThreadId ?? thread.Id,
                    Depth = currentSubAgentSource?.Depth ?? 0
                });
                try
                {
                    using var preSamplingCompactionScope = PreSamplingCompactionRuntimeScope.Set(
                        new PreSamplingCompactionRuntimeContext
                        {
                            TryCompactAsync = TryCompactBeforeSamplingAsync
                        });
                    using var guidanceScope = TurnGuidanceRuntimeScope.Set(new TurnGuidanceRuntimeContext
                    {
                        ThreadId = threadId,
                        TurnId = turn.Id,
                        TryDrainGuidanceMessageAsync = TryDrainGuidanceMessageAsync
                    });

                    await foreach (var update in agent.RunStreamingAsync(userMessage, session)
                        .WithCancellation(executionCt))
                    {
                        foreach (var responseContent in update.Contents)
                        {
                            switch (responseContent)
                            {
                                case TextContent tc:
                                    // Agent message text
                                    FinalizeStreamingReasoning();
                                    var chunk = tc.Text ?? string.Empty;
                                    if (agentMessageItem == null)
                                    {
                                        agentMessageItem = new SessionItem
                                        {
                                            Id = SessionIdGenerator.NewItemId(NextItemSeq()),
                                            TurnId = turn.Id,
                                            Type = ItemType.AgentMessage,
                                            Status = ItemStatus.Streaming,
                                            CreatedAt = DateTimeOffset.UtcNow,
                                            Payload = new AgentMessagePayload { Text = string.Empty }
                                        };
                                        turn.Items.Add(agentMessageItem);
                                        eventChannel.EmitItemStarted(agentMessageItem);
                                    }
                                    agentText += chunk;
                                    agentDeltaIndex += 1;
                                    if (sessionStreamDebugLogger?.ShouldCapture(threadId, turn.Id) == true)
                                    {
                                        sessionStreamDebugLogger.Log(
                                            "agent_delta_source",
                                            threadId,
                                            turn.Id,
                                            new
                                            {
                                                itemId = agentMessageItem.Id,
                                                deltaIndex = agentDeltaIndex,
                                                chunkChars = chunk.Length,
                                                chunkText = sessionStreamDebugLogger.IncludeFullText ? chunk : null,
                                                cumulativeChars = agentText.Length,
                                                cumulativeText = sessionStreamDebugLogger.IncludeFullText ? agentText : null
                                            });
                                    }
                                    eventChannel.EmitItemDelta(agentMessageItem, new AgentMessageDelta { TextDelta = chunk });
                                    break;

                                case TextReasoningContent reasoning:
                                    if (ReasoningContentHelper.TryGetText(reasoning, out var rText))
                                    {
                                        FinalizeStreamingAgentMessage();
                                        if (reasoningItem == null)
                                        {
                                            reasoningItem = new SessionItem
                                            {
                                                Id = SessionIdGenerator.NewItemId(NextItemSeq()),
                                                TurnId = turn.Id,
                                                Type = ItemType.ReasoningContent,
                                                Status = ItemStatus.Streaming,
                                                CreatedAt = DateTimeOffset.UtcNow,
                                                Payload = new ReasoningContentPayload { Text = string.Empty }
                                            };
                                            turn.Items.Add(reasoningItem);
                                            eventChannel.EmitItemStarted(reasoningItem);
                                        }
                                        reasoningText += rText;
                                        eventChannel.EmitItemDelta(reasoningItem,
                                            new ReasoningContentDelta { TextDelta = rText });
                                    }
                                    break;

                                case ToolCallArgumentsDeltaContent toolArgsDelta:
                                {
                                    if (string.IsNullOrEmpty(toolArgsDelta.ArgumentsDelta))
                                        break;

                                    var toolCallIndex = toolArgsDelta.ToolCallIndex;
                                    if (!string.IsNullOrWhiteSpace(toolArgsDelta.ToolName))
                                    {
                                        streamingToolNameByIndex ??= [];
                                        streamingToolNameByIndex[toolCallIndex] = toolArgsDelta.ToolName;
                                    }

                                    var resolvedToolName =
                                        streamingToolNameByIndex != null
                                        && streamingToolNameByIndex.TryGetValue(toolCallIndex, out var cachedToolName)
                                            ? cachedToolName
                                            : null;
                                    if (string.IsNullOrWhiteSpace(resolvedToolName))
                                        break;
                                    if (IsPluginFunctionTool(pluginFunctionToolNames, resolvedToolName))
                                        break;

                                    FinalizeStreamingReasoning();
                                    streamingToolCallItemsByIndex ??= [];
                                    if (!streamingToolCallItemsByIndex.TryGetValue(toolCallIndex, out var streamingToolCallItem))
                                    {
                                        streamingToolCallItem = new SessionItem
                                        {
                                            Id = SessionIdGenerator.NewItemId(NextItemSeq()),
                                            TurnId = turn.Id,
                                            Type = ItemType.ToolCall,
                                            Status = ItemStatus.Streaming,
                                            CreatedAt = DateTimeOffset.UtcNow,
                                            Payload = new ToolCallPayload
                                            {
                                                ToolName = resolvedToolName,
                                                CallId = toolArgsDelta.CallId ?? string.Empty,
                                                Arguments = null
                                            }
                                        };
                                        streamingToolCallItemsByIndex[toolCallIndex] = streamingToolCallItem;
                                        turn.Items.Add(streamingToolCallItem);
                                        eventChannel.EmitItemStarted(streamingToolCallItem);
                                    }

                                    if (!string.IsNullOrWhiteSpace(toolArgsDelta.CallId))
                                    {
                                        streamingToolCallItemsByCallId ??= new Dictionary<string, SessionItem>(StringComparer.Ordinal);
                                        if (!streamingToolCallItemsByCallId.ContainsKey(toolArgsDelta.CallId))
                                            streamingToolCallItemsByCallId[toolArgsDelta.CallId] = streamingToolCallItem;
                                    }

                                    eventChannel.EmitItemDelta(streamingToolCallItem, new ToolCallArgumentsDelta
                                    {
                                        ToolName = resolvedToolName,
                                        CallId = toolArgsDelta.CallId,
                                        Delta = toolArgsDelta.ArgumentsDelta
                                    });
                                    break;
                                }

                                case FunctionCallContent fc:
                                {
                                    var isPluginFunctionTool = IsPluginFunctionTool(pluginFunctionToolNames, fc.Name);
                                    if (isPluginFunctionTool && !string.IsNullOrWhiteSpace(fc.CallId))
                                        pluginFunctionCallIds.Add(fc.CallId);
                                    FinalizeStreamingReasoning();
                                    RegisterCommandExecutionIfNeeded(
                                        fc,
                                        turn,
                                        NextItemSeq,
                                        eventChannel,
                                        supportsCommandExecutionStreaming,
                                        effectiveWorkspacePath);
                                    if (isPluginFunctionTool)
                                        break;

                                    SessionItem? toolCallItem = null;
                                    if (!string.IsNullOrWhiteSpace(fc.CallId)
                                        && streamingToolCallItemsByCallId != null
                                        && streamingToolCallItemsByCallId.TryGetValue(fc.CallId, out var existingStreamingToolCallItem))
                                    {
                                        toolCallItem = existingStreamingToolCallItem;
                                        toolCallItem.Status = ItemStatus.Completed;
                                        toolCallItem.CompletedAt = DateTimeOffset.UtcNow;
                                        toolCallItem.Payload = new ToolCallPayload
                                        {
                                            ToolName = fc.Name,
                                            Arguments = fc.Arguments != null
                                                ? JsonNode.Parse(
                                                    System.Text.Json.JsonSerializer.Serialize(
                                                        fc.Arguments)) as JsonObject
                                                : null,
                                            CallId = fc.CallId
                                        };
                                        eventChannel.EmitItemCompleted(toolCallItem);
                                        streamingToolCallItemsByCallId.Remove(fc.CallId);
                                        TryRemoveStreamingToolCallIndexByItemReference(
                                            streamingToolCallItemsByIndex,
                                            existingStreamingToolCallItem);
                                    }
                                    else
                                    {
                                        toolCallItem = new SessionItem
                                        {
                                            Id = SessionIdGenerator.NewItemId(NextItemSeq()),
                                            TurnId = turn.Id,
                                            Type = ItemType.ToolCall,
                                            Status = ItemStatus.Completed,
                                            CreatedAt = DateTimeOffset.UtcNow,
                                            CompletedAt = DateTimeOffset.UtcNow,
                                            Payload = new ToolCallPayload
                                            {
                                                ToolName = fc.Name,
                                                Arguments = fc.Arguments != null
                                                    ? JsonNode.Parse(
                                                        System.Text.Json.JsonSerializer.Serialize(
                                                            fc.Arguments)) as JsonObject
                                                    : null,
                                                CallId = fc.CallId
                                            }
                                        };
                                        turn.Items.Add(toolCallItem);
                                        eventChannel.EmitItemStarted(toolCallItem);
                                        eventChannel.EmitItemCompleted(toolCallItem);
                                    }

                                    // Track SubAgent progress when SpawnAgent tool calls are detected
                                    if (string.Equals(fc.Name, "SpawnAgent", StringComparison.Ordinal)
                                        && fc.Arguments != null)
                                    {
                                        var rawLabel = fc.Arguments.TryGetValue("agentNickname", out var labelObj)
                                            ? labelObj?.ToString()
                                            : null;
                                        var rawTask = fc.Arguments.TryGetValue("agentPrompt", out var taskObj)
                                            ? taskObj?.ToString()
                                            : null;

                                        if (rawLabel != null || rawTask != null)
                                        {
                                            var normalizedLabel = SubAgentManager.NormalizeLabel(
                                                rawLabel, rawTask ?? "task");
                                            progressAggregator ??= new SubAgentProgressAggregator(
                                                eventChannel, threadId, turn.Id);
                                            progressAggregator.TrackLabel(normalizedLabel);
                                        }
                                    }
                                    break;
                                }

                                case FunctionResultContent fr:
                                {
                                    FinalizeStreamingReasoning();
                                    if (!string.IsNullOrWhiteSpace(fr.CallId)
                                        && pluginFunctionCallIds.Remove(fr.CallId))
                                    {
                                        // Plugin function calls are represented by pluginFunctionCall
                                        // items emitted from the plugin function wrapper.
                                        // Even when toolResult is suppressed, keep text segmentation aligned
                                        // with normal tools so post-tool text starts a new agent message item.
                                        FinalizeStreamingAgentMessage();
                                        break;
                                    }
                                    var resultText = ImageContentSanitizingChatClient.DescribeResult(fr.Result);
                                    var toolResultItem = new SessionItem
                                    {
                                        Id = SessionIdGenerator.NewItemId(NextItemSeq()),
                                        TurnId = turn.Id,
                                        Type = ItemType.ToolResult,
                                        Status = ItemStatus.Completed,
                                        CreatedAt = DateTimeOffset.UtcNow,
                                        CompletedAt = DateTimeOffset.UtcNow,
                                        Payload = new ToolResultPayload
                                        {
                                            CallId = fr.CallId,
                                            Result = resultText,
                                            Success = fr.Exception == null
                                                && !StreamingFunctionInvokingChatClient.IsInvalidToolArgumentsResult(fr)
                                        }
                                    };
                                    turn.Items.Add(toolResultItem);
                                    eventChannel.EmitItemStarted(toolResultItem);
                                    eventChannel.EmitItemCompleted(toolResultItem);
                                    FinalizeStreamingAgentMessage();
                                    break;
                                }

                                case UsageContent usage:
                                {
                                    var curIn = usage.Details.InputTokenCount ?? 0;
                                    var curOut = usage.Details.OutputTokenCount ?? 0;
                                    if (curIn > 0 || curOut > 0)
                                    {
                                        UsageSnapshotDelta.Compute(
                                            curIn,
                                            curOut,
                                            ref lastUsageInput,
                                            ref lastUsageOutput,
                                            out var deltaIn,
                                            out var deltaOut);
                                        if (deltaIn > 0 || deltaOut > 0)
                                        {
                                            inputTokens += deltaIn;
                                            outputTokens += deltaOut;
                                            tokenTracker.UpdateWithStreamingDeltas(deltaIn, deltaOut, curIn);
                                            var contextUsage = await SaveContextUsageSnapshotAsync(
                                                threadId,
                                                tokenTracker.LastInputTokens,
                                                CancellationToken.None);
                                            eventChannel.EmitUsageDelta(
                                                deltaIn,
                                                deltaOut,
                                                totalInputTokens: tokenTracker.LastInputTokens,
                                                totalOutputTokens: outputTokens,
                                                contextUsage: contextUsage);
                                        }
                                    }

                                    break;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    // Stop SubAgent progress aggregator before cleaning up AsyncLocal context
                    if (progressAggregator != null)
                        await progressAggregator.DisposeAsync();

                    TracingChatClient.ResetCallState(threadId);
                    TracingChatClient.CurrentSessionKey = null;
                    TokenTracker.Current = null;
                    approvalContextDisposable?.Dispose();
                }

                // Step 5h: Finalize any still-streaming items.
                FinalizeStreamingAgentMessage();
                FinalizeStreamingReasoning();

                // Step 5i: Accumulate token usage (include SubAgent tokens)
                var totalInput = inputTokens + tokenTracker.SubAgentInputTokens;
                var totalOutput = outputTokens + tokenTracker.SubAgentOutputTokens;
                if (totalInput > 0 || totalOutput > 0)
                {
                    turn.TokenUsage = new TokenUsageInfo
                    {
                        InputTokens = totalInput,
                        OutputTokens = totalOutput,
                        TotalTokens = totalInput + totalOutput
                    };
                }

                // Step 5j: Run Stop hooks
                if (hookRunner != null)
                {
                    var stopInput = new HookInput { SessionId = threadId, Response = agentText };
                    await hookRunner.RunAsync(HookEvent.Stop, stopInput, CancellationToken.None);
                }

                // Step 5k: Post-turn threshold notification.
                // Auto compaction runs before model sampling through
                // PreSamplingCompactionRuntimeScope. If the final response
                // itself pushes the context over the threshold and no follow-up
                // model call is needed, keep the snapshot visible and compact
                // before the next sampling request.
                {
                    var compactionPipeline = agentFactory.CompactionPipeline;
                    var threshold = compactionPipeline.EvaluateThreshold(tokenTracker.LastInputTokens);
                    if (threshold.AboveError)
                    {
                        eventChannel.EmitSystemEvent(
                            "compactError",
                            percentLeft: threshold.PercentLeft,
                            tokenCount: threshold.Tokens);
                    }
                    else if (threshold.AboveWarning)
                    {
                        eventChannel.EmitSystemEvent(
                            "compactWarning",
                            percentLeft: threshold.PercentLeft,
                            tokenCount: threshold.Tokens);
                    }
                }

                // Step 5m: Release gate
                gateLock.Dispose();
                gateLock = null;

                await RestoreUndrainedGuidanceAsync();

                // Steps 5n-5r: Complete Turn
                turn.Status = TurnStatus.Completed;
                turn.CompletedAt = DateTimeOffset.UtcNow;
                thread.LastActiveAt = DateTimeOffset.UtcNow;
                RecordTurnTokenUsage(thread, turn);
                eventChannel.EmitTurnCompleted(turn);

                try
                {
                    await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
                    await TrySaveSessionAsync(agent, session, threadId);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Failed to persist thread state after turn completion for thread {ThreadId}", threadId);
                }

                TryScheduleMemoryConsolidation(threadId, thread, turn, session, eventChannel, NextItemSeq);
                ThreadRuntimeSignalForBroadcast?.Invoke(
                    threadId,
                    EndsWithSuccessfulCreatePlanInPlanMode(thread, turn)
                        ? SessionThreadRuntimeSignal.TurnCompletedAwaitingPlanConfirmation
                        : SessionThreadRuntimeSignal.TurnCompleted);

                await TryStartNextQueuedTurnAsync(threadId, CancellationToken.None);
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Explicit CancelTurn call
                FinalizeStreamingAgentMessage();
                FinalizeStreamingReasoning();
                await RestoreUndrainedGuidanceAsync();
                turn.Status = TurnStatus.Cancelled;
                turn.CompletedAt = DateTimeOffset.UtcNow;
                eventChannel.EmitTurnCancelled(turn, "Cancelled by request");
                ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnCancelled);
                await PersistCancelledTurnAsync();
            }
            catch (OperationCanceledException)
            {
                // Caller cancellation
                FinalizeStreamingAgentMessage();
                FinalizeStreamingReasoning();
                await RestoreUndrainedGuidanceAsync();
                turn.Status = TurnStatus.Cancelled;
                turn.CompletedAt = DateTimeOffset.UtcNow;
                eventChannel.EmitTurnCancelled(turn, "Caller cancelled");
                ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnCancelled);
                await PersistCancelledTurnAsync();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Turn execution failed for thread {ThreadId}", threadId);

                // Step 5k-R: Reactive compaction on prompt_too_long / context_length_exceeded.
                // On success we still fail this turn (the model's streaming response is
                // already gone), but the compacted history lets the user re-send their
                // prompt and succeed without any manual cleanup.
                var reactiveMessage = ex.Message;
                if (IsPromptTooLongError(ex) && session is not null)
                {
                    try
                    {
                        eventChannel.EmitSystemEvent("compacting");
                        var status = await agentFactory.CompactionPipeline.TryReactiveCompactAsync(
                            session,
                            threadId,
                            thread.LastActiveAt,
                            CancellationToken.None);
                        if (status.Success)
                        {
                            tokenTracker?.Reset();
                            await SaveContextUsageSnapshotAsync(
                                threadId,
                                status.ThresholdAfter.Tokens,
                                CancellationToken.None);
                            traceCollector?.RecordContextCompaction(threadId);
                            eventChannel.EmitSystemEvent(
                                "compacted",
                                percentLeft: status.ThresholdAfter.PercentLeft,
                                tokenCount: status.ThresholdAfter.Tokens);
                            {
                                var noticeItem = CreateCompactionNoticeItem(
                                    turn,
                                    NextItemSeq(),
                                    trigger: "reactive",
                                    status);
                                turn.Items.Add(noticeItem);
                                eventChannel.EmitItemStarted(noticeItem);
                                eventChannel.EmitItemCompleted(noticeItem);
                            }
                            ThreadRuntimeSignalForBroadcast?.Invoke(
                                threadId,
                                SessionThreadRuntimeSignal.ContextCompacted);
                            reactiveMessage =
                                "The request exceeded the model's context window. "
                                + "History has been compacted; please re-send the message.";
                        }
                        else
                        {
                            eventChannel.EmitSystemEvent(
                                "compactFailed",
                                message: status.FailureReason,
                                percentLeft: status.ThresholdAfter.PercentLeft,
                                tokenCount: status.ThresholdAfter.Tokens);
                        }
                    }
                    catch (Exception compactEx)
                    {
                        logger?.LogWarning(
                            compactEx,
                            "Reactive compaction failed for thread {ThreadId}",
                            threadId);
                    }
                }

                await RestoreUndrainedGuidanceAsync();
                FailTurn(turn, eventChannel, reactiveMessage);
                ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnFailed);
                await TrySaveThreadAsync(thread);
                if (session is not null)
                    await TrySaveSessionAsync(agent, session, threadId);
            }
            finally
            {
                approvalOverride?.Dispose();
                gateLock?.Dispose();
                _pendingApprovals.TryRemove(turnKey, out _);
                _runningTurns.TryRemove(turnKey, out var runCts);
                runCts?.Dispose();
                eventChannel.Complete();
            }
        }, CancellationToken.None); // Run regardless of caller ct; we handle it internally

        return eventChannel;

        int NextItemSeq() => Interlocked.Increment(ref itemSeq);
    }

    /// <inheritdoc/>
    public Task ResolveApprovalAsync(
        string threadId,
        string turnId,
        string requestId,
        SessionApprovalDecision decision,
        CancellationToken ct = default)
    {
        if (_pendingApprovals.TryGetValue(new TurnKey(threadId, turnId), out var svc))
        {
            svc.TryResolve(requestId, decision);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CancelTurnAsync(string threadId, string turnId, CancellationToken ct = default)
    {
        if (_runningTurns.TryGetValue(new TurnKey(threadId, turnId), out var cts))
            cts.Cancel();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task CleanBackgroundTerminalsAsync(string threadId, CancellationToken ct = default)
    {
        if (backgroundTerminalService == null)
            return;

        await backgroundTerminalService.CleanThreadAsync(threadId, ct);
    }

    /// <inheritdoc/>
    public async Task<QueuedTurnInput> EnqueueTurnInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        CancellationToken ct = default,
        SessionInputSnapshot? inputSnapshot = null)
    {
        if (content.Count == 0 && inputSnapshot?.MaterializedInputParts is not { Count: > 0 })
            throw new InvalidOperationException("Queued input must not be empty.");

        var thread = await GetOrLoadThreadAsync(threadId, ct);
        if (thread.Status != ThreadStatus.Active)
            throw new InvalidOperationException($"Thread '{threadId}' is not Active (current status: {thread.Status}). Cannot enqueue input.");

        var activeTurnId = thread.Turns
            .LastOrDefault(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval)
            ?.Id;

        var nativeParts = inputSnapshot?.NativeInputParts?.ToList()
            ?? content.Select(c => c.ToWireInputPart()).ToList();
        var materializedParts = inputSnapshot?.MaterializedInputParts?.ToList()
            ?? nativeParts;
        var displayText = inputSnapshot?.DisplayText
            ?? SessionWireMapper.BuildDisplayText(nativeParts);

        var queued = new QueuedTurnInput
        {
            Id = SessionIdGenerator.NewQueuedInputId(),
            ThreadId = threadId,
            NativeInputParts = nativeParts,
            MaterializedInputParts = materializedParts,
            DisplayText = displayText,
            Sender = sender,
            Status = "queued",
            CreatedAt = DateTimeOffset.UtcNow,
            ReadyAfterTurnId = activeTurnId
        };

        IReadOnlyList<QueuedTurnInput> queueSnapshot;
        using (await AcquireThreadQueueLockAsync(threadId, ct))
        {
            if (_threads.TryGetValue(threadId, out var cachedThread))
                thread = cachedThread;

            queued = queued with
            {
                ReadyAfterTurnId = thread.Turns
                    .LastOrDefault(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval)
                    ?.Id
            };

            var queue = thread.QueuedInputs.ToList();
            queue.Add(queued);
            thread.QueuedInputs = queue;
            thread.LastActiveAt = DateTimeOffset.UtcNow;
            await PersistThreadWithMaterializationAsync(thread, ct);
            queueSnapshot = queue.ToList();
        }

        PublishQueueUpdated(thread.Id, queueSnapshot);
        return queued;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(
        string threadId,
        string queuedInputId,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        IReadOnlyList<QueuedTurnInput> queueSnapshot;
        using (await AcquireThreadQueueLockAsync(threadId, ct))
        {
            if (_threads.TryGetValue(threadId, out var cachedThread))
                thread = cachedThread;

            var queue = thread.QueuedInputs.ToList();
            var removed = queue.RemoveAll(q => string.Equals(q.Id, queuedInputId, StringComparison.Ordinal));
            if (removed == 0)
                throw new KeyNotFoundException($"Queued input '{queuedInputId}' not found.");

            thread.QueuedInputs = queue;
            thread.LastActiveAt = DateTimeOffset.UtcNow;
            await PersistThreadWithMaterializationAsync(thread, ct);
            queueSnapshot = queue.ToList();
        }

        PublishQueueUpdated(thread.Id, queueSnapshot);
        return queueSnapshot;
    }

    /// <inheritdoc/>
    public async Task<TurnSteerResult> SteerTurnAsync(
        string threadId,
        string expectedTurnId,
        string queuedInputId,
        CancellationToken ct = default,
        SenderContext? sender = null)
    {
        if (string.IsNullOrWhiteSpace(expectedTurnId))
            throw new InvalidOperationException("expectedTurnId must not be empty.");
        if (string.IsNullOrWhiteSpace(queuedInputId))
            throw new InvalidOperationException("queuedInputId must not be empty.");

        var thread = await GetOrLoadThreadAsync(threadId, ct);
        if (thread.Status != ThreadStatus.Active)
            throw new InvalidOperationException($"Thread '{threadId}' is not Active (current status: {thread.Status}). Cannot steer turn.");

        var turn = thread.Turns.LastOrDefault(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval)
            ?? throw new InvalidOperationException($"Thread '{threadId}' has no active turn to steer.");
        if (!string.Equals(turn.Id, expectedTurnId, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected active turn id '{expectedTurnId}' but found '{turn.Id}'.");

        IReadOnlyList<QueuedTurnInput> queueSnapshot;
        using (await AcquireThreadQueueLockAsync(threadId, ct))
        {
            if (_threads.TryGetValue(threadId, out var cachedThread))
                thread = cachedThread;

            turn = thread.Turns.LastOrDefault(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval)
                ?? throw new InvalidOperationException($"Thread '{threadId}' has no active turn to steer.");
            if (!string.Equals(turn.Id, expectedTurnId, StringComparison.Ordinal))
                throw new InvalidOperationException($"Expected active turn id '{expectedTurnId}' but found '{turn.Id}'.");

            var queue = thread.QueuedInputs.ToList();
            var queueIndex = queue.FindIndex(q => string.Equals(q.Id, queuedInputId, StringComparison.Ordinal));
            if (queueIndex < 0)
                throw new KeyNotFoundException($"Queued input '{queuedInputId}' not found.");

            var queued = queue[queueIndex];
            if (!string.Equals(queued.Status, "queued", StringComparison.Ordinal))
                throw new InvalidOperationException($"Queued input '{queuedInputId}' is not queued (current status: {queued.Status}).");

            queue[queueIndex] = queued with
            {
                Status = "guidancePending",
                ReadyAfterTurnId = turn.Id,
                Sender = sender ?? queued.Sender
            };
            thread.QueuedInputs = queue;
            thread.LastActiveAt = DateTimeOffset.UtcNow;
            await PersistThreadWithMaterializationAsync(thread, ct);
            queueSnapshot = queue.ToList();
        }

        PublishQueueUpdated(thread.Id, queueSnapshot);

        return new TurnSteerResult
        {
            TurnId = turn.Id,
            QueuedInputs = queueSnapshot
        };
    }

    /// <inheritdoc/>
    public async Task<SessionThread> RollbackThreadAsync(string threadId, int numTurns, CancellationToken ct = default)
    {
        if (numTurns <= 0)
            throw new ArgumentOutOfRangeException(nameof(numTurns), "numTurns must be >= 1.");

        var thread = await GetOrLoadThreadAsync(threadId, ct);
        if (thread.Status == ThreadStatus.Archived)
            throw new InvalidOperationException($"Thread '{threadId}' is archived and cannot be rolled back.");
        if (thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval))
            throw new InvalidOperationException($"Thread '{threadId}' has a running Turn. Cancel it before rollback.");
        if (thread.Turns.Count < numTurns)
            throw new InvalidOperationException($"Thread '{threadId}' has only {thread.Turns.Count} turns; cannot roll back {numTurns}.");

        thread.Turns.RemoveRange(thread.Turns.Count - numTurns, numTurns);
        thread.LastActiveAt = DateTimeOffset.UtcNow;

        await persistence.RollbackThreadAsync(thread, numTurns, ct);
        await TryRebuildAndSaveSessionAsync(_threadAgents.GetValueOrDefault(threadId, defaultAgent), threadId);
        ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnCompleted);
        return thread;
    }

    // =========================================================================
    // Configuration
    // =========================================================================

    /// <inheritdoc/>
    public async Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        using (await AcquireThreadAgentLockAsync(threadId, ct))
        {
            thread.Configuration ??= new ThreadConfiguration();
            thread.Configuration.Mode = mode;

            _threadAgents[threadId] = await BuildAgentForThreadAsync(thread, ct);

            await PersistThreadWithMaterializationAsync(thread, ct);
        }
    }

    /// <inheritdoc/>
    public async Task UpdateThreadConfigurationAsync(
        string threadId,
        ThreadConfiguration config,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        using (await AcquireThreadAgentLockAsync(threadId, ct))
        {
            thread.Configuration = config;
            _threadAgents[threadId] = await BuildAgentForThreadAsync(thread, ct);
            await PersistThreadWithMaterializationAsync(thread, ct);
        }
    }

    /// <inheritdoc />
    public async Task RefreshThreadAgentAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        using (await AcquireThreadAgentLockAsync(threadId, ct))
            _threadAgents[threadId] = await BuildAgentForThreadAsync(thread, ct);
    }

    /// <inheritdoc />
    public void InvalidateThreadAgents()
    {
        _forcePerThreadAgents = true;
        _threadAgents.Clear();
    }

    /// <inheritdoc/>
    public async Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        var previous = thread.DisplayName;
        thread.DisplayName = displayName;
        await PersistThreadWithMaterializationAsync(thread, ct);
        if (previous != displayName)
            ThreadRenamedForBroadcast?.Invoke(thread);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private async Task<SessionThread> GetOrLoadThreadAsync(string threadId, CancellationToken ct)
    {
        if (_threads.TryGetValue(threadId, out var cached))
            return cached;

        var thread = await persistence.LoadThreadAsync(threadId, ct)
                     ?? throw new KeyNotFoundException($"Thread '{threadId}' not found.");

        _threads[thread.Id] = thread;
        _materializedThreads[thread.Id] = 0;
        _ = GetOrCreateBroker(thread.Id);
        return thread;
    }

    private async Task<IDisposable> AcquireThreadQueueLockAsync(string threadId, CancellationToken ct)
    {
        var queueLock = _threadQueueLocks.GetOrAdd(threadId, static _ => new SemaphoreSlim(1, 1));
        await queueLock.WaitAsync(ct);
        return new SemaphoreSlimReleaser(queueLock);
    }

    private async Task<IDisposable> AcquireThreadAgentLockAsync(string threadId, CancellationToken ct)
    {
        var agentLock = _threadAgentLocks.GetOrAdd(threadId, static _ => new SemaphoreSlim(1, 1));
        await agentLock.WaitAsync(ct);
        return new SemaphoreSlimReleaser(agentLock);
    }

    private void PublishQueueUpdated(string threadId, IReadOnlyList<QueuedTurnInput> queuedInputs) =>
        GetOrCreateBroker(threadId).PublishThreadQueueUpdated(queuedInputs);

    private sealed class SemaphoreSlimReleaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }

    private async Task TryStartNextQueuedTurnAsync(string threadId, CancellationToken ct)
    {
        QueuedTurnInput? queued = null;
        SessionThread? thread = null;
        try
        {
            thread = await GetOrLoadThreadAsync(threadId, ct);
            IReadOnlyList<QueuedTurnInput> queueSnapshot;
            using (await AcquireThreadQueueLockAsync(threadId, ct))
            {
                if (_threads.TryGetValue(threadId, out var cachedThread))
                    thread = cachedThread;

                var queue = thread.QueuedInputs.ToList();
                var queueIndex = queue.FindIndex(q => string.Equals(q.Status, "queued", StringComparison.Ordinal));
                if (queueIndex < 0)
                    return;
                if (thread.Turns.Any(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval))
                    return;

                queued = queue[queueIndex];
                queue.RemoveAt(queueIndex);
                thread.QueuedInputs = queue;
                thread.LastActiveAt = DateTimeOffset.UtcNow;
                await PersistThreadWithMaterializationAsync(thread, ct);
                queueSnapshot = queue.ToList();
            }

            PublishQueueUpdated(thread.Id, queueSnapshot);

            var content = await ResolveQueuedInputPartsAsync(queued.MaterializedInputParts.ToList(), ct);
            if (content.Count == 0)
                return;

            var events = SubmitInputAsync(
                threadId,
                content,
                queued.Sender,
                messages: null,
                ct,
                new SessionInputSnapshot
                {
                    NativeInputParts = queued.NativeInputParts,
                    MaterializedInputParts = queued.MaterializedInputParts,
                    DisplayText = queued.DisplayText,
                    DeliveryMode = "queued"
                });

            _ = Task.Run(async () =>
            {
                await foreach (var _ in events.WithCancellation(CancellationToken.None)) { }
            }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to start queued input {QueuedInputId} for thread {ThreadId}", queued?.Id, threadId);
            if (thread != null && queued != null)
            {
                IReadOnlyList<QueuedTurnInput>? queueSnapshot = null;
                using (await AcquireThreadQueueLockAsync(threadId, CancellationToken.None))
                {
                    if (_threads.TryGetValue(threadId, out var cachedThread))
                        thread = cachedThread;

                    var queue = thread.QueuedInputs.ToList();
                    if (queue.All(q => !string.Equals(q.Id, queued.Id, StringComparison.Ordinal)))
                    {
                        queue.Insert(0, queued);
                        thread.QueuedInputs = queue;
                        await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
                        queueSnapshot = queue.ToList();
                    }
                }

                if (queueSnapshot != null)
                    PublishQueueUpdated(thread.Id, queueSnapshot);
            }
        }
    }

    private static async Task<List<AIContent>> ResolveQueuedInputPartsAsync(
        List<SessionWireInputPart> parts,
        CancellationToken ct)
    {
        var result = new List<AIContent>(parts.Count);
        foreach (var part in parts)
        {
            result.Add(part.Type switch
            {
                "localImage" when part.Path is { } path => await ResolveQueuedLocalImageAsync(path, part.MimeType, part.FileName, ct),
                "image" when part.Url is { } url => await ResolveQueuedRemoteImageAsync(url, ct),
                _ => part.ToAIContent()
            });
        }
        return result;
    }

    private static async Task<AIContent> ResolveQueuedLocalImageAsync(
        string path,
        string? mimeTypeHint,
        string? fileNameHint,
        CancellationToken ct)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var data = new DataContent(bytes, InferMediaType(path));
            data.AdditionalProperties ??= new AdditionalPropertiesDictionary();
            data.AdditionalProperties["localImage.path"] = path;
            if (!string.IsNullOrWhiteSpace(mimeTypeHint))
                data.AdditionalProperties["localImage.mimeType"] = mimeTypeHint.Trim();
            if (!string.IsNullOrWhiteSpace(fileNameHint))
                data.AdditionalProperties["localImage.fileName"] = fileNameHint.Trim();
            return data;
        }
        catch
        {
            return new TextContent($"[localImage:{path}]");
        }
    }

    private static async Task<AIContent> ResolveQueuedRemoteImageAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await QueuedInputHttpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "image/png";
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return new DataContent(bytes, mediaType);
        }
        catch
        {
            return new TextContent($"[image:{url}]");
        }
    }

    private static string InferMediaType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/png"
        };
    }

    private void RecordTurnTokenUsage(SessionThread thread, SessionTurn turn)
    {
        if (tokenUsageStore == null || turn.TokenUsage == null || string.IsNullOrWhiteSpace(turn.OriginChannel))
            return;

        var initiator = turn.Initiator;
        var hasSubjectUser = !string.IsNullOrWhiteSpace(initiator?.UserId);
        var subjectKind = hasSubjectUser ? TokenUsageSubjectKinds.User : TokenUsageSubjectKinds.Thread;
        var subjectId = hasSubjectUser
            ? initiator!.UserId!
            : thread.Id;
        var subjectLabel = hasSubjectUser
            ? initiator!.UserName ?? initiator.UserId!
            : thread.DisplayName ?? thread.Id;
        var hasGroupContext = !string.IsNullOrWhiteSpace(initiator?.GroupId);

        tokenUsageStore.Record(new TokenUsageRecord
        {
            Timestamp = turn.CompletedAt ?? DateTimeOffset.UtcNow,
            SourceId = turn.OriginChannel!,
            SourceMode = TokenUsageSourceModes.ServerManaged,
            SubjectKind = subjectKind,
            SubjectId = subjectId,
            SubjectLabel = subjectLabel,
            ContextKind = hasGroupContext ? TokenUsageContextKinds.Group : null,
            ContextId = hasGroupContext ? initiator!.GroupId : null,
            ContextLabel = hasGroupContext ? initiator!.GroupId : null,
            ThreadId = thread.Id,
            SessionKey = thread.Id,
            InputTokens = turn.TokenUsage.InputTokens,
            OutputTokens = turn.TokenUsage.OutputTokens
        });
    }

    private async Task EnsurePerThreadAgentIfMissingAsync(
        string threadId, SessionThread thread, CancellationToken ct)
    {
        if (!_forcePerThreadAgents && thread.Configuration == null && channelRuntimeToolProvider == null)
            return;

        using (await AcquireThreadAgentLockAsync(threadId, ct))
        {
            if (!_threadAgents.ContainsKey(threadId))
                _threadAgents[threadId] = await BuildAgentForThreadAsync(thread, ct);
        }
    }

    private async Task PersistThreadStatusAsync(SessionThread thread, CancellationToken ct)
    {
        await PersistThreadIfMaterializedAsync(thread, ct);
    }

    private ThreadEventBroker GetOrCreateBroker(string threadId) =>
        _threadEventBrokers.GetOrAdd(threadId, static id => new ThreadEventBroker(id));

    /// <summary>
    /// Returns or creates an <see cref="AgentModeManager"/> for the given thread,
    /// ensuring the mode manager reflects the requested <paramref name="mode"/>.
    /// </summary>
    private AgentModeManager GetOrCreateModeManager(string threadId, AgentMode mode)
    {
        return _threadModeManagers.AddOrUpdate(
            threadId,
            _ =>
            {
                var mm = new AgentModeManager();
                if (mode != AgentMode.Agent) mm.SwitchMode(mode);
                return mm;
            },
            (_, existing) =>
            {
                if (existing.CurrentMode != mode) existing.SwitchMode(mode);
                return existing;
            });
    }

    private static string ResolveApprovalSource(string? channelName) =>
        channelName?.ToLowerInvariant() ?? "console";

    private static List<UserMessageImage> ExtractUserMessageImages(IList<AIContent> content)
    {
        var images = new List<UserMessageImage>();
        foreach (var part in content.OfType<DataContent>())
        {
            if (part.AdditionalProperties == null)
                continue;
            if (!TryGetStringProperty(part.AdditionalProperties, AppServer.AppServerRequestHandler.LocalImagePathMetadataKey, out var path))
                continue;
            var image = new UserMessageImage
            {
                Path = path,
                MimeType = TryGetStringProperty(
                    part.AdditionalProperties,
                    AppServer.AppServerRequestHandler.LocalImageMimeTypeMetadataKey,
                    out var mimeType)
                    ? mimeType
                    : null,
                FileName = TryGetStringProperty(
                    part.AdditionalProperties,
                    AppServer.AppServerRequestHandler.LocalImageFileNameMetadataKey,
                    out var fileName)
                    ? fileName
                    : null
            };
            images.Add(image);
        }
        return images;
    }

    private static bool TryGetStringProperty(
        IReadOnlyDictionary<string, object?> properties,
        string key,
        out string value)
    {
        value = string.Empty;
        if (!properties.TryGetValue(key, out var raw) || raw == null)
            return false;
        var text = raw as string ?? raw.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return false;
        value = text.Trim();
        return true;
    }

    private static void RegisterCommandExecutionIfNeeded(
        FunctionCallContent functionCall,
        SessionTurn turn,
        Func<int> nextItemSeq,
        SessionEventChannel eventChannel,
        bool supportsCommandExecutionStreaming,
        string defaultWorkspacePath)
    {
        if (!supportsCommandExecutionStreaming)
            return;

        if (!string.Equals(functionCall.Name, "Exec", StringComparison.Ordinal))
            return;

        if (CommandExecutionRuntimeScope.Current is not { } runtime)
            return;

        var args = functionCall.Arguments;
        var command = args != null && args.TryGetValue("command", out var commandObj)
            ? commandObj?.ToString()
            : null;
        if (string.IsNullOrWhiteSpace(command))
            return;

        var workingDirectory = args != null && args.TryGetValue("workingDir", out var cwdObj)
            ? cwdObj?.ToString()
            : null;
        workingDirectory = !string.IsNullOrWhiteSpace(workingDirectory)
            ? Path.GetFullPath(workingDirectory)
            : defaultWorkspacePath;

        var item = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(nextItemSeq()),
            TurnId = turn.Id,
            Type = ItemType.CommandExecution,
            Status = ItemStatus.Started,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new CommandExecutionPayload
            {
                CallId = functionCall.CallId,
                Command = command,
                WorkingDirectory = workingDirectory,
                Source = "host",
                Status = "inProgress",
                AggregatedOutput = string.Empty
            }
        };
        turn.Items.Add(item);
        eventChannel.EmitItemStarted(item);
        runtime.RegisterPending(new PendingCommandExecutionRegistration
        {
            CallId = functionCall.CallId,
            Command = command,
            WorkingDirectory = workingDirectory,
            Source = "host",
            Item = item
        });
    }

    private static bool IsPromptTooLongError(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var msg = current.Message;
            if (msg.Contains("prompt_too_long", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("context_length_exceeded", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("maximum context length", StringComparison.OrdinalIgnoreCase)
                || msg.Contains("context window", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static void FailTurn(SessionTurn turn, SessionEventChannel channel, string errorMsg)
    {
        turn.Status = TurnStatus.Failed;
        turn.Error = errorMsg;
        turn.CompletedAt = DateTimeOffset.UtcNow;
        channel.EmitTurnFailed(turn, errorMsg);
    }

    private static bool EndsWithSuccessfulCreatePlanInPlanMode(SessionThread thread, SessionTurn turn)
    {
        if (!string.Equals(thread.Configuration?.Mode, "plan", StringComparison.OrdinalIgnoreCase))
            return false;

        for (var idx = turn.Items.Count - 1; idx >= 0; idx--)
        {
            if (turn.Items[idx].Payload is not ToolCallPayload toolCall)
                continue;

            if (!string.Equals(toolCall.ToolName, "CreatePlan", StringComparison.Ordinal))
                return false;

            return turn.Items
                .Where(item => item.Payload is ToolResultPayload)
                .Select(item => item.Payload as ToolResultPayload)
                .Any(result =>
                    result != null
                    && string.Equals(result.CallId, toolCall.CallId, StringComparison.Ordinal)
                    && result.Success);
        }

        return false;
    }

    private void TryScheduleMemoryConsolidation(
        string threadId,
        SessionThread thread,
        SessionTurn turn,
        AgentSession session,
        SessionEventChannel eventChannel,
        Func<int> nextItemSequence)
    {
        var consolidator = agentFactory.Consolidator;
        var memoryConfig = _appConfigMonitor?.Current.Memory
            ?? agentFactory.ToolProviderContext.Config.Memory;

        if (consolidator is null || !memoryConfig.AutoConsolidateEnabled)
            return;

        var interval = Math.Max(1, memoryConfig.ConsolidateEveryNTurns);
        var count = _turnsSinceConsolidation.AddOrUpdate(
            threadId,
            1,
            static (_, previous) => previous + 1);

        if (count < interval)
            return;

        var history = SnapshotSessionHistoryForConsolidation(session, thread);
        if (history.Count == 0)
            return;

        _turnsSinceConsolidation[threadId] = 0;
        eventChannel.EmitSystemEvent("consolidating");

        var broker = GetOrCreateBroker(threadId);
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await consolidator.ConsolidateAsync(history);
                switch (result.Outcome)
                {
                    case MemoryConsolidationOutcome.Succeeded:
                        await AppendMemoryConsolidationNoticeAsync(
                            threadId,
                            thread,
                            turn,
                            nextItemSequence,
                            broker);
                        broker.PublishSystemEvent("consolidated");
                        break;

                    case MemoryConsolidationOutcome.Skipped:
                        broker.PublishSystemEvent("consolidationSkipped", message: result.Message);
                        break;

                    case MemoryConsolidationOutcome.Failed:
                        logger?.LogWarning(
                            "Memory consolidation failed for thread {ThreadId}: {Message}",
                            threadId,
                            result.Message);
                        broker.PublishSystemEvent("consolidationFailed", message: result.Message);
                        break;
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Memory consolidation failed for thread {ThreadId}", threadId);
                broker.PublishSystemEvent("consolidationFailed", message: ex.Message);
            }
        });
    }

    private static IReadOnlyList<ChatMessage> SnapshotSessionHistoryForConsolidation(
        AgentSession session,
        SessionThread thread)
    {
        var chatHistory = session.GetService<ChatHistoryProvider>();
        if (chatHistory is InMemoryChatHistoryProvider provider)
        {
            var messages = provider.GetMessages(session).ToList();
            if (messages.Count > 0)
                return messages;
        }

        var fallback = new List<ChatMessage>();
        foreach (var turn in thread.Turns)
        {
            foreach (var item in turn.Items)
            {
                if (item.Type == ItemType.UserMessage && item.AsUserMessage is { Text: { } userText } &&
                    !string.IsNullOrWhiteSpace(userText))
                {
                    fallback.Add(new ChatMessage(ChatRole.User, userText.Trim()));
                }
                else if (item.Type == ItemType.AgentMessage && item.AsAgentMessage is { Text: { } agentText } &&
                         !string.IsNullOrWhiteSpace(agentText))
                {
                    fallback.Add(new ChatMessage(ChatRole.Assistant, agentText.Trim()));
                }
            }
        }

        return fallback;
    }

    private async Task TrySaveThreadAsync(SessionThread thread)
    {
        if (IsPendingPermanentDeletion(thread.Id))
            return;

        try
        {
            await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to persist thread state for thread {ThreadId}", thread.Id);
        }
    }

    private async Task TrySaveSessionAsync(AIAgent agent, AgentSession session, string threadId)
    {
        if (IsPendingPermanentDeletion(threadId))
            return;

        if (!_threads.TryGetValue(threadId, out var thread) || thread.HistoryMode != HistoryMode.Server)
            return;
            
        try
        {
            await persistence.SaveSessionAsync(agent, session, threadId, ct: CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to save agent session for thread {ThreadId}", threadId);
        }
    }

    private async Task TryRebuildAndSaveSessionAsync(AIAgent agent, string threadId)
    {
        if (IsPendingPermanentDeletion(threadId))
            return;

        if (!_threads.TryGetValue(threadId, out var thread) || thread.HistoryMode != HistoryMode.Server)
            return;

        try
        {
            await persistence.RebuildAndSaveSessionFromThreadAsync(agent, threadId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to rebuild agent session for thread {ThreadId}; clearing stale session.", threadId);
            try
            {
                persistence.DeleteSessionFile(threadId);
            }
            catch (Exception deleteEx)
            {
                logger?.LogWarning(deleteEx, "Failed to clear stale agent session for thread {ThreadId}", threadId);
            }
        }
    }

    private bool IsMaterialized(string threadId) => _materializedThreads.ContainsKey(threadId);

    private bool IsPendingPermanentDeletion(string threadId) => _threadsPendingPermanentDeletion.ContainsKey(threadId);

    private async Task PersistThreadWithMaterializationAsync(SessionThread thread, CancellationToken ct)
    {
        if (IsPendingPermanentDeletion(thread.Id))
            return;

        await persistence.SaveThreadAsync(thread, ct);
        _materializedThreads[thread.Id] = 0;
    }

    private async Task PersistThreadIfMaterializedAsync(SessionThread thread, CancellationToken ct)
    {
        if (IsPendingPermanentDeletion(thread.Id))
            return;
        if (!IsMaterialized(thread.Id))
            return;
        await PersistThreadWithMaterializationAsync(thread, ct);
    }

    private static SessionItem CreateErrorItem(
        SessionTurn turn, int seq, string message, string code, bool fatal)
    {
        return new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(seq),
            TurnId = turn.Id,
            Type = ItemType.Error,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new ErrorPayload { Message = message, Code = code, Fatal = fatal }
        };
    }

    private static SessionItem CreateCompactionNoticeItem(
        SessionTurn turn,
        int seq,
        string trigger,
        CompactionStatus status)
    {
        var mode = status.Outcome == CompactionOutcome.Micro ? "micro" : "partial";
        return new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(seq),
            TurnId = turn.Id,
            Type = ItemType.SystemNotice,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new SystemNoticePayload
            {
                Kind = "compacted",
                Trigger = trigger,
                Mode = mode,
                TokensBefore = status.ThresholdBefore.Tokens,
                TokensAfter = status.ThresholdAfter.Tokens,
                PercentLeftAfter = status.ThresholdAfter.PercentLeft,
                ClearedToolResults = status.ClearedToolResults
            }
        };
    }

    private static SessionItem CreateMemoryConsolidationNoticeItem(SessionTurn turn, int seq)
    {
        return new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(seq),
            TurnId = turn.Id,
            Type = ItemType.SystemNotice,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new SystemNoticePayload
            {
                Kind = "memoryConsolidated"
            }
        };
    }

    private async Task AppendMemoryConsolidationNoticeAsync(
        string threadId,
        SessionThread thread,
        SessionTurn turn,
        Func<int> nextItemSequence,
        ThreadEventBroker broker)
    {
        if (IsPendingPermanentDeletion(threadId))
            return;

        using var gateLock = await sessionGate.AcquireAsync(threadId, CancellationToken.None);
        if (IsPendingPermanentDeletion(threadId))
            return;

        var noticeItem = CreateMemoryConsolidationNoticeItem(turn, nextItemSequence());
        turn.Items.Add(noticeItem);
        broker.PublishItemEvent(SessionEventType.ItemStarted, turn.Id, noticeItem);
        broker.PublishItemEvent(SessionEventType.ItemCompleted, turn.Id, noticeItem);
        await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
    }

    private async Task<AIAgent> BuildAgentForThreadAsync(
        SessionThread thread,
        CancellationToken ct)
    {
        var previousSessionKey = TracingChatClient.CurrentSessionKey;
        TracingChatClient.CurrentSessionKey = thread.Id;
        try
        {
            return await BuildAgentForThreadCoreAsync(thread, ct);
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = previousSessionKey;
        }
    }

    private async Task<AIAgent> BuildAgentForThreadCoreAsync(
        SessionThread thread,
        CancellationToken ct)
    {
        var threadId = thread.Id;
        var config = thread.Configuration ?? new ThreadConfiguration();
        var mode = config.Mode.Equals("plan", StringComparison.OrdinalIgnoreCase)
            ? AgentMode.Plan
            : AgentMode.Agent;
        var mm = GetOrCreateModeManager(threadId, mode);
        var baseCtx = agentFactory.ToolProviderContext;
        var agentControlToolAccess = ResolveAgentControlToolAccess(thread);
        var effectiveMainModel = baseCtx.OpenAIClientProvider.ResolveMainModel(baseCtx.Config, config.Model);
        var threadChatClient = ResolveThreadChatClient(baseCtx, effectiveMainModel);
        var externalCliSessionStore = new ThreadExternalCliSessionStore(thread);
        var threadBaseContext = CloneContextWithChatClient(
            baseCtx,
            threadChatClient,
            effectiveMainModel,
            externalCliSessionStore,
            thread);

        ToolProviderContext? scopedContext = null;
        if (!string.IsNullOrEmpty(config.WorkspaceOverride))
        {
            var craftPath = Path.Combine(config.WorkspaceOverride, ".craft");
            Directory.CreateDirectory(craftPath);

            var scopedMemory = new MemoryStore(craftPath);
            var scopedSkills = new SkillsLoader(craftPath);

            scopedContext = new ToolProviderContext
            {
                Config = baseCtx.Config,
                ChatClient = threadChatClient,
                OpenAIClientProvider = baseCtx.OpenAIClientProvider,
                EffectiveMainModel = effectiveMainModel,
                WorkspacePath = config.WorkspaceOverride,
                BotPath = craftPath,
                MemoryStore = scopedMemory,
                SkillsLoader = scopedSkills,
                ApprovalService = baseCtx.ApprovalService,
                PathBlacklist = new PathBlacklist([]),
                BackgroundTerminalService = baseCtx.BackgroundTerminalService,
                TraceCollector = baseCtx.TraceCollector,
                LspServerManager = baseCtx.LspServerManager,
                AcpExtensionProxy = baseCtx.AcpExtensionProxy,
                NodeReplProxy = baseCtx.NodeReplProxy,
                CronTools = baseCtx.CronTools,
                DeferredToolRegistry = baseCtx.DeferredToolRegistry,
                ExternalCliSessionStore = externalCliSessionStore,
                AutomationTaskDirectory = config.AutomationTaskDirectory,
                RequireApprovalOutsideWorkspace = config.RequireApprovalOutsideWorkspace,
                CurrentThreadId = thread.Id,
                CurrentThreadSource = thread.Source,
                CurrentOriginChannel = thread.OriginChannel,
                CurrentChannelContext = thread.ChannelContext,
                AgentControlToolAccess = agentControlToolAccess,
                AllowedAgentControlTools = ToSet(config.AllowedAgentControlTools),
                ToolAllowList = ToSet(config.ToolAllowList),
                ToolDenyList = ToSet(config.ToolDenyList),
                PromptProfile = config.PromptProfile,
                RoleInstructions = config.RoleInstructions
            };
        }

        List<AITool>? profileTools = null;
        if (!string.IsNullOrEmpty(config.ToolProfile))
        {
            if (toolProfileRegistry == null
                || !toolProfileRegistry.TryGet(config.ToolProfile, out var profileProviders)
                || profileProviders == null)
            {
                throw new InvalidOperationException($"Tool profile '{config.ToolProfile}' is not registered.");
            }

            var toolCtx = scopedContext ?? threadBaseContext;
            profileTools = agentFactory.CreateToolsFromProviders(profileProviders, toolCtx);
        }

        if (config.UseToolProfileOnly)
        {
            if (profileTools is not { Count: > 0 })
                throw new InvalidOperationException("UseToolProfileOnly requires a registered ToolProfile with at least one tool.");
            var toolCtx2 = scopedContext ?? threadBaseContext;
            ApplyThreadToolFilters(profileTools, config);
            return agentFactory.CreateAgentWithTools(profileTools, mm, toolCtx2, config.AgentInstructions);
        }

        if (config.McpServers is not { Length: > 0 })
        {
            if (scopedContext != null)
            {
                var tools = agentFactory.CreateToolsForMode(mode, scopedContext);
                if (profileTools != null)
                    tools.AddRange(profileTools);
                AppendChannelTools(tools, thread);
                ApplyThreadToolFilters(tools, config);
                return agentFactory.CreateAgentWithTools(tools, mm, scopedContext);
            }

            if (profileTools != null)
            {
                var tools = agentFactory.CreateToolsForMode(mode, threadBaseContext);
                tools.AddRange(profileTools);
                AppendChannelTools(tools, thread);
                ApplyThreadToolFilters(tools, config);
                return agentFactory.CreateAgentWithTools(tools, mm, threadBaseContext);
            }

            var modeTools = agentFactory.CreateToolsForMode(mode, threadBaseContext);
            AppendChannelTools(modeTools, thread);
            ApplyThreadToolFilters(modeTools, config);
            return agentFactory.CreateAgentWithTools(modeTools, mm, threadBaseContext);
        }

        // Dispose previous per-thread MCP manager if replacing config
        if (_threadMcpManagers.TryRemove(threadId, out var oldManager))
            await oldManager.DisposeAsync();

        var mcpManager = new McpClientManager();
        await mcpManager.ConnectAsync(config.McpServers, ct);
        _threadMcpManagers[threadId] = mcpManager;

        if (scopedContext != null)
        {
            var effectiveContext = new ToolProviderContext
            {
                Config = scopedContext.Config,
                ChatClient = scopedContext.ChatClient,
                OpenAIClientProvider = scopedContext.OpenAIClientProvider,
                EffectiveMainModel = scopedContext.EffectiveMainModel,
                WorkspacePath = scopedContext.WorkspacePath,
                BotPath = scopedContext.BotPath,
                MemoryStore = scopedContext.MemoryStore,
                SkillsLoader = scopedContext.SkillsLoader,
                ApprovalService = scopedContext.ApprovalService,
                PathBlacklist = scopedContext.PathBlacklist,
                BackgroundTerminalService = scopedContext.BackgroundTerminalService,
                TraceCollector = scopedContext.TraceCollector,
                McpClientManager = mcpManager,
                LspServerManager = scopedContext.LspServerManager,
                AcpExtensionProxy = scopedContext.AcpExtensionProxy,
                NodeReplProxy = scopedContext.NodeReplProxy,
                CronTools = scopedContext.CronTools,
                DeferredToolRegistry = scopedContext.DeferredToolRegistry,
                ExternalCliSessionStore = scopedContext.ExternalCliSessionStore,
                AutomationTaskDirectory = scopedContext.AutomationTaskDirectory,
                RequireApprovalOutsideWorkspace = scopedContext.RequireApprovalOutsideWorkspace,
                CurrentThreadId = scopedContext.CurrentThreadId,
                CurrentThreadSource = scopedContext.CurrentThreadSource,
                CurrentOriginChannel = scopedContext.CurrentOriginChannel,
                CurrentChannelContext = scopedContext.CurrentChannelContext,
                AgentControlToolAccess = scopedContext.AgentControlToolAccess,
                AllowedAgentControlTools = scopedContext.AllowedAgentControlTools,
                ToolAllowList = scopedContext.ToolAllowList,
                ToolDenyList = scopedContext.ToolDenyList,
                PromptProfile = scopedContext.PromptProfile,
                RoleInstructions = scopedContext.RoleInstructions
            };

            var modeTools = agentFactory.CreateToolsForMode(mode, effectiveContext);
            if (profileTools != null)
                modeTools.AddRange(profileTools);
            modeTools.AddRange(mcpManager.Tools);
            AppendChannelTools(modeTools, thread);
            ApplyThreadToolFilters(modeTools, config);
            return agentFactory.CreateAgentWithTools(modeTools, mm, effectiveContext);
        }

        var toolsWithMcp = agentFactory.CreateToolsForMode(mode, threadBaseContext);
        if (profileTools != null)
            toolsWithMcp.AddRange(profileTools);
        toolsWithMcp.AddRange(mcpManager.Tools);
        AppendChannelTools(toolsWithMcp, thread);
        ApplyThreadToolFilters(toolsWithMcp, config);
        return agentFactory.CreateAgentWithTools(toolsWithMcp, mm, threadBaseContext);
    }

    private static void ApplyThreadToolFilters(List<AITool> tools, ThreadConfiguration config)
    {
        var allow = config.ToolAllowList?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
        var deny = config.ToolDenyList?
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        if (allow is { Count: > 0 })
            tools.RemoveAll(tool => !allow.Contains(tool.Name));

        if (deny is { Count: > 0 })
            tools.RemoveAll(tool => deny.Contains(tool.Name));
    }

    private void AppendChannelTools(List<AITool> tools, SessionThread thread)
    {
        if (channelRuntimeToolProvider != null)
        {
            var reservedNames = new HashSet<string>(tools.Select(t => t.Name), StringComparer.Ordinal);
            var channelTools = channelRuntimeToolProvider.CreateToolsForThread(thread, reservedNames);
            if (channelTools.Count > 0)
                tools.AddRange(channelTools);
        }

        var pluginFunctionToolNames = tools
            .OfType<IPluginFunctionTool>()
            .Select(tool => tool.PluginFunctionDescriptor?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);

        if (pluginFunctionToolNames.Count == 0)
        {
            _threadPluginFunctionToolNames.TryRemove(thread.Id, out _);
            return;
        }

        _threadPluginFunctionToolNames[thread.Id] = pluginFunctionToolNames;
    }

    private IReadOnlySet<string> GetPluginFunctionToolNames(string threadId)
        => _threadPluginFunctionToolNames.GetValueOrDefault(threadId, EmptyPluginFunctionToolNames);

    private static bool IsPluginFunctionTool(IReadOnlySet<string> pluginFunctionToolNames, string? toolName)
        => !string.IsNullOrWhiteSpace(toolName) && pluginFunctionToolNames.Contains(toolName);

    private static OpenAI.Chat.ChatClient ResolveThreadChatClient(ToolProviderContext baseContext, string effectiveMainModel) =>
        baseContext.OpenAIClientProvider.TryGetChatClient(baseContext.Config, effectiveMainModel, out var chatClient)
            ? chatClient!
            : baseContext.ChatClient;

    private static ToolProviderContext CloneContextWithChatClient(
        ToolProviderContext source,
        OpenAI.Chat.ChatClient chatClient,
        string effectiveMainModel,
        IExternalCliSessionStore? externalCliSessionStore = null,
        SessionThread? thread = null)
    {
        var cloned = new ToolProviderContext
        {
            Config = source.Config,
            ChatClient = chatClient,
            OpenAIClientProvider = source.OpenAIClientProvider,
            EffectiveMainModel = effectiveMainModel,
            WorkspacePath = source.WorkspacePath,
            BotPath = source.BotPath,
            MemoryStore = source.MemoryStore,
            SkillsLoader = source.SkillsLoader,
            ApprovalService = source.ApprovalService,
            PathBlacklist = source.PathBlacklist,
            BackgroundTerminalService = source.BackgroundTerminalService,
            CronTools = source.CronTools,
            McpClientManager = source.McpClientManager,
            LspServerManager = source.LspServerManager,
            TraceCollector = source.TraceCollector,
            AcpExtensionProxy = source.AcpExtensionProxy,
            NodeReplProxy = source.NodeReplProxy,
            ExternalCliSessionStore = externalCliSessionStore ?? source.ExternalCliSessionStore,
            AgentFileSystem = source.AgentFileSystem,
            AutomationTaskDirectory = source.AutomationTaskDirectory,
            RequireApprovalOutsideWorkspace = source.RequireApprovalOutsideWorkspace,
            DeferredToolRegistry = source.DeferredToolRegistry,
            CurrentThreadId = thread?.Id ?? source.CurrentThreadId,
            CurrentThreadSource = thread?.Source ?? source.CurrentThreadSource,
            CurrentOriginChannel = thread?.OriginChannel ?? source.CurrentOriginChannel,
            CurrentChannelContext = thread?.ChannelContext ?? source.CurrentChannelContext,
            AgentControlToolAccess = thread == null
                ? source.AgentControlToolAccess
                : ResolveAgentControlToolAccess(thread),
            AllowedAgentControlTools = thread == null ? source.AllowedAgentControlTools : ToSet(thread.Configuration?.AllowedAgentControlTools),
            ToolAllowList = thread == null ? source.ToolAllowList : ToSet(thread.Configuration?.ToolAllowList),
            ToolDenyList = thread == null ? source.ToolDenyList : ToSet(thread.Configuration?.ToolDenyList),
            PromptProfile = thread?.Configuration?.PromptProfile ?? source.PromptProfile,
            RoleInstructions = thread?.Configuration?.RoleInstructions ?? source.RoleInstructions
        };
        return cloned;
    }

    private static IReadOnlySet<string>? ToSet(IEnumerable<string>? values)
    {
        if (values == null)
            return null;

        var set = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
        return set.Count == 0 ? null : set;
    }

    internal static bool TryRemoveStreamingToolCallIndexByItemReference(
        Dictionary<int, SessionItem>? streamingToolCallItemsByIndex,
        SessionItem targetItem)
    {
        if (streamingToolCallItemsByIndex == null)
            return false;

        int? matchedIndex = null;
        foreach (var kvp in streamingToolCallItemsByIndex)
        {
            if (!ReferenceEquals(kvp.Value, targetItem))
                continue;
            matchedIndex = kvp.Key;
            break;
        }

        return matchedIndex.HasValue && streamingToolCallItemsByIndex.Remove(matchedIndex.Value);
    }
}
