using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Hooks;
using DotCraft.Memory;
using DotCraft.Mcp;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
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
/// execution, emits the structured event stream, and delegates persistence to ThreadStore.
/// </summary>
public sealed class SessionService(
    AgentFactory agentFactory,
    AIAgent defaultAgent,
    ThreadStore threadStore,
    SessionGate sessionGate,
    HookRunner? hookRunner = null,
    TraceCollector? traceCollector = null,
    TimeSpan? approvalTimeout = null,
    ILogger<SessionService>? logger = null,
    ApprovalStore? approvalStore = null,
    IToolProfileRegistry? toolProfileRegistry = null)
    : ISessionService
{
    private readonly IToolProfileRegistry? _toolProfileRegistry = toolProfileRegistry;

    private readonly TimeSpan _approvalTimeout = approvalTimeout ?? TimeSpan.FromMinutes(5);

    // In-memory state
    private readonly ConcurrentDictionary<string, SessionThread> _threads = new();
    private readonly ConcurrentDictionary<string, AIAgent> _threadAgents = new();
    private readonly ConcurrentDictionary<TurnKey, SessionApprovalService> _pendingApprovals = new();
    private readonly ConcurrentDictionary<TurnKey, CancellationTokenSource> _runningTurns = new();
    private readonly ConcurrentDictionary<string, McpClientManager> _threadMcpManagers = new();
    private readonly ConcurrentDictionary<string, AgentModeManager> _threadModeManagers = new();
    private readonly ConcurrentDictionary<string, ThreadEventBroker> _threadEventBrokers = new();

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

        _threads[thread.Id] = thread;
        var broker = GetOrCreateBroker(thread.Id);

        // Create per-thread agent if custom configuration provided
        if (config != null)
            _threadAgents[thread.Id] = await BuildAgentForConfigAsync(thread.Id, config, ct);

        await threadStore.SaveThreadAsync(thread, ct);
        broker.PublishThreadEvent(SessionEventType.ThreadCreated, thread);

        return thread;
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
                await threadStore.SaveThreadAsync(cached, ct);
                GetOrCreateBroker(threadId).PublishThreadStatusChanged(previousStatus, cached.Status);
            }

            await EnsurePerThreadAgentIfMissingAsync(threadId, cached, ct);

            var resumedByChannel = ChannelSessionScope.Current?.Channel ?? cached.OriginChannel;
            GetOrCreateBroker(threadId).PublishThreadEvent(SessionEventType.ThreadResumed,
                new ThreadResumedPayload { Thread = cached, ResumedBy = resumedByChannel });
            return cached;
        }

        var thread = await threadStore.LoadThreadAsync(threadId, ct)
            ?? throw new KeyNotFoundException($"Thread '{threadId}' not found.");

        if (thread.Status == ThreadStatus.Archived)
            throw new InvalidOperationException($"Thread '{threadId}' is archived and cannot be resumed.");

        thread.Status = ThreadStatus.Active;
        thread.LastActiveAt = DateTimeOffset.UtcNow;

        _threads[thread.Id] = thread;
        var broker = GetOrCreateBroker(thread.Id);

        await EnsurePerThreadAgentIfMissingAsync(thread.Id, thread, ct);

        await threadStore.SaveThreadAsync(thread, ct);
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
        await PersistThreadStatusAsync(thread, ct);
        GetOrCreateBroker(threadId).PublishThreadStatusChanged(previousStatus, thread.Status);
    }

    /// <inheritdoc/>
    public async Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default)
    {
        // Cancel any running or approval-pending turns for this thread
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

        // Remove all in-memory state
        _threads.TryRemove(threadId, out _);
        _threadAgents.TryRemove(threadId, out _);
        _threadModeManagers.TryRemove(threadId, out _);
        _threadEventBrokers.TryRemove(threadId, out _);
        if (_threadMcpManagers.TryRemove(threadId, out var mcpManager))
            await mcpManager.DisposeAsync();

        // Delete persisted files
        threadStore.DeleteThread(threadId);
        threadStore.DeleteSessionFile(threadId);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        bool includeArchived = false,
        CancellationToken ct = default)
    {
        var all = await threadStore.LoadIndexAsync(ct);
        return all
            .Where(s =>
                (includeArchived || s.Status != ThreadStatus.Archived)
                && string.Equals(s.WorkspacePath, identity.WorkspacePath, StringComparison.OrdinalIgnoreCase)
                && (identity.UserId == null || s.UserId == identity.UserId)
                && (identity.ChannelContext == null
                    ? s.ChannelContext == null
                    : s.ChannelContext == identity.ChannelContext))
            .OrderByDescending(s => s.LastActiveAt)
            .ToList();
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
        CancellationToken ct = default)
    {
        // This method returns immediately; execution happens in a background Task.
        // We use a SessionEventChannel to bridge the background task to the caller.
        var channel = StartTurnAsync(threadId, content, sender, messages, ct);
        return channel.ReadAllAsync(ct);
    }

    private SessionEventChannel StartTurnAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender,
        ChatMessage[]? messages,
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
        var text = string.Concat(content.OfType<TextContent>().Select(t => t.Text));

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
                SenderId = sender?.SenderId,
                SenderName = sender?.SenderName,
                SenderRole = sender?.SenderRole,
                ChannelName = turnOriginChannel,
                ChannelContext = turnChannelContext,
                GroupId = sender?.GroupId ?? channelInfo?.GroupId
            }
        };

        turn.Input = userItem;
        turn.Items.Add(userItem);
        thread.Turns.Add(turn);
        thread.LastActiveAt = DateTimeOffset.UtcNow;

        // Set a display name from the first user message so the session list shows a preview.
        if (string.IsNullOrEmpty(thread.DisplayName))
            thread.DisplayName = text.Length > 50 ? text[..50] + "..." : text;

        // Step 3: Create event channel
        var broker = GetOrCreateBroker(threadId);
        var eventChannel = broker.CreateTurnChannel(turn.Id);

        // Step 4: Emit initial events synchronously so the caller sees them before awaiting
        eventChannel.EmitTurnStarted(turn);
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
                AgentSession session;
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
                    session = await threadStore.LoadOrCreateSessionAsync(agent, threadId, executionCt);
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

                // Step 5e: Set tracing context
                TracingChatClient.CurrentSessionKey = threadId;
                TracingChatClient.ResetCallState(threadId);
                var tokenTracker = agentFactory.GetOrCreateTokenTracker(threadId);
                TokenTracker.Current = tokenTracker;

                // Step 5f: Set up approval service override
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
                            approvalStore);
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
                long inputTokens = 0, outputTokens = 0;
                long lastUsageInput = 0, lastUsageOutput = 0;

                // SubAgent progress aggregator: lazily created when SpawnSubagent tool calls appear
                SubAgentProgressAggregator? progressAggregator = null;

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

                                case FunctionCallContent fc:
                                {
                                    var toolCallItem = new SessionItem
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
                                    var resultText = fr.Result is string s ? s
                                        : fr.Result?.ToString() ?? string.Empty;
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

                                    // Finalize the current AgentMessage so any subsequent
                                    // text (post-tool response) starts a fresh item,
                                    // preserving the natural interleaving in stored turns.
                                    if (agentMessageItem != null)
                                    {
                                        agentMessageItem.Payload = new AgentMessagePayload { Text = agentText };
                                        agentMessageItem.Status = ItemStatus.Completed;
                                        agentMessageItem.CompletedAt = DateTimeOffset.UtcNow;
                                        eventChannel.EmitItemCompleted(agentMessageItem);
                                        agentMessageItem = null;
                                        agentText = string.Empty;
                                    }
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
                                            eventChannel.EmitUsageDelta(deltaIn, deltaOut);
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

                // Step 5i: Save AgentSession (server mode only)
                if (thread.HistoryMode == HistoryMode.Server)
                {
                    try
                    {
                        await threadStore.SaveSessionAsync(agent, session, threadId, compact: true,
                            ct: CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError(ex, "Failed to save agent session for thread {ThreadId}", threadId);
                    }
                }

                // Step 5j: Run Stop hooks
                if (hookRunner != null)
                {
                    var stopInput = new HookInput { SessionId = threadId, Response = agentText };
                    await hookRunner.RunAsync(HookEvent.Stop, stopInput, CancellationToken.None);
                }

                // Step 5k: Compaction
                if (agentFactory is { Compactor: not null, MaxContextTokens: > 0 } &&
                    (tokenTracker.LastInputTokens) >= agentFactory.MaxContextTokens)
                {
                    eventChannel.EmitSystemEvent("compacting");
                    if (await agentFactory.Compactor.TryCompactAsync(session, CancellationToken.None))
                    {
                        tokenTracker.Reset();
                        traceCollector?.RecordContextCompaction(threadId);
                        eventChannel.EmitSystemEvent("compacted");
                    }
                    else
                    {
                        eventChannel.EmitSystemEvent("compactSkipped");
                    }
                }

                // Step 5l: Memory consolidation (awaited for client notification)
                {
                    var consolidationTask = agentFactory.TryConsolidateMemory(session, threadId);
                    if (consolidationTask != null)
                    {
                        eventChannel.EmitSystemEvent("consolidating");
                        await consolidationTask;
                        eventChannel.EmitSystemEvent("consolidated");
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

                try
                {
                    await threadStore.SaveThreadAsync(thread, CancellationToken.None);
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
                await TrySaveThreadAsync(thread);
            }
            catch (OperationCanceledException)
            {
                // Caller cancellation
                turn.Status = TurnStatus.Cancelled;
                turn.CompletedAt = DateTimeOffset.UtcNow;
                eventChannel.EmitTurnCancelled(turn, "Caller cancelled");
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Turn execution failed for thread {ThreadId}", threadId);
                FailTurn(turn, eventChannel, ex.Message);
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

        var agentMode = mode.Equals("plan", StringComparison.OrdinalIgnoreCase)
            ? AgentMode.Plan
            : AgentMode.Agent;
        var mm = GetOrCreateModeManager(threadId, agentMode);
        _threadAgents[threadId] = agentFactory.CreateAgentForMode(agentMode, mm);

        await threadStore.SaveThreadAsync(thread, ct);
    }

    /// <inheritdoc/>
    public async Task UpdateThreadConfigurationAsync(
        string threadId,
        ThreadConfiguration config,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        thread.Configuration = config;
        _threadAgents[threadId] = await BuildAgentForConfigAsync(threadId, config, ct);
        await threadStore.SaveThreadAsync(thread, ct);
    }

    /// <inheritdoc/>
    public async Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        thread.DisplayName = displayName;
        await threadStore.SaveThreadAsync(thread, ct);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private async Task<SessionThread> GetOrLoadThreadAsync(string threadId, CancellationToken ct)
    {
        if (_threads.TryGetValue(threadId, out var cached))
            return cached;

        var thread = await threadStore.LoadThreadAsync(threadId, ct)
            ?? throw new KeyNotFoundException($"Thread '{threadId}' not found.");

        _threads[thread.Id] = thread;
        _ = GetOrCreateBroker(thread.Id);
        return thread;
    }

    private async Task EnsurePerThreadAgentIfMissingAsync(
        string threadId, SessionThread thread, CancellationToken ct)
    {
        if (thread.Configuration != null && !_threadAgents.ContainsKey(threadId))
            _threadAgents[threadId] = await BuildAgentForConfigAsync(threadId, thread.Configuration, ct);
    }

    private async Task PersistThreadStatusAsync(SessionThread thread, CancellationToken ct)
    {
        await threadStore.SaveThreadAsync(thread, ct);
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

    private static void FailTurn(SessionTurn turn, SessionEventChannel channel, string errorMsg)
    {
        turn.Status = TurnStatus.Failed;
        turn.Error = errorMsg;
        turn.CompletedAt = DateTimeOffset.UtcNow;
        channel.EmitTurnFailed(turn, errorMsg);
    }

    private async Task TrySaveThreadAsync(SessionThread thread)
    {
        try
        {
            await threadStore.SaveThreadAsync(thread, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to persist thread state for thread {ThreadId}", thread.Id);
        }
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

    private async Task<AIAgent> BuildAgentForConfigAsync(
        string threadId, ThreadConfiguration config, CancellationToken ct)
    {
        var mode = config.Mode.Equals("plan", StringComparison.OrdinalIgnoreCase)
            ? AgentMode.Plan
            : AgentMode.Agent;
        var mm = GetOrCreateModeManager(threadId, mode);

        ToolProviderContext? scopedContext = null;
        if (!string.IsNullOrEmpty(config.WorkspaceOverride))
        {
            var craftPath = Path.Combine(config.WorkspaceOverride, ".craft");
            Directory.CreateDirectory(craftPath);

            var scopedMemory = new MemoryStore(craftPath);
            var scopedSkills = new SkillsLoader(craftPath);
            var baseCtx = agentFactory.ToolProviderContext;

            scopedContext = new ToolProviderContext
            {
                Config = baseCtx.Config,
                ChatClient = baseCtx.ChatClient,
                WorkspacePath = config.WorkspaceOverride,
                BotPath = craftPath,
                MemoryStore = scopedMemory,
                SkillsLoader = scopedSkills,
                ApprovalService = baseCtx.ApprovalService,
                PathBlacklist = new PathBlacklist([]),
                TraceCollector = baseCtx.TraceCollector,
                ChannelClient = baseCtx.ChannelClient,
                AcpExtensionProxy = baseCtx.AcpExtensionProxy,
                CronTools = baseCtx.CronTools,
                DeferredToolRegistry = baseCtx.DeferredToolRegistry,
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

            var toolCtx = scopedContext ?? agentFactory.ToolProviderContext;
            profileTools = agentFactory.CreateToolsFromProviders(profileProviders, toolCtx);
        }

        if (config.UseToolProfileOnly)
        {
            if (profileTools is not { Count: > 0 })
                throw new InvalidOperationException("UseToolProfileOnly requires a registered ToolProfile with at least one tool.");
            var toolCtx2 = scopedContext ?? agentFactory.ToolProviderContext;
            return agentFactory.CreateAgentWithTools(profileTools, mm, toolCtx2, config.AgentInstructions);
        }

        if (config.McpServers is not { Length: > 0 })
        {
            if (scopedContext != null)
            {
                var tools = agentFactory.CreateToolsForMode(mode, scopedContext);
                if (profileTools != null)
                    tools.AddRange(profileTools);
                return agentFactory.CreateAgentWithTools(tools, mm, scopedContext);
            }

            if (profileTools != null)
            {
                var tools = agentFactory.CreateToolsForMode(mode);
                tools.AddRange(profileTools);
                return agentFactory.CreateAgentWithTools(tools, mm, agentFactory.ToolProviderContext);
            }

            return agentFactory.CreateAgentForMode(mode, mm);
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
                ChannelClient = scopedContext.ChannelClient,
                AcpExtensionProxy = scopedContext.AcpExtensionProxy,
                CronTools = scopedContext.CronTools,
                DeferredToolRegistry = scopedContext.DeferredToolRegistry,
                AutomationTaskDirectory = scopedContext.AutomationTaskDirectory,
                RequireApprovalOutsideWorkspace = scopedContext.RequireApprovalOutsideWorkspace
            };

            var modeTools = agentFactory.CreateToolsForMode(mode, effectiveContext);
            if (profileTools != null)
                modeTools.AddRange(profileTools);
            modeTools.AddRange(mcpManager.Tools);
            return agentFactory.CreateAgentWithTools(modeTools, mm, effectiveContext);
        }

        var toolsWithMcp = agentFactory.CreateToolsForMode(mode);
        if (profileTools != null)
            toolsWithMcp.AddRange(profileTools);
        toolsWithMcp.AddRange(mcpManager.Tools);
        return agentFactory.CreateAgentWithTools(toolsWithMcp, mm);
    }
}
