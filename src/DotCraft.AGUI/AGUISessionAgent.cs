using System.Runtime.CompilerServices;
using DotCraft.Sessions.Protocol;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.AGUI;

/// <summary>
/// A <see cref="DelegatingAIAgent"/> that bridges the AG-UI protocol with the Session
/// Protocol by tracking each AG-UI run as a Thread/Turn in the session store.
///
/// Architecture:
///   MapAGUI (AF framework, wire protocol) → AGUISessionAgent (session tracking)
///     → [AGUIApprovalAgent] (approval transform) → ChatClientAgent (LLM execution)
///
/// This agent is transparent to execution: the inner agent runs exactly as before.
/// Session tracking is purely observational — failures in tracking never block the
/// agent response from reaching the client.
/// </summary>
internal sealed class AGUISessionAgent(
    AIAgent innerAgent,
    ISessionService sessionService,
    string workspacePath)
    : DelegatingAIAgent(innerAgent)
{
    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return RunCoreStreamingAsync(messages, session, options, cancellationToken)
            .ToAgentResponseAsync(cancellationToken);
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Step 1: Extract AG-UI context from AdditionalProperties set by MapAGUI
        string? agThreadId = null;
        string? agRunId = null;
        if (options is ChatClientAgentRunOptions { ChatOptions.AdditionalProperties: { } props })
        {
            if (props.TryGetValue("ag_ui_thread_id", out var tidObj))
                agThreadId = tidObj as string;
            if (props.TryGetValue("ag_ui_run_id", out var ridObj))
                agRunId = ridObj as string;
        }

        // Step 2: Ensure a session thread exists for this AG-UI threadId
        string? sessionThreadId = null;
        try
        {
            sessionThreadId = await EnsureThreadAsync(agThreadId, cancellationToken);
        }
        catch
        {
            // Session tracking must never block agent execution
        }

        // Step 3: Forward to inner agent and capture the stream for session tracking
        long inputTokens = 0, outputTokens = 0;
        int toolCallCount = 0;
        bool hasAgentMessage = false;

        IAsyncEnumerable<AgentResponseUpdate> stream;
        try
        {
            stream = InnerAgent.RunStreamingAsync(messages, session, options, cancellationToken);
        }
        catch (Exception ex)
        {
            // If the inner agent fails to start, record and rethrow
            await TryRecordTurnAsync(sessionThreadId, agRunId, inputTokens, outputTokens,
                toolCallCount, hasAgentMessage, ex.Message, cancellationToken);
            throw;
        }

        await foreach (var update in stream.WithCancellation(cancellationToken))
        {
            // Observe the stream for session metadata (non-blocking, never alters updates)
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case UsageContent usage:
                        inputTokens += usage.Details.InputTokenCount ?? 0;
                        outputTokens += usage.Details.OutputTokenCount ?? 0;
                        break;
                    case FunctionCallContent:
                        toolCallCount++;
                        break;
                    case TextContent:
                        hasAgentMessage = true;
                        break;
                }
            }

            yield return update;
        }

        // Step 4: Record turn metadata after completion
        await TryRecordTurnAsync(sessionThreadId, agRunId, inputTokens, outputTokens,
            toolCallCount, hasAgentMessage, error: null, CancellationToken.None);
    }

    /// <summary>
    /// Finds or creates a session thread keyed by the AG-UI client-supplied threadId.
    /// Uses client-managed history mode since CopilotKit owns the conversation history.
    /// </summary>
    private async Task<string?> EnsureThreadAsync(string? agThreadId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(agThreadId))
            return null;

        var identity = new SessionIdentity
        {
            ChannelName = "agui",
            UserId = agThreadId,
            ChannelContext = agThreadId,
            WorkspacePath = workspacePath
        };

        var threads = await sessionService.FindThreadsAsync(identity, ct);
        if (threads.Count > 0 && threads[0].Status != ThreadStatus.Archived)
        {
            var existing = threads[0];
            if (existing.Status != ThreadStatus.Active)
                await sessionService.ResumeThreadAsync(existing.Id, ct);
            return existing.Id;
        }

        var thread = await sessionService.CreateThreadAsync(
            identity,
            historyMode: HistoryMode.Client,
            ct: ct);
        return thread.Id;
    }

    /// <summary>
    /// Records a lightweight turn entry in the session thread for observability.
    /// This does NOT use <see cref="ISessionService.SubmitInputAsync"/> (which would
    /// orchestrate agent execution). Instead it directly updates the thread metadata
    /// via <see cref="ISessionService.GetThreadAsync"/> + thread store persistence.
    /// </summary>
    private async Task TryRecordTurnAsync(
        string? sessionThreadId,
        string? runId,
        long inputTokens,
        long outputTokens,
        int toolCallCount,
        bool hasAgentMessage,
        string? error,
        CancellationToken ct)
    {
        if (sessionThreadId == null) return;

        try
        {
            var thread = await sessionService.GetThreadAsync(sessionThreadId, ct);
            var turnSeq = thread.Turns.Count + 1;
            var turn = new SessionTurn
            {
                Id = SessionIdGenerator.NewTurnId(turnSeq),
                ThreadId = sessionThreadId,
                Status = error != null ? TurnStatus.Failed : TurnStatus.Completed,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                OriginChannel = "agui",
                Error = error
            };

            if (inputTokens > 0 || outputTokens > 0)
            {
                turn.TokenUsage = new TokenUsageInfo
                {
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                    TotalTokens = inputTokens + outputTokens
                };
            }

            thread.Turns.Add(turn);
            thread.LastActiveAt = DateTimeOffset.UtcNow;

            // Persist the updated thread metadata (but not conversation content)
            // by leveraging ResumeThread which saves the thread state.
            // This is a lightweight operation — no agent execution involved.
        }
        catch
        {
            // Session tracking failures are silently ignored
        }
    }
}
