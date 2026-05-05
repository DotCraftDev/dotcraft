using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Tracing;

/// <summary>
/// DelegatingChatClient that records trace events.
/// Designed to be placed INSIDE FunctionInvokingChatClient so it intercepts
/// each individual LLM call (including follow-up calls after tool execution).
/// Tool calls are detected from LLM responses; tool results are detected
/// from input messages on follow-up calls by FunctionInvokingChatClient.
///
/// State is stored per session key in a ConcurrentDictionary instead of AsyncLocal,
/// because FunctionInvokingChatClient calls this client's streaming method multiple
/// times across async enumerable boundaries where AsyncLocal copy-on-write semantics
/// prevent state from being shared between invocations.
/// </summary>
public sealed class TracingChatClient(IChatClient innerClient, TraceCollector collector) : DelegatingChatClient(innerClient)
{
    private static readonly AsyncLocal<string?> SessionKeyLocal = new();

    /// <summary>
    /// Per-session shared state that survives across multiple calls from FunctionInvokingChatClient.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SessionCallState> SessionStates = new();

    /// <summary>
    /// Tracks active sessions for reliable session key retrieval across async boundaries.
    /// Key is session ID, value is timestamp of last activity.
    /// </summary>
    private static readonly ConcurrentDictionary<string, DateTimeOffset> ActiveSessions = new();

    public static string? CurrentSessionKey
    {
        get => SessionKeyLocal.Value;
        set
        {
            SessionKeyLocal.Value = value;
            if (value != null)
            {
                ActiveSessions[value] = DateTimeOffset.UtcNow;
            }
        }
    }

    /// <summary>
    /// Gets the most recently active session key.
    /// This is more reliable than CurrentSessionKey when called from tool execution context
    /// where AsyncLocal value may not flow correctly across async enumerable boundaries.
    /// </summary>
    public static string? GetActiveSessionKey()
    {
        // First try AsyncLocal
        var key = SessionKeyLocal.Value;
        if (!string.IsNullOrEmpty(key))
            return key;

        // Fallback: find the most recently active session
        var mostRecent = ActiveSessions.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
        return mostRecent.Key;
    }

    /// <summary>
    /// Removes a session from the active sessions tracking.
    /// </summary>
    public static void ClearActiveSession(string sessionKey)
    {
        ActiveSessions.TryRemove(sessionKey, out _);
    }

    public static void ResetCallState(string? sessionKey = null)
    {
        var key = sessionKey ?? CurrentSessionKey;
        if (key != null)
        {
            SessionStates.TryRemove(key, out _);
            ActiveSessions.TryRemove(key, out _);
        }
    }

    private static SessionCallState GetOrCreateState(string sessionKey)
    {
        return SessionStates.GetOrAdd(sessionKey, _ => new SessionCallState());
    }

    /// <summary>
    /// Resolves the session key for the current call. Never returns null so that
    /// GetOrCreateState is never given a null key (e.g. when AG-UI requests do not set CurrentSessionKey).
    /// </summary>
    private static string ResolveSessionKeyForCurrentCall()
    {
        var key = CurrentSessionKey;
        if (!string.IsNullOrEmpty(key))
            return key;
        key = GetActiveSessionKey();
        if (!string.IsNullOrEmpty(key))
            return key;
        return "ag-ui:" + Guid.NewGuid().ToString("N")[..12];
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var sessionKey = ResolveSessionKeyForCurrentCall();
        var messages = chatMessages as IList<ChatMessage> ?? chatMessages.ToList();
        var state = GetOrCreateState(sessionKey);

        // Record request only on first call
        RecordRequestIfFirst(sessionKey, messages, state);

        ChatResponse response;
        try
        {
            response = await base.GetResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception ex)
        {
            collector.RecordError(sessionKey, ex.Message);
            throw;
        }

        RecordToolCallsFromResponse(sessionKey, response.Messages, state);

        // Record response if we have any text (regardless of tool calls)
        // Tool calls may happen in earlier iterations, but the final iteration will have the actual response
        var responseText = response.Text;
        if (!string.IsNullOrEmpty(responseText))
        {
            collector.RecordResponse(
                sessionKey,
                responseText,
                response.ResponseId,
                null,
                response.ModelId,
                response.FinishReason.ToString(),
                response.AdditionalProperties);
        }

        if (response.Usage != null)
        {
            var input = response.Usage.InputTokenCount ?? 0;
            var output = response.Usage.OutputTokenCount ?? 0;
            if (input > 0 || output > 0)
                collector.RecordTokenUsage(sessionKey, input, output);
        }

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionKey = ResolveSessionKeyForCurrentCall();
        var messages = chatMessages as IList<ChatMessage> ?? chatMessages.ToList();
        var state = GetOrCreateState(sessionKey);

        // Record request only on first call
        RecordRequestIfFirst(sessionKey, messages, state);

        var thinkingBuffer = new StringBuilder();
        var responseBuffer = new StringBuilder();
        DateTimeOffset? thinkingStartedAt = null;
        DateTimeOffset? responseStartedAt = null;
        ChatResponseUpdate? responseLastUpdate = null;
        long inputTokens = 0, outputTokens = 0;

        void FlushThinking()
        {
            if (thinkingBuffer.Length == 0)
                return;

            collector.RecordThinking(
                sessionKey,
                thinkingBuffer.ToString(),
                thinkingStartedAt);
            thinkingBuffer.Clear();
            thinkingStartedAt = null;
        }

        void FlushResponse(bool includeFinishReason)
        {
            if (responseBuffer.Length == 0)
                return;

            var segmentUpdate = responseLastUpdate ?? state.LastUpdate;
            var finalUpdate = includeFinishReason ? state.LastUpdate : null;
            collector.RecordResponse(
                sessionKey,
                responseBuffer.ToString(),
                segmentUpdate?.ResponseId ?? finalUpdate?.ResponseId,
                segmentUpdate?.MessageId ?? finalUpdate?.MessageId,
                segmentUpdate?.ModelId ?? finalUpdate?.ModelId,
                includeFinishReason ? (finalUpdate?.FinishReason ?? segmentUpdate?.FinishReason)?.ToString() : null,
                segmentUpdate?.AdditionalProperties ?? finalUpdate?.AdditionalProperties,
                responseStartedAt);
            responseBuffer.Clear();
            responseStartedAt = null;
            responseLastUpdate = null;
        }

        void FlushPendingSegments(bool includeResponseFinishReason)
        {
            FlushThinking();
            FlushResponse(includeResponseFinishReason);
        }

        void AppendThinking(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            FlushResponse(includeFinishReason: false);
            thinkingStartedAt ??= DateTimeOffset.UtcNow;
            thinkingBuffer.Append(text);
        }

        void AppendResponse(string text, ChatResponseUpdate update)
        {
            if (string.IsNullOrEmpty(text))
                return;

            FlushThinking();
            responseStartedAt ??= DateTimeOffset.UtcNow;
            responseBuffer.Append(text);
            responseLastUpdate = update;
        }

        IAsyncEnumerable<ChatResponseUpdate> stream;
        try
        {
            stream = base.GetStreamingResponseAsync(messages, options, cancellationToken);
        }
        catch (Exception ex)
        {
            collector.RecordError(sessionKey, ex.Message);
            throw;
        }

        await using var enumerator = stream.WithCancellation(cancellationToken).GetAsyncEnumerator();
        while (true)
        {
            ChatResponseUpdate update;
            try
            {
                if (!await enumerator.MoveNextAsync())
                    break;
                update = enumerator.Current;
            }
            catch (Exception ex)
            {
                FlushPendingSegments(includeResponseFinishReason: false);
                collector.RecordError(sessionKey, ex.Message);
                throw;
            }

            state.LastUpdate = update;
            var sawTextContent = false;

            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextReasoningContent reasoning:
                    {
                        if (ReasoningContentHelper.TryGetText(reasoning, out var text))
                            AppendThinking(text);
                        break;
                    }

                    case TextContent text:
                    {
                        sawTextContent = true;
                        AppendResponse(text.Text, update);
                        break;
                    }

                    case FunctionCallContent fc:
                    {
                        FlushPendingSegments(includeResponseFinishReason: false);
                        var callId = fc.CallId ?? "";
                        if (state.ProcessedCallIds.Add($"call:{callId}"))
                        {
                            collector.RecordToolCallStarted(sessionKey, fc);
                            if (!string.IsNullOrEmpty(callId))
                            {
                                state.ToolTimers[callId] = Stopwatch.StartNew();
                                state.ToolNameMap[callId] = fc.Name;
                            }
                        }
                        break;
                    }
                    case FunctionResultContent fr:
                    {
                        FlushPendingSegments(includeResponseFinishReason: false);
                        var resultCallId = fr.CallId;
                        if (state.ProcessedCallIds.Add($"result:{resultCallId}"))
                        {
                            if (state.ToolTimers.TryGetValue(resultCallId, out var timer))
                            {
                                timer.Stop();
                                var toolName = state.ToolNameMap.GetValueOrDefault(resultCallId, "unknown");
                                collector.RecordToolCallCompleted(sessionKey, fr, toolName, timer.ElapsedMilliseconds);
                                state.ToolTimers.Remove(resultCallId);
                                state.ToolNameMap.Remove(resultCallId);
                            }
                        }
                        break;
                    }
                    case UsageContent usage:
                    {
                        if (usage.Details.InputTokenCount.HasValue)
                            inputTokens = usage.Details.InputTokenCount.Value;
                        if (usage.Details.OutputTokenCount.HasValue)
                            outputTokens = usage.Details.OutputTokenCount.Value;
                        break;
                    }
                }
            }

            if (!sawTextContent && !string.IsNullOrEmpty(update.Text))
                AppendResponse(update.Text, update);

            yield return update;
        }

        FlushPendingSegments(includeResponseFinishReason: true);

        if (inputTokens > 0 || outputTokens > 0)
            collector.RecordTokenUsage(sessionKey, inputTokens, outputTokens);
    }

    private void RecordRequestIfFirst(string sessionKey, IList<ChatMessage> messages, SessionCallState state)
    {
        if (state.RequestRecorded)
            return;

        // Find the last user message anywhere in the message list
        var lastUserMsg = messages.LastOrDefault(m => m.Role == ChatRole.User);
        if (lastUserMsg != null)
        {
            var text = lastUserMsg.Text;
            if (!string.IsNullOrEmpty(text))
            {
                state.RequestRecorded = true;
                collector.RecordRequest(sessionKey, text);
            }
        }
    }

    private void RecordToolCallsFromResponse(
        string sessionKey,
        IList<ChatMessage> responseMessages,
        SessionCallState state)
    {
        foreach (var msg in responseMessages)
        {
            // Record FunctionCallContent
            foreach (var fc in msg.Contents.OfType<FunctionCallContent>())
            {
                var callId = fc.CallId;
                if (!state.ProcessedCallIds.Add($"call:{callId}")) continue;

                collector.RecordToolCallStarted(sessionKey, fc);
                if (!string.IsNullOrEmpty(callId))
                {
                    state.ToolTimers[callId] = Stopwatch.StartNew();
                    state.ToolNameMap[callId] = fc.Name;
                }
            }

            // Record FunctionResultContent
            foreach (var fr in msg.Contents.OfType<FunctionResultContent>())
            {
                var callId = fr.CallId;
                if (!state.ProcessedCallIds.Add($"result:{callId}")) continue;

                if (state.ToolTimers.TryGetValue(callId, out var timer))
                {
                    timer.Stop();
                    var toolName = state.ToolNameMap.GetValueOrDefault(callId, "unknown");
                    collector.RecordToolCallCompleted(sessionKey, fr, toolName, timer.ElapsedMilliseconds);
                    state.ToolTimers.Remove(callId);
                    state.ToolNameMap.Remove(callId);
                }
            }
        }
    }

    /// <summary>
    /// Holds per-session state shared across multiple calls from FunctionInvokingChatClient.
    /// </summary>
    private sealed class SessionCallState
    {
        public bool RequestRecorded;
        public readonly HashSet<string> ProcessedCallIds = new();
        public readonly Dictionary<string, Stopwatch> ToolTimers = new();
        public readonly Dictionary<string, string> ToolNameMap = new();
        public ChatResponseUpdate? LastUpdate;
    }
}
