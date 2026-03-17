using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DotCraft.Context;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.CLI.Rendering;

/// <summary>
/// Adapter to convert AIAgent streaming output into RenderEvent stream
/// </summary>
public static partial class StreamAdapter
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new() 
    { 
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Matches three or more consecutive newlines (with optional \r) for collapsing.
    /// Preserves double newlines (blank lines) which are structurally significant in markdown.
    /// </summary>
    [GeneratedRegex(@"(\r?\n){3,}")]
    private static partial Regex ExcessiveNewlineRegex();

    /// <summary>
    /// Adapt AIAgent streaming output to RenderEvent stream
    /// </summary>
    public static async IAsyncEnumerable<RenderEvent> AdaptAsync(
        IAsyncEnumerable<AgentResponseUpdate> stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        TokenTracker? tokenTracker = null)
    {
        // Track CallId → (icon, name, argsJson, formattedDisplay) so that ToolCompleted events are
        // self-contained even when multiple tool calls run in parallel.
        var callIdMap = new Dictionary<string, (string Icon, string Name, string? ArgsJson, string? FormattedDisplay)>();

        long inputTokens = 0;
        long outputTokens = 0;

        // Cross-iteration flag: once we see a tool call/result, suppress all text
        // until we get a non-whitespace text update that is NOT accompanied by tool content.
        // This prevents stray newlines/text that LLMs emit *between* consecutive tool calls
        // (each arriving in a separate update message) from leaking through.
        bool insideToolSequence = false;

        await foreach (var update in stream.WithCancellation(cancellationToken))
        {
            // Handle tool calls and results.
            bool hasToolContent = false;

            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextReasoningContent reasoning:
                    {
                        if (Agents.ReasoningContentHelper.TryGetText(reasoning, out var reasoningText))
                            yield return RenderEvent.Thinking("💭", "Thinking", reasoningText);
                        break;
                    }

                    case UsageContent usage:
                    {
                        var iterInput = usage.Details.InputTokenCount ?? 0;
                        var iterOutput = usage.Details.OutputTokenCount ?? 0;
                        if (iterInput > 0 || iterOutput > 0)
                        {
                            inputTokens += iterInput;
                            outputTokens += iterOutput;
                            tokenTracker?.Update(iterInput, iterOutput);
                        }
                        break;
                    }

                    case FunctionCallContent functionCall:
                    {
                        hasToolContent = true;

                        var icon = ToolRegistry.GetToolIcon(functionCall.Name);
                        var title = functionCall.Name;

                        // Serialize args for debug mode fallback
                        string? argsJson = null;
                        if (functionCall.Arguments != null)
                        {
                            try
                            {
                                argsJson = JsonSerializer.Serialize(functionCall.Arguments, JsonSerializerOptions);
                            }
                            catch
                            {
                                argsJson = functionCall.Arguments.ToString();
                            }
                        }

                        // Human-readable formatted description
                        var formattedDisplay = ToolRegistry.FormatToolCall(functionCall.Name, functionCall.Arguments);

                        if (!string.IsNullOrEmpty(functionCall.CallId))
                            callIdMap[functionCall.CallId] = (icon, title, argsJson, formattedDisplay);

                        yield return RenderEvent.ToolStarted(
                            icon,
                            title,
                            string.Empty,
                            argsJson,
                            formattedDisplay,
                            callId: functionCall.CallId);
                        break;
                    }

                    case FunctionResultContent functionResult:
                    {
                        hasToolContent = true;

                        var result = Agents.ImageContentSanitizingChatClient.DescribeResult(functionResult.Result);

                        string? icon = null;
                        string? title = null;
                        string? argsJson = null;
                        string? formattedDisplay = null;
                        if (!string.IsNullOrEmpty(functionResult.CallId) &&
                            callIdMap.TryGetValue(functionResult.CallId, out var info))
                        {
                            icon = info.Icon;
                            title = info.Name;
                            argsJson = info.ArgsJson;
                            formattedDisplay = info.FormattedDisplay;
                            callIdMap.Remove(functionResult.CallId);
                        }

                        yield return RenderEvent.ToolCompleted(
                            icon,
                            title,
                            argsJson ?? string.Empty,  // Content = raw args (debug fallback)
                            result,
                            formattedDisplay,
                            callId: functionResult.CallId);
                        break;
                    }
                }
            }

            // Update cross-iteration tool sequence tracking.
            if (hasToolContent)
            {
                insideToolSequence = true;
                continue; // Skip any text in the same update as tool content.
            }

            // Skip null/empty text (no content at all).
            if (string.IsNullOrEmpty(update.Text))
            {
                continue;
            }

            // Inside a tool sequence: suppress whitespace-only chunks to prevent
            // blank-line spam between consecutive tool calls, and trim leading
            // newlines from the first substantive text chunk after tools finish.
            if (insideToolSequence)
            {
                if (string.IsNullOrWhiteSpace(update.Text))
                {
                    continue;
                }

                var rawText = update.Text.TrimStart('\r', '\n');
                insideToolSequence = false;

                if (string.IsNullOrWhiteSpace(rawText))
                {
                    continue;
                }

                yield return RenderEvent.Response(rawText);
                continue;
            }

            insideToolSequence = false;

            // Normal response text: preserve whitespace structure (including blank
            // lines) which is critical for markdown block-level parsing. Only
            // collapse 3+ consecutive newlines to 2 to prevent excessive spacing.
            var text = ExcessiveNewlineRegex().Replace(update.Text, "\n\n");
            if (!string.IsNullOrEmpty(text))
            {
                yield return RenderEvent.Response(text);
            }
        }

        if (inputTokens > 0 || outputTokens > 0)
        {
            var displayInput = (tokenTracker?.TotalInputTokens ?? inputTokens)
                             + (tokenTracker?.SubAgentInputTokens ?? 0);
            var displayOutput = (tokenTracker?.TotalOutputTokens ?? outputTokens)
                              + (tokenTracker?.SubAgentOutputTokens ?? 0);
            yield return RenderEvent.TokenUsage(displayInput, displayOutput, displayInput + displayOutput);
        }

        yield return RenderEvent.Completed(string.Empty);
    }

    /// <summary>
    /// Adapts an <see cref="IAsyncEnumerable{SessionEvent}"/> stream produced by
    /// <see cref="ISessionService.SubmitInputAsync"/> into a
    /// <see cref="RenderEvent"/> stream consumed by <see cref="AgentRenderer"/>.
    /// <para>
    /// Approval events (<see cref="SessionEventType.ApprovalRequested"/> /
    /// <see cref="SessionEventType.ApprovalResolved"/>) are translated to
    /// <see cref="RenderEventType.ApprovalRequired"/> / <see cref="RenderEventType.ApprovalCompleted"/>
    /// so the renderer can pause its spinner while the channel adapter handles the user prompt.
    /// </para>
    /// </summary>
    public static async IAsyncEnumerable<RenderEvent> AdaptSessionEventsAsync(
        IAsyncEnumerable<SessionEvent> events,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // callId → (icon, name, argsJson, formattedDisplay)
        var callIdMap = new Dictionary<string, (string? Icon, string? Name, string? ArgsJson, string? FormattedDisplay)>();

        await foreach (var evt in events.WithCancellation(ct))
        {
            switch (evt.EventType)
            {
                // ---------------------------------------------------------
                // Agent text streaming
                // ---------------------------------------------------------
                case SessionEventType.ItemDelta when evt.DeltaPayload is { } delta:
                {
                    var text = delta.TextDelta;
                    if (!string.IsNullOrEmpty(text))
                        yield return RenderEvent.Response(text);
                    break;
                }

                case SessionEventType.ItemDelta when evt.ReasoningDeltaPayload is { } reasoning:
                {
                    var text = reasoning.TextDelta;
                    if (!string.IsNullOrEmpty(text))
                        yield return RenderEvent.Thinking("💭", "Thinking", text);
                    break;
                }

                // ---------------------------------------------------------
                // Tool calls
                // ---------------------------------------------------------
                case SessionEventType.ItemStarted when evt.ItemPayload?.Type == ItemType.ToolCall:
                {
                    var item = evt.ItemPayload!;
                    var toolPayload = item.Payload as ToolCallPayload;
                    var toolName = toolPayload?.ToolName ?? string.Empty;
                    var icon = ToolRegistry.GetToolIcon(toolName);
                    string? argsJson = null;
                    string? formattedDisplay = null;
                    if (toolPayload?.Arguments != null)
                    {
                        try { argsJson = toolPayload.Arguments.ToJsonString(); }
                        catch { argsJson = toolPayload.Arguments.ToString(); }
                        formattedDisplay = ToolRegistry.FormatToolCall(toolName, toolPayload.Arguments);
                    }
                    var callId = toolPayload?.CallId;
                    if (!string.IsNullOrEmpty(callId))
                        callIdMap[callId] = (icon, toolName, argsJson, formattedDisplay);
                    yield return RenderEvent.ToolStarted(icon, toolName, string.Empty, argsJson, formattedDisplay, callId: callId);
                    break;
                }

                case SessionEventType.ItemCompleted when evt.ItemPayload?.Type == ItemType.ToolResult:
                {
                    var item = evt.ItemPayload!;
                    var resultPayload = item.Payload as ToolResultPayload;
                    var callId = resultPayload?.CallId;
                    string? icon = null, name = null, argsJson = null, formattedDisplay = null;
                    if (!string.IsNullOrEmpty(callId) && callIdMap.TryGetValue(callId, out var info))
                    {
                        icon = info.Icon;
                        name = info.Name;
                        argsJson = info.ArgsJson;
                        formattedDisplay = info.FormattedDisplay;
                        callIdMap.Remove(callId);
                    }
                    yield return RenderEvent.ToolCompleted(icon, name, argsJson ?? string.Empty, resultPayload?.Result, formattedDisplay, callId: callId);
                    break;
                }

                // ---------------------------------------------------------
                // Approval flow
                // ---------------------------------------------------------
                case SessionEventType.ApprovalRequested:
                    yield return RenderEvent.ApprovalRequest();
                    break;

                case SessionEventType.ApprovalResolved:
                    yield return RenderEvent.ApprovalComplete();
                    break;

                // ---------------------------------------------------------
                // Turn completed / failed
                // ---------------------------------------------------------
                case SessionEventType.TurnCompleted:
                {
                    var turn = evt.TurnPayload;
                    var usage = turn?.TokenUsage;
                    if (usage != null)
                        yield return RenderEvent.TokenUsage(usage.InputTokens, usage.OutputTokens, usage.TotalTokens);
                    yield return RenderEvent.Completed(string.Empty);
                    break;
                }

                case SessionEventType.TurnFailed:
                {
                    var turn = evt.TurnPayload;
                    var errMsg = turn?.Error ?? "Turn failed";
                    yield return RenderEvent.ErrorEvent(errMsg);
                    yield return RenderEvent.Completed(string.Empty);
                    break;
                }

                // ---------------------------------------------------------
                // SubAgent progress (aggregated snapshot)
                // ---------------------------------------------------------
                case SessionEventType.SubAgentProgress when evt.SubAgentProgressPayload is { } progress:
                {
                    yield return RenderEvent.SubAgentProgressUpdate(progress.Entries);
                    break;
                }

                // All other events (thread/created, turn/started, item/started for non-tool, etc.) are ignored.
            }
        }
    }

    /// <summary>
    /// Adapts an <see cref="IAsyncEnumerable{JsonDocument}"/> of JSON-RPC notifications
    /// produced by <see cref="DotCraft.Protocol.AppServer.AppServerWireClient.ReadTurnNotificationsAsync"/>
    /// into the same <see cref="RenderEvent"/> stream consumed by <see cref="AgentRenderer"/>.
    ///
    /// This is the wire-protocol counterpart of <see cref="AdaptSessionEventsAsync"/> for use
    /// when the CLI connects to the AppServer over the JSON-RPC 2.0 wire protocol.
    ///
    /// Approval flow: <c>item/approval/request</c> server requests are handled out-of-band by
    /// <c>AppServerWireClient.ServerRequestHandler</c> before reaching this adapter. The adapter
    /// observes only the resulting <c>item/started (approvalRequest)</c> and <c>item/approval/resolved</c>
    /// notifications for spinner control.
    /// </summary>
    public static async IAsyncEnumerable<RenderEvent> AdaptWireNotificationsAsync(
        IAsyncEnumerable<JsonDocument> notifications,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var callIdMap = new Dictionary<string, (string? Icon, string? Name, string? ArgsJson, string? FormattedDisplay)>();

        await foreach (var doc in notifications.WithCancellation(ct))
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("method", out var methodEl)) continue;
            var method = methodEl.GetString() ?? string.Empty;

            var hasParams = root.TryGetProperty("params", out var @params);

            switch (method)
            {
                // ---------------------------------------------------------
                // Agent text and reasoning streaming (spec Section 6.3)
                // ---------------------------------------------------------
                case AppServerMethods.ItemAgentMessageDelta:
                {
                    if (!hasParams) break;
                    var delta = @params.TryGetProperty("delta", out var d) ? d.GetString() : null;
                    if (!string.IsNullOrEmpty(delta))
                        yield return RenderEvent.Response(delta);
                    break;
                }

                case AppServerMethods.ItemReasoningDelta:
                {
                    if (!hasParams) break;
                    var delta = @params.TryGetProperty("delta", out var d) ? d.GetString() : null;
                    if (!string.IsNullOrEmpty(delta))
                        yield return RenderEvent.Thinking("💭", "Thinking", delta);
                    break;
                }

                // ---------------------------------------------------------
                // Item lifecycle (spec Section 6.3)
                // ---------------------------------------------------------
                case AppServerMethods.ItemStarted:
                {
                    if (!hasParams || !@params.TryGetProperty("item", out var item)) break;
                    var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;

                    if (type == "toolCall")
                    {
                        var payload = item.TryGetProperty("payload", out var p) ? p : default;
                        var toolName = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("toolName", out var tn)
                            ? tn.GetString() : null;
                        var callId = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("callId", out var ci)
                            ? ci.GetString() : null;
                        var icon = ToolRegistry.GetToolIcon(toolName ?? string.Empty);
                        string? argsJson = null, formattedDisplay = null;
                        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("arguments", out var args)
                            && args.ValueKind == JsonValueKind.Object)
                        {
                            argsJson = args.GetRawText();
                            var argsNode = JsonNode.Parse(argsJson) as JsonObject;
                            formattedDisplay = ToolRegistry.FormatToolCall(toolName ?? string.Empty, argsNode);
                        }
                        if (!string.IsNullOrEmpty(callId))
                            callIdMap[callId] = (icon, toolName, argsJson, formattedDisplay);
                        yield return RenderEvent.ToolStarted(icon, toolName, string.Empty, argsJson, formattedDisplay, callId: callId);
                    }
                    else if (type == "approvalRequest")
                    {
                        // Signal the renderer to pause the spinner while approval is handled
                        // out-of-band by AppServerWireClient.ServerRequestHandler.
                        yield return RenderEvent.ApprovalRequest();
                    }
                    break;
                }

                case AppServerMethods.ItemCompleted:
                {
                    if (!hasParams || !@params.TryGetProperty("item", out var item)) break;
                    var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;

                    if (type == "toolResult")
                    {
                        var payload = item.TryGetProperty("payload", out var p) ? p : default;
                        var callId = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("callId", out var ci)
                            ? ci.GetString() : null;
                        var result = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("result", out var r)
                            ? r.GetString() : null;
                        string? icon = null, name = null, argsJson = null, formattedDisplay = null;
                        if (!string.IsNullOrEmpty(callId) && callIdMap.TryGetValue(callId!, out var info))
                        {
                            (icon, name, argsJson, formattedDisplay) = info;
                            callIdMap.Remove(callId!);
                        }
                        yield return RenderEvent.ToolCompleted(icon, name, argsJson ?? string.Empty, result, formattedDisplay, callId: callId);
                    }
                    break;
                }

                // ---------------------------------------------------------
                // Approval flow (spec Section 7)
                // ---------------------------------------------------------
                case AppServerMethods.ItemApprovalResolved:
                {
                    yield return RenderEvent.ApprovalComplete();
                    break;
                }

                // ---------------------------------------------------------
                // Turn lifecycle (spec Section 6.2)
                // ---------------------------------------------------------
                case AppServerMethods.TurnCompleted:
                {
                    if (hasParams && @params.TryGetProperty("turn", out var turn)
                        && turn.TryGetProperty("tokenUsage", out var tu)
                        && tu.ValueKind == JsonValueKind.Object)
                    {
                        var inputTokens = tu.TryGetProperty("inputTokens", out var it) ? it.GetInt64() : 0L;
                        var outputTokens = tu.TryGetProperty("outputTokens", out var ot) ? ot.GetInt64() : 0L;
                        var totalTokens = tu.TryGetProperty("totalTokens", out var tt) ? tt.GetInt64() : inputTokens + outputTokens;
                        if (inputTokens > 0 || outputTokens > 0)
                            yield return RenderEvent.TokenUsage(inputTokens, outputTokens, totalTokens);
                    }
                    yield return RenderEvent.Completed(string.Empty);
                    break;
                }

                case AppServerMethods.TurnFailed:
                {
                    var errMsg = hasParams && @params.TryGetProperty("error", out var e)
                        ? e.GetString() : null;
                    yield return RenderEvent.ErrorEvent(errMsg ?? "Turn failed");
                    yield return RenderEvent.Completed(string.Empty);
                    break;
                }

                case AppServerMethods.TurnCancelled:
                {
                    yield return RenderEvent.Completed("cancelled");
                    break;
                }

                // ---------------------------------------------------------
                // SubAgent progress (spec Section 6.5)
                // ---------------------------------------------------------
                case AppServerMethods.SubAgentProgress:
                {
                    if (!hasParams || !@params.TryGetProperty("entries", out var entriesEl)
                        || entriesEl.ValueKind != JsonValueKind.Array)
                        break;

                    var entries = new List<Protocol.SubAgentProgressEntry>();
                    foreach (var item in entriesEl.EnumerateArray())
                    {
                        var label = item.TryGetProperty("label", out var l) ? l.GetString() : null;
                        if (string.IsNullOrEmpty(label)) continue;

                        entries.Add(new Protocol.SubAgentProgressEntry
                        {
                            Label = label!,
                            CurrentTool = item.TryGetProperty("currentTool", out var ct2) && ct2.ValueKind == JsonValueKind.String
                                ? ct2.GetString() : null,
                            InputTokens = item.TryGetProperty("inputTokens", out var it) ? it.GetInt64() : 0L,
                            OutputTokens = item.TryGetProperty("outputTokens", out var ot) ? ot.GetInt64() : 0L,
                            IsCompleted = item.TryGetProperty("isCompleted", out var ic) && ic.GetBoolean()
                        });
                    }

                    if (entries.Count > 0)
                        yield return RenderEvent.SubAgentProgressUpdate(entries);
                    break;
                }

                // All other notifications (thread/*, turn/started, item/started for non-tool, etc.) are ignored.
            }
        }
    }
}


