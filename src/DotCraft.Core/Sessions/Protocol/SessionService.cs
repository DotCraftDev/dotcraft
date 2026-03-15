using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Hooks;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Tracing;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions.Protocol;

/// <summary>
/// Session Core implementation. Manages Thread/Turn/Item lifecycle, orchestrates agent
/// execution, emits the structured event stream, and delegates persistence to ThreadStore.
/// </summary>
public sealed class SessionService : ISessionService
{
    private readonly AgentFactory _agentFactory;
    private readonly AIAgent _defaultAgent;
    private readonly ThreadStore _threadStore;
    private readonly SessionGate _sessionGate;
    private readonly HookRunner? _hookRunner;
    private readonly TraceCollector? _traceCollector;
    private readonly TimeSpan _approvalTimeout;
    private readonly ILogger<SessionService>? _logger;

    // In-memory state
    private readonly ConcurrentDictionary<string, SessionThread> _threads = new();
    private readonly ConcurrentDictionary<string, AIAgent> _threadAgents = new();
    private readonly ConcurrentDictionary<string, SessionApprovalService> _pendingApprovals = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _runningTurns = new();

    public SessionService(
        AgentFactory agentFactory,
        AIAgent defaultAgent,
        ThreadStore threadStore,
        SessionGate sessionGate,
        HookRunner? hookRunner = null,
        TraceCollector? traceCollector = null,
        TimeSpan? approvalTimeout = null,
        ILogger<SessionService>? logger = null)
    {
        _agentFactory = agentFactory;
        _defaultAgent = defaultAgent;
        _threadStore = threadStore;
        _sessionGate = sessionGate;
        _hookRunner = hookRunner;
        _traceCollector = traceCollector;
        _approvalTimeout = approvalTimeout ?? TimeSpan.FromMinutes(5);
        _logger = logger;
    }

    // =========================================================================
    // Thread lifecycle
    // =========================================================================

    /// <inheritdoc/>
    public async Task<SessionThread> CreateThreadAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        CancellationToken ct = default)
    {
        var thread = new SessionThread
        {
            Id = SessionIdGenerator.NewThreadId(),
            WorkspacePath = identity.WorkspacePath,
            UserId = identity.UserId,
            OriginChannel = identity.ChannelName,
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            HistoryMode = historyMode,
            Configuration = config
        };

        if (identity.ChannelContext != null)
            thread.Metadata["channelContext"] = identity.ChannelContext;

        _threads[thread.Id] = thread;

        // Create per-thread agent if custom configuration provided
        if (config != null)
            _threadAgents[thread.Id] = BuildAgentForConfig(config);

        await _threadStore.SaveThreadAsync(thread, ct);
        await _threadStore.UpdateIndexEntryAsync(thread, ct);

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
                cached.Status = ThreadStatus.Active;
                cached.LastActiveAt = DateTimeOffset.UtcNow;
                await _threadStore.SaveThreadAsync(cached, ct);
                await _threadStore.UpdateIndexEntryAsync(cached, ct);
            }
            return cached;
        }

        var thread = await _threadStore.LoadThreadAsync(threadId, ct)
            ?? throw new KeyNotFoundException($"Thread '{threadId}' not found.");

        if (thread.Status == ThreadStatus.Archived)
            throw new InvalidOperationException($"Thread '{threadId}' is archived and cannot be resumed.");

        thread.Status = ThreadStatus.Active;
        thread.LastActiveAt = DateTimeOffset.UtcNow;

        _threads[thread.Id] = thread;

        if (thread.Configuration != null)
            _threadAgents[thread.Id] = BuildAgentForConfig(thread.Configuration);

        await _threadStore.SaveThreadAsync(thread, ct);
        await _threadStore.UpdateIndexEntryAsync(thread, ct);

        return thread;
    }

    /// <inheritdoc/>
    public async Task PauseThreadAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        if (thread.Status == ThreadStatus.Paused) return;
        var prev = thread.Status;
        thread.Status = ThreadStatus.Paused;
        await PersistThreadStatusAsync(thread, ct);
    }

    /// <inheritdoc/>
    public async Task ArchiveThreadAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        if (thread.Status == ThreadStatus.Archived) return;
        thread.Status = ThreadStatus.Archived;
        // Release per-thread agent if any
        _threadAgents.TryRemove(threadId, out _);
        await PersistThreadStatusAsync(thread, ct);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        CancellationToken ct = default)
    {
        var index = await _threadStore.LoadIndexAsync(ct);
        var results = index
            .Where(s =>
                string.Equals(s.WorkspacePath, identity.WorkspacePath, StringComparison.OrdinalIgnoreCase)
                && (identity.UserId == null || s.UserId == identity.UserId))
            .OrderByDescending(s => s.LastActiveAt)
            .ToList();

        // Fall back to checking in-memory threads if index is stale
        if (results.Count == 0)
        {
            results = _threads.Values
                .Where(t =>
                    string.Equals(t.WorkspacePath, identity.WorkspacePath, StringComparison.OrdinalIgnoreCase)
                    && (identity.UserId == null || t.UserId == identity.UserId))
                .OrderByDescending(t => t.LastActiveAt)
                .Select(ThreadSummary.FromThread)
                .ToList();
        }

        return results;
    }

    /// <inheritdoc/>
    public async Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default) =>
        await GetOrLoadThreadAsync(threadId, ct);

    // =========================================================================
    // Turn orchestration
    // =========================================================================

    /// <inheritdoc/>
    public IAsyncEnumerable<SessionEvent> SubmitInputAsync(
        string threadId,
        string text,
        SenderContext? sender = null,
        ChatMessage[]? messages = null,
        CancellationToken ct = default)
    {
        // This method returns immediately; execution happens in a background Task.
        // We use a SessionEventChannel to bridge the background task to the caller.
        var channel = StartTurnAsync(threadId, text, sender, messages, ct);
        return channel.ReadAllAsync(ct);
    }

    private SessionEventChannel StartTurnAsync(
        string threadId,
        string text,
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

        // Step 2: Create Turn and UserMessage Item
        var turnSeq = thread.Turns.Count + 1;
        var turn = new SessionTurn
        {
            Id = SessionIdGenerator.NewTurnId(turnSeq),
            ThreadId = threadId,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow,
            OriginChannel = thread.OriginChannel
        };

        var itemSeq = 1;
        int NextItemSeq() => itemSeq++;

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
                SenderName = sender?.SenderName
            }
        };

        turn.Input = userItem;
        turn.Items.Add(userItem);
        thread.Turns.Add(turn);
        thread.LastActiveAt = DateTimeOffset.UtcNow;

        // Step 3: Create event channel
        var eventChannel = new SessionEventChannel(threadId, turn.Id);

        // Step 4: Emit initial events synchronously so the caller sees them before awaiting
        eventChannel.EmitTurnStarted(turn);
        eventChannel.EmitItemStarted(userItem);
        eventChannel.EmitItemCompleted(userItem);

        // Step 5: Run execution in background
        var cts = new CancellationTokenSource();
        _runningTurns[turn.Id] = cts;

        // Link caller cancellation with our internal CTS
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerCt, cts.Token);
        var executionCt = linkedCts.Token;

        _ = Task.Run(async () =>
        {
            IDisposable? gateLock = null;
            IDisposable? approvalOverride = null;
            try
            {
                // Step 5a: Acquire SessionGate
                try
                {
                    gateLock = await _sessionGate.AcquireAsync(threadId, executionCt);
                }
                catch (SessionGateOverflowException ex)
                {
                    _logger?.LogWarning("Session gate overflow for thread {ThreadId}: {Message}", threadId, ex.Message);
                    FailTurn(turn, eventChannel, $"Session queue overflow: {ex.Message}");
                    return;
                }

                // Step 5b: Load/create AgentSession
                var agent = _threadAgents.GetValueOrDefault(threadId, _defaultAgent);
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
                    session = await _threadStore.LoadOrCreateSessionAsync(agent, threadId, executionCt);
                }

                // Step 5c: Append runtime context
                var prompt = RuntimeContextBuilder.AppendTo(text);

                // Step 5d: Run PrePrompt hooks
                if (_hookRunner != null)
                {
                    var hookInput = new HookInput { SessionId = threadId, Prompt = prompt };
                    var hookResult = await _hookRunner.RunAsync(HookEvent.PrePrompt, hookInput, executionCt);
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
                var tokenTracker = _agentFactory.GetOrCreateTokenTracker(threadId);
                TokenTracker.Current = tokenTracker;

                // Step 5f: Set up approval service override
                var approvalService = new SessionApprovalService(
                    eventChannel, turn, NextItemSeq, _approvalTimeout);
                _pendingApprovals[turn.Id] = approvalService;
                approvalOverride = SessionScopedApprovalService.SetOverride(approvalService);

                // Set ApprovalContext for tools that read ApprovalContextScope
                var approvalContextDisposable = sender != null
                    ? ApprovalContextScope.Set(new ApprovalContext
                    {
                        UserId = sender.SenderId,
                        UserRole = sender.SenderRole,
                        Source = ApprovalSource.Console // default; adapters can set this
                    })
                    : null;

                // Step 5g: Run agent
                SessionItem? agentMessageItem = null;
                SessionItem? reasoningItem = null;
                var agentText = string.Empty;
                var reasoningText = string.Empty;
                long inputTokens = 0, outputTokens = 0;

                try
                {
                    await foreach (var update in agent.RunStreamingAsync(prompt, session)
                        .WithCancellation(executionCt))
                    {
                        foreach (var content in update.Contents)
                        {
                            switch (content)
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
                                            ToolName = fc.Name ?? string.Empty,
                                            Arguments = fc.Arguments != null
                                                ? JsonNode.Parse(
                                                    System.Text.Json.JsonSerializer.Serialize(
                                                        fc.Arguments)) as JsonObject
                                                : null,
                                            CallId = fc.CallId ?? string.Empty
                                        }
                                    };
                                    turn.Items.Add(toolCallItem);
                                    eventChannel.EmitItemStarted(toolCallItem);
                                    eventChannel.EmitItemCompleted(toolCallItem);
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
                                            CallId = fr.CallId ?? string.Empty,
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
                                    var iterInput = usage.Details.InputTokenCount ?? 0;
                                    var iterOutput = usage.Details.OutputTokenCount ?? 0;
                                    if (iterInput > 0 || iterOutput > 0)
                                    {
                                        inputTokens += iterInput;
                                        outputTokens += iterOutput;
                                        tokenTracker.Update(iterInput, iterOutput);
                                    }
                                    break;
                            }
                        }
                    }
                }
                finally
                {
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

                // Step 5i: Accumulate token usage
                if (inputTokens > 0 || outputTokens > 0)
                {
                    turn.TokenUsage = new TokenUsageInfo
                    {
                        InputTokens = inputTokens,
                        OutputTokens = outputTokens,
                        TotalTokens = inputTokens + outputTokens
                    };
                }

                // Step 5i: Save AgentSession (server mode only)
                if (thread.HistoryMode == HistoryMode.Server)
                {
                    try
                    {
                        await _threadStore.SaveSessionAsync(agent, session, threadId, compact: true,
                            ct: CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Failed to save agent session for thread {ThreadId}", threadId);
                    }
                }

                // Step 5j: Run Stop hooks
                if (_hookRunner != null)
                {
                    var stopInput = new HookInput { SessionId = threadId, Response = agentText };
                    await _hookRunner.RunAsync(HookEvent.Stop, stopInput, CancellationToken.None);
                }

                // Step 5k: Compaction
                if (_agentFactory is { Compactor: not null, MaxContextTokens: > 0 } &&
                    (tokenTracker.LastInputTokens) >= _agentFactory.MaxContextTokens)
                {
                    if (await _agentFactory.Compactor.TryCompactAsync(session, CancellationToken.None))
                    {
                        tokenTracker.Reset();
                        _traceCollector?.RecordContextCompaction(threadId);
                    }
                }

                // Step 5l: Memory consolidation (fire-and-forget)
                _ = _agentFactory.TryConsolidateMemory(session, threadId);

                // Step 5m: Release gate
                gateLock?.Dispose();
                gateLock = null;

                // Steps 5n-5r: Complete Turn
                turn.Status = TurnStatus.Completed;
                turn.CompletedAt = DateTimeOffset.UtcNow;
                thread.LastActiveAt = DateTimeOffset.UtcNow;
                eventChannel.EmitTurnCompleted(turn);

                try
                {
                    await _threadStore.SaveThreadAsync(thread, CancellationToken.None);
                    await _threadStore.UpdateIndexEntryAsync(thread, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to persist thread state after turn completion for thread {ThreadId}", threadId);
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
                _logger?.LogError(ex, "Turn execution failed for thread {ThreadId}", threadId);
                FailTurn(turn, eventChannel, ex.Message);
                await TrySaveThreadAsync(thread);
            }
            finally
            {
                approvalOverride?.Dispose();
                gateLock?.Dispose();
                _pendingApprovals.TryRemove(turn.Id, out _);
                _runningTurns.TryRemove(turn.Id, out var runCts);
                runCts?.Dispose();
                eventChannel.Complete();
            }
        }, CancellationToken.None); // Run regardless of caller ct; we handle it internally

        return eventChannel;
    }

    /// <inheritdoc/>
    public Task ResolveApprovalAsync(
        string turnId,
        string requestId,
        bool approved,
        CancellationToken ct = default)
    {
        if (_pendingApprovals.TryGetValue(turnId, out var svc))
        {
            svc.TryResolve(requestId, approved);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task CancelTurnAsync(string turnId, CancellationToken ct = default)
    {
        if (_runningTurns.TryGetValue(turnId, out var cts))
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
        _threadAgents[threadId] = _agentFactory.CreateAgentForMode(agentMode);

        await _threadStore.SaveThreadAsync(thread, ct);
        await _threadStore.UpdateIndexEntryAsync(thread, ct);
    }

    /// <inheritdoc/>
    public async Task UpdateThreadConfigurationAsync(
        string threadId,
        ThreadConfiguration config,
        CancellationToken ct = default)
    {
        var thread = await GetOrLoadThreadAsync(threadId, ct);
        thread.Configuration = config;
        _threadAgents[threadId] = BuildAgentForConfig(config);
        await _threadStore.SaveThreadAsync(thread, ct);
        await _threadStore.UpdateIndexEntryAsync(thread, ct);
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private async Task<SessionThread> GetOrLoadThreadAsync(string threadId, CancellationToken ct)
    {
        if (_threads.TryGetValue(threadId, out var cached))
            return cached;

        var thread = await _threadStore.LoadThreadAsync(threadId, ct)
            ?? throw new KeyNotFoundException($"Thread '{threadId}' not found.");

        _threads[thread.Id] = thread;
        return thread;
    }

    private async Task PersistThreadStatusAsync(SessionThread thread, CancellationToken ct)
    {
        await _threadStore.SaveThreadAsync(thread, ct);
        await _threadStore.UpdateIndexEntryAsync(thread, ct);
    }

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
            await _threadStore.SaveThreadAsync(thread, CancellationToken.None);
            await _threadStore.UpdateIndexEntryAsync(thread, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to persist thread state for thread {ThreadId}", thread.Id);
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

    private AIAgent BuildAgentForConfig(ThreadConfiguration config)
    {
        var mode = config.Mode?.Equals("plan", StringComparison.OrdinalIgnoreCase) == true
            ? AgentMode.Plan
            : AgentMode.Agent;
        return _agentFactory.CreateAgentForMode(mode);
    }

}
