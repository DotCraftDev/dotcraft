using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Context.Compaction;
using DotCraft.Hooks;
using DotCraft.Memory;
using DotCraft.Mcp;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Logging;
using DotCraft.Tracing;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.ClientModel;

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
    TimeSpan? approvalTimeout = null,
    ILogger<SessionService>? logger = null,
    ApprovalStore? approvalStore = null,
    IToolProfileRegistry? toolProfileRegistry = null,
    SessionStreamDebugLogger? sessionStreamDebugLogger = null)
    : ISessionService
{
    private readonly IToolProfileRegistry? _toolProfileRegistry = toolProfileRegistry;
    private readonly SessionStreamDebugLogger? _sessionStreamDebugLogger = sessionStreamDebugLogger;

    private readonly TimeSpan _approvalTimeout = approvalTimeout ?? TimeSpan.FromMinutes(5);

    // In-memory state
    private readonly ConcurrentDictionary<string, SessionThread> _threads = new();
    private readonly ConcurrentDictionary<string, AIAgent> _threadAgents = new();
    private readonly ConcurrentDictionary<TurnKey, SessionApprovalService> _pendingApprovals = new();
    private readonly ConcurrentDictionary<TurnKey, CancellationTokenSource> _runningTurns = new();
    private readonly ConcurrentDictionary<string, McpClientManager> _threadMcpManagers = new();
    private readonly ConcurrentDictionary<string, AgentModeManager> _threadModeManagers = new();
    private readonly ConcurrentDictionary<string, ThreadEventBroker> _threadEventBrokers = new();
    private readonly ConcurrentDictionary<string, byte> _materializedThreads = new();
    private readonly ConcurrentDictionary<string, byte> _threadsPendingPermanentDeletion = new();
    private readonly ConcurrentDictionary<string, IReadOnlySet<string>> _threadExternalChannelToolNames = new();
    private static readonly IReadOnlySet<string> EmptyExternalToolNames = new HashSet<string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string>? ThreadDeletedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }

    /// <inheritdoc />
    public Action<string, SessionThreadRuntimeSignal>? ThreadRuntimeSignalForBroadcast { get; set; }

    /// <inheritdoc />
    public ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId)
    {
        if (string.IsNullOrWhiteSpace(threadId))
            return null;

        var tracker = agentFactory.TryGetTokenTracker(threadId);
        if (tracker is null)
            return null;

        var pipeline = agentFactory.CompactionPipeline;
        var threshold = pipeline.EvaluateThreshold(tracker.LastInputTokens);

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
        CancellationToken ct = default)
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
            DisplayName = displayName
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
            _threadAgents[thread.Id] = await BuildAgentForThreadAsync(thread, ct);

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
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        if (thread.Status == ThreadStatus.Archived) return;
        var previousStatus = thread.Status;
        thread.Status = ThreadStatus.Archived;
        // Release per-thread agent if any
        _threadAgents.TryRemove(threadId, out _);
        _threadModeManagers.TryRemove(threadId, out _);
        _threadExternalChannelToolNames.TryRemove(threadId, out _);
        await PersistThreadStatusAsync(thread, ct);
        GetOrCreateBroker(threadId).PublishThreadStatusChanged(previousStatus, thread.Status);
    }

    /// <inheritdoc/>
    public async Task UnarchiveThreadAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        if (thread.Status == ThreadStatus.Active) return;
        var previousStatus = thread.Status;
        thread.Status = ThreadStatus.Active;
        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await PersistThreadStatusAsync(thread, ct);
        GetOrCreateBroker(threadId).PublishThreadStatusChanged(previousStatus, thread.Status);
    }

    /// <inheritdoc/>
    public async Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default)
    {
        var normalizedThreadId = threadId.Trim();
        if (normalizedThreadId.Length == 0)
            throw new ArgumentException("threadId is required.", nameof(threadId));

        _threadsPendingPermanentDeletion[normalizedThreadId] = 0;

        try
        {
            // Cancel any running or approval-pending turns for this thread
            if (_threads.TryGetValue(normalizedThreadId, out var thread))
            {
                foreach (var turn in thread.Turns.Where(t => t.Status is TurnStatus.Running or TurnStatus.WaitingApproval))
                {
                    var key = new TurnKey(normalizedThreadId, turn.Id);
                    if (_runningTurns.TryRemove(key, out var turnCts))
                        await turnCts.CancelAsync();
                    _pendingApprovals.TryRemove(key, out _);
                }
            }

            await persistence.DeleteThreadCascadeAsync(normalizedThreadId, ct);

            // Remove all in-memory state only after persistence succeeds.
            _threads.TryRemove(normalizedThreadId, out _);
            _threadAgents.TryRemove(normalizedThreadId, out _);
            _threadModeManagers.TryRemove(normalizedThreadId, out _);
            _threadEventBrokers.TryRemove(normalizedThreadId, out _);
            _materializedThreads.TryRemove(normalizedThreadId, out _);
            _threadExternalChannelToolNames.TryRemove(normalizedThreadId, out _);
            if (_threadMcpManagers.TryRemove(normalizedThreadId, out var mcpManager))
                await mcpManager.DisposeAsync();

            ThreadDeletedForBroadcast?.Invoke(normalizedThreadId);
        }
        catch
        {
            _threadsPendingPermanentDeletion.TryRemove(normalizedThreadId, out _);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        bool includeArchived = false,
        IReadOnlyList<string>? crossChannelOrigins = null,
        CancellationToken ct = default)
    {
        var all = await persistence.LoadIndexAsync(ct);
        var hasCross = crossChannelOrigins is { Count: > 0 };
        var mergedById = new Dictionary<string, ThreadSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var summary in all)
            mergedById[summary.Id] = summary;
        foreach (var thread in _threads.Values)
            mergedById[thread.Id] = ThreadSummary.FromThread(thread);
        var merged = mergedById.Values;

        return merged
            .Where(s =>
            {
                if (!(includeArchived || s.Status != ThreadStatus.Archived))
                    return false;
                if (!string.Equals(s.WorkspacePath, identity.WorkspacePath, StringComparison.OrdinalIgnoreCase))
                    return false;

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
            if (_sessionStreamDebugLogger == null || evt.EventType != SessionEventType.ItemDelta)
                return;
            if (!_sessionStreamDebugLogger.ShouldCapture(evt.ThreadId, evt.TurnId))
                return;

            if (evt.DeltaPayload is { } agentDelta)
            {
                _sessionStreamDebugLogger.Log(
                    "session_event_delta",
                    evt.ThreadId,
                    evt.TurnId,
                    new
                    {
                        itemId = evt.ItemId,
                        deltaKind = agentDelta.DeltaKind,
                        deltaChars = agentDelta.TextDelta.Length,
                        deltaText = _sessionStreamDebugLogger.IncludeFullText ? agentDelta.TextDelta : null
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
            AgentSession? session = null;
            TokenTracker? tokenTracker = null;
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
                var agent = _threadAgents.GetValueOrDefault(threadId, defaultAgent);

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
                        foreach (var msg in messages)
                            memProvider.Add(msg);
                    }
                }
                else
                {
                    session = await persistence.LoadOrCreateSessionAsync(agent, threadId, executionCt);
                }

                // Step 5c: Append runtime context to the multimodal content list
                var userMessage = new ChatMessage(ChatRole.User, content.AppendRuntimeContext());

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
                var approvalPolicy = thread.Configuration?.ApprovalPolicy ?? ApprovalPolicy.Default;
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
                SessionItem? agentMessageItem = null;
                SessionItem? reasoningItem = null;
                var agentText = string.Empty;
                var reasoningText = string.Empty;
                var agentDeltaIndex = 0;
                Dictionary<int, SessionItem>? streamingToolCallItemsByIndex = null;
                Dictionary<int, string>? streamingToolNameByIndex = null;
                Dictionary<string, SessionItem>? streamingToolCallItemsByCallId = null;
                var externalChannelCallIds = new HashSet<string>(StringComparer.Ordinal);
                long inputTokens = 0, outputTokens = 0;
                long lastUsageInput = 0, lastUsageOutput = 0;
                var externalChannelToolNames = GetExternalChannelToolNames(threadId);

                // SubAgent progress aggregator: lazily created when SpawnSubagent tool calls appear
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

                using var externalChannelToolScope = ExternalChannelToolExecutionScope.Set(
                    new ExternalChannelToolExecutionContext
                    {
                        ThreadId = threadId,
                        TurnId = turn.Id,
                        OriginChannel = turnOriginChannel,
                        ChannelContext = turn.Initiator?.ChannelContext,
                        SenderId = turn.Initiator?.UserId,
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
                void FinalizeStreamingAgentMessage()
                {
                    // Finalize the current AgentMessage so any subsequent
                    // text (post-tool response) starts a fresh item,
                    // preserving the natural interleaving in stored turns.
                    if (agentMessageItem == null)
                        return;

                    agentMessageItem.Payload = new AgentMessagePayload { Text = agentText };
                    agentMessageItem.Status = ItemStatus.Completed;
                    agentMessageItem.CompletedAt = DateTimeOffset.UtcNow;
                    eventChannel.EmitItemCompleted(agentMessageItem);
                    agentMessageItem = null;
                    agentText = string.Empty;
                }

                try
                {
                    await foreach (var update in agent.RunStreamingAsync(userMessage, session)
                        .WithCancellation(executionCt))
                    {
                        foreach (var responseContent in update.Contents)
                        {
                            switch (responseContent)
                            {
                                case TextContent tc:
                                    // Agent message text
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
                                    if (_sessionStreamDebugLogger?.ShouldCapture(threadId, turn.Id) == true)
                                    {
                                        _sessionStreamDebugLogger.Log(
                                            "agent_delta_source",
                                            threadId,
                                            turn.Id,
                                            new
                                            {
                                                itemId = agentMessageItem.Id,
                                                deltaIndex = agentDeltaIndex,
                                                chunkChars = chunk.Length,
                                                chunkText = _sessionStreamDebugLogger.IncludeFullText ? chunk : null,
                                                cumulativeChars = agentText.Length,
                                                cumulativeText = _sessionStreamDebugLogger.IncludeFullText ? agentText : null
                                            });
                                    }
                                    eventChannel.EmitItemDelta(agentMessageItem, new AgentMessageDelta { TextDelta = chunk });
                                    break;

                                case TextReasoningContent reasoning:
                                    if (ReasoningContentHelper.TryGetText(reasoning, out var rText))
                                    {
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
                                    if (IsExternalChannelTool(externalChannelToolNames, resolvedToolName))
                                        break;

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
                                    var isExternalChannelTool = IsExternalChannelTool(externalChannelToolNames, fc.Name);
                                    if (isExternalChannelTool && !string.IsNullOrWhiteSpace(fc.CallId))
                                        externalChannelCallIds.Add(fc.CallId);
                                    RegisterCommandExecutionIfNeeded(
                                        fc,
                                        turn,
                                        NextItemSeq,
                                        eventChannel,
                                        supportsCommandExecutionStreaming,
                                        effectiveWorkspacePath);
                                    if (isExternalChannelTool)
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

                                    // Track SubAgent progress when SpawnSubagent tool calls are detected
                                    if (string.Equals(fc.Name, "SpawnSubagent", StringComparison.Ordinal)
                                        && fc.Arguments != null)
                                    {
                                        var rawLabel = fc.Arguments.TryGetValue("label", out var labelObj)
                                            ? labelObj?.ToString()
                                            : null;
                                        var rawTask = fc.Arguments.TryGetValue("task", out var taskObj)
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
                                    if (!string.IsNullOrWhiteSpace(fr.CallId)
                                        && externalChannelCallIds.Remove(fr.CallId))
                                    {
                                        // External channel tool calls are represented by
                                        // externalChannelToolCall items emitted from the runtime adapter.
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
                                            Success = true
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
                                            eventChannel.EmitUsageDelta(
                                                deltaIn,
                                                deltaOut,
                                                totalInputTokens: tokenTracker.LastInputTokens,
                                                totalOutputTokens: outputTokens);
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

                // Step 5h: Finalize any still-streaming items (agentMessageItem is null if
                // it was already finalized after the last tool result).
                if (agentMessageItem != null)
                {
                    agentMessageItem.Payload = new AgentMessagePayload { Text = agentText };
                    agentMessageItem.Status = ItemStatus.Completed;
                    agentMessageItem.CompletedAt = DateTimeOffset.UtcNow;
                    eventChannel.EmitItemCompleted(agentMessageItem);
                }
                if (reasoningItem != null)
                {
                    reasoningItem.Payload = new ReasoningContentPayload { Text = reasoningText };
                    reasoningItem.Status = ItemStatus.Completed;
                    reasoningItem.CompletedAt = DateTimeOffset.UtcNow;
                    eventChannel.EmitItemCompleted(reasoningItem);
                }

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

                // Step 5k: Layered compaction pipeline
                //   - emit compactWarning / compactError as the usage nears the auto threshold
                //   - run MicroCompactor + PartialCompactor when auto threshold is exceeded
                //   - memory consolidation is driven by the pipeline itself so the prefix
                //     summarized by the LLM is exactly what lands in MEMORY.md / HISTORY.md
                {
                    var compactionPipeline = agentFactory.CompactionPipeline;
                    var threshold = compactionPipeline.EvaluateThreshold(tokenTracker.LastInputTokens);
                    if (!threshold.AboveAuto)
                    {
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
                    else
                    {
                        eventChannel.EmitSystemEvent(
                            "compacting",
                            percentLeft: threshold.PercentLeft,
                            tokenCount: threshold.Tokens);

                        var status = await compactionPipeline.TryAutoCompactAsync(
                            session!,
                            threadId,
                            tokenTracker.LastInputTokens,
                            thread.LastActiveAt,
                            CancellationToken.None);

                        switch (status.Outcome)
                        {
                            case CompactionOutcome.Micro:
                            case CompactionOutcome.Partial:
                                tokenTracker.Reset();
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
                                break;

                            case CompactionOutcome.Skipped:
                                eventChannel.EmitSystemEvent(
                                    "compactSkipped",
                                    message: status.FailureReason,
                                    percentLeft: status.ThresholdAfter.PercentLeft,
                                    tokenCount: status.ThresholdAfter.Tokens);
                                break;

                            case CompactionOutcome.Failed:
                                eventChannel.EmitSystemEvent(
                                    "compactFailed",
                                    message: status.FailureReason,
                                    percentLeft: status.ThresholdAfter.PercentLeft,
                                    tokenCount: status.ThresholdAfter.Tokens);
                                break;
                        }
                    }
                }

                // Step 5m: Release gate
                gateLock.Dispose();
                gateLock = null;

                // Steps 5n-5r: Complete Turn
                turn.Status = TurnStatus.Completed;
                turn.CompletedAt = DateTimeOffset.UtcNow;
                thread.LastActiveAt = DateTimeOffset.UtcNow;
                eventChannel.EmitTurnCompleted(turn);
                ThreadRuntimeSignalForBroadcast?.Invoke(
                    threadId,
                    EndsWithSuccessfulCreatePlanInPlanMode(thread, turn)
                        ? SessionThreadRuntimeSignal.TurnCompletedAwaitingPlanConfirmation
                        : SessionThreadRuntimeSignal.TurnCompleted);

                try
                {
                    await PersistThreadWithMaterializationAsync(thread, CancellationToken.None);
                    await TrySaveSessionAsync(agent, session, threadId);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Failed to persist thread state after turn completion for thread {ThreadId}", threadId);
                }
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                // Explicit CancelTurn call
                turn.Status = TurnStatus.Cancelled;
                turn.CompletedAt = DateTimeOffset.UtcNow;
                eventChannel.EmitTurnCancelled(turn, "Cancelled by request");
                ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnCancelled);
                await TrySaveThreadAsync(thread);
            }
            catch (OperationCanceledException)
            {
                // Caller cancellation
                turn.Status = TurnStatus.Cancelled;
                turn.CompletedAt = DateTimeOffset.UtcNow;
                eventChannel.EmitTurnCancelled(turn, "Caller cancelled");
                ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnCancelled);
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

                FailTurn(turn, eventChannel, reactiveMessage);
                ThreadRuntimeSignalForBroadcast?.Invoke(threadId, SessionThreadRuntimeSignal.TurnFailed);
                await TrySaveThreadAsync(thread);
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

    // =========================================================================
    // Configuration
    // =========================================================================

    /// <inheritdoc/>
    public async Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        thread.Configuration ??= new ThreadConfiguration();
        thread.Configuration.Mode = mode;

        _threadAgents[threadId] = await BuildAgentForThreadAsync(thread, ct);

        await PersistThreadWithMaterializationAsync(thread, ct);
    }

    /// <inheritdoc/>
    public async Task UpdateThreadConfigurationAsync(
        string threadId,
        ThreadConfiguration config,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        thread.Configuration = config;
        _threadAgents[threadId] = await BuildAgentForThreadAsync(thread, ct);
        await PersistThreadWithMaterializationAsync(thread, ct);
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

    private async Task EnsurePerThreadAgentIfMissingAsync(
        string threadId, SessionThread thread, CancellationToken ct)
    {
        if ((thread.Configuration != null || channelRuntimeToolProvider != null) && !_threadAgents.ContainsKey(threadId))
            _threadAgents[threadId] = await BuildAgentForThreadAsync(thread, ct);
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
            CallId = functionCall.CallId ?? string.Empty,
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
            var msg = current.Message ?? string.Empty;
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

    private async Task<AIAgent> BuildAgentForThreadAsync(
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
        var threadChatClient = ResolveThreadChatClient(baseCtx, config);
        var externalCliSessionStore = new ThreadExternalCliSessionStore(thread);
        var threadBaseContext = CloneContextWithChatClient(baseCtx, threadChatClient, externalCliSessionStore);

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
                WorkspacePath = config.WorkspaceOverride,
                BotPath = craftPath,
                MemoryStore = scopedMemory,
                SkillsLoader = scopedSkills,
                ApprovalService = baseCtx.ApprovalService,
                PathBlacklist = new PathBlacklist([]),
                TraceCollector = baseCtx.TraceCollector,
                LspServerManager = baseCtx.LspServerManager,
                AcpExtensionProxy = baseCtx.AcpExtensionProxy,
                CronTools = baseCtx.CronTools,
                DeferredToolRegistry = baseCtx.DeferredToolRegistry,
                ExternalCliSessionStore = externalCliSessionStore,
                AutomationTaskDirectory = config.AutomationTaskDirectory,
                RequireApprovalOutsideWorkspace = config.RequireApprovalOutsideWorkspace
            };
        }

        List<AITool>? profileTools = null;
        if (!string.IsNullOrEmpty(config.ToolProfile))
        {
            if (_toolProfileRegistry == null
                || !_toolProfileRegistry.TryGet(config.ToolProfile, out var profileProviders)
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
                return agentFactory.CreateAgentWithTools(tools, mm, scopedContext);
            }

            if (profileTools != null)
            {
                var tools = agentFactory.CreateToolsForMode(mode, threadBaseContext);
                tools.AddRange(profileTools);
                AppendChannelTools(tools, thread);
                return agentFactory.CreateAgentWithTools(tools, mm, threadBaseContext);
            }

            var modeTools = agentFactory.CreateToolsForMode(mode, threadBaseContext);
            AppendChannelTools(modeTools, thread);
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
                WorkspacePath = scopedContext.WorkspacePath,
                BotPath = scopedContext.BotPath,
                MemoryStore = scopedContext.MemoryStore,
                SkillsLoader = scopedContext.SkillsLoader,
                ApprovalService = scopedContext.ApprovalService,
                PathBlacklist = scopedContext.PathBlacklist,
                TraceCollector = scopedContext.TraceCollector,
                McpClientManager = mcpManager,
                LspServerManager = scopedContext.LspServerManager,
                AcpExtensionProxy = scopedContext.AcpExtensionProxy,
                CronTools = scopedContext.CronTools,
                DeferredToolRegistry = scopedContext.DeferredToolRegistry,
                ExternalCliSessionStore = scopedContext.ExternalCliSessionStore,
                AutomationTaskDirectory = scopedContext.AutomationTaskDirectory,
                RequireApprovalOutsideWorkspace = scopedContext.RequireApprovalOutsideWorkspace
            };

            var modeTools = agentFactory.CreateToolsForMode(mode, effectiveContext);
            if (profileTools != null)
                modeTools.AddRange(profileTools);
            modeTools.AddRange(mcpManager.Tools);
            AppendChannelTools(modeTools, thread);
            return agentFactory.CreateAgentWithTools(modeTools, mm, effectiveContext);
        }

        var toolsWithMcp = agentFactory.CreateToolsForMode(mode, threadBaseContext);
        if (profileTools != null)
            toolsWithMcp.AddRange(profileTools);
        toolsWithMcp.AddRange(mcpManager.Tools);
        AppendChannelTools(toolsWithMcp, thread);
        return agentFactory.CreateAgentWithTools(toolsWithMcp, mm, threadBaseContext);
    }

    private void AppendChannelTools(List<AITool> tools, SessionThread thread)
    {
        if (channelRuntimeToolProvider == null)
        {
            _threadExternalChannelToolNames.TryRemove(thread.Id, out _);
            return;
        }

        var reservedNames = new HashSet<string>(tools.Select(t => t.Name), StringComparer.Ordinal);
        var channelTools = channelRuntimeToolProvider.CreateToolsForThread(thread, reservedNames);
        if (channelTools.Count == 0)
        {
            _threadExternalChannelToolNames.TryRemove(thread.Id, out _);
            return;
        }

        _threadExternalChannelToolNames[thread.Id] = channelTools
            .Select(tool => tool.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);
        tools.AddRange(channelTools);
    }

    private IReadOnlySet<string> GetExternalChannelToolNames(string threadId)
        => _threadExternalChannelToolNames.TryGetValue(threadId, out var names)
            ? names
            : EmptyExternalToolNames;

    private static bool IsExternalChannelTool(IReadOnlySet<string> externalToolNames, string? toolName)
        => !string.IsNullOrWhiteSpace(toolName) && externalToolNames.Contains(toolName);

    private static OpenAI.Chat.ChatClient ResolveThreadChatClient(ToolProviderContext baseContext, ThreadConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config.Model))
            return baseContext.ChatClient;

        if (string.IsNullOrWhiteSpace(baseContext.Config.ApiKey))
            return baseContext.ChatClient;

        if (!Uri.TryCreate(baseContext.Config.EndPoint, UriKind.Absolute, out var endpoint))
            return baseContext.ChatClient;

        var client = new OpenAIClient(
            new ApiKeyCredential(baseContext.Config.ApiKey),
            new OpenAIClientOptions { Endpoint = endpoint });
        return client.GetChatClient(config.Model);
    }

    private static ToolProviderContext CloneContextWithChatClient(
        ToolProviderContext source,
        OpenAI.Chat.ChatClient chatClient,
        IExternalCliSessionStore? externalCliSessionStore = null)
    {
        var cloned = new ToolProviderContext
        {
            Config = source.Config,
            ChatClient = chatClient,
            WorkspacePath = source.WorkspacePath,
            BotPath = source.BotPath,
            MemoryStore = source.MemoryStore,
            SkillsLoader = source.SkillsLoader,
            ApprovalService = source.ApprovalService,
            PathBlacklist = source.PathBlacklist,
            CronTools = source.CronTools,
            McpClientManager = source.McpClientManager,
            LspServerManager = source.LspServerManager,
            TraceCollector = source.TraceCollector,
            AcpExtensionProxy = source.AcpExtensionProxy,
            ExternalCliSessionStore = externalCliSessionStore ?? source.ExternalCliSessionStore,
            AgentFileSystem = source.AgentFileSystem,
            AutomationTaskDirectory = source.AutomationTaskDirectory,
            RequireApprovalOutsideWorkspace = source.RequireApprovalOutsideWorkspace,
            DeferredToolRegistry = source.DeferredToolRegistry
        };
        return cloned;
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
