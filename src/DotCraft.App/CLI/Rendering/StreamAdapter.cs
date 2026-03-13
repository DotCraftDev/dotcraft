using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotCraft.Context;
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
}


