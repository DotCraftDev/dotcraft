using System.Text;
using DotCraft.Context;
using DotCraft.DashBoard;
using DotCraft.Hooks;
using DotCraft.Sessions;
using DotCraft.Memory;
using DotCraft.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace DotCraft.Agents;

/// <summary>
/// Shared agent execution logic used across all channel modes.
/// </summary>
public sealed class AgentRunner(
    AIAgent agent,
    SessionStore sessionStore,
    AgentFactory? agentFactory = null,
    TraceCollector? traceCollector = null,
    SessionGate? sessionGate = null,
    HookRunner? hookRunner = null)
{
    private static string AppendRuntimeContext(string prompt) => RuntimeContextBuilder.AppendTo(prompt);

    /// <summary>
    /// Run agent with a prompt, manage session lifecycle, stream output, and log results.
    /// </summary>
    public async Task<string?> RunAsync(string prompt, string sessionKey, CancellationToken cancellationToken = default)
    {
        var tag = sessionKey.StartsWith("heartbeat") ? "Heartbeat"
            : sessionKey.StartsWith("cron:") ? "Cron"
            : "Agent";

        if (sessionKey.StartsWith("cron:"))
        {
            prompt = $"[System: Scheduled Task Triggered]\n" +
                     $"The following is a scheduled cron job that has just been triggered. " +
                     $"Execute the task described below directly and respond with the result. " +
                     $"Do NOT treat this as a user conversation or create a new scheduled task.\n\n" +
                     $"Task: {prompt}";
        }

        prompt = AppendRuntimeContext(prompt);

        AnsiConsole.MarkupLine(
            $"[grey][[{tag}]][/] Running: [dim]{Markup.Escape(prompt.Length > 120 ? prompt[..120] + "..." : prompt)}[/]");

        IDisposable? gateLock = null;
        try
        {
            if (sessionGate != null)
                gateLock = await sessionGate.AcquireAsync(sessionKey);
        }
        catch (SessionGateOverflowException)
        {
            AnsiConsole.MarkupLine(
                $"[grey][[{tag}]][/] [yellow]Request evicted for session {Markup.Escape(sessionKey)} (queue overflow)[/]");
            return null;
        }

        try
        {
            var session = await sessionStore.LoadOrCreateAsync(agent, sessionKey, cancellationToken);
            var sb = new StringBuilder();
            long inputTokens = 0, outputTokens = 0, totalTokens = 0;
            var tokenTracker = agentFactory?.GetOrCreateTokenTracker(sessionKey);

            traceCollector?.RecordSessionMetadata(
                sessionKey,
                null,
                agentFactory?.LastCreatedTools?.Select(t => t.Name));

            // Run PrePrompt hooks (can block the prompt)
            if (hookRunner != null)
            {
                var prePromptInput = new HookInput { SessionId = sessionKey, Prompt = prompt };
                var prePromptResult =
                    await hookRunner.RunAsync(HookEvent.PrePrompt, prePromptInput, cancellationToken);
                if (prePromptResult.Blocked)
                {
                    AnsiConsole.MarkupLine(
                        $"[grey][[{tag}]][/] [yellow]Prompt blocked by hook: {Markup.Escape(prePromptResult.BlockReason ?? "no reason")}[/]");
                    return $"Prompt blocked by hook: {prePromptResult.BlockReason ?? "no reason given"}";
                }
            }

            TracingChatClient.CurrentSessionKey = sessionKey;
            TracingChatClient.ResetCallState(sessionKey);
            try
            {
                await foreach (var update in agent.RunStreamingAsync(prompt, session).WithCancellation(cancellationToken))
                {
                    foreach (var content in update.Contents)
                    {
                        switch (content)
                        {
                            case FunctionCallContent fc:
                            {
                                var icon = ToolRegistry.GetToolIcon(fc.Name);
                                var displayText = ToolRegistry.FormatToolCall(fc.Name, fc.Arguments) ?? fc.Name;
                                AnsiConsole.MarkupLine(
                                    $"[grey][[{tag}]][/] [yellow]{Markup.Escape($"{icon} {displayText}")}[/]");
                                break;
                            }
                            case FunctionResultContent fr:
                            {
                                var result = ImageContentSanitizingChatClient.DescribeResult(fr.Result);
                                var preview = result.Length > 200 ? result[..200] + "..." : result;
                                var normalized = preview.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ')
                                    .Trim();
                                AnsiConsole.MarkupLine($"[grey][[{tag}]][/]   [grey]{Markup.Escape(normalized)}[/]");
                                break;
                            }
                            case UsageContent usage:
                            {
                                if (usage.Details.InputTokenCount.HasValue)
                                    inputTokens = usage.Details.InputTokenCount.Value;
                                if (usage.Details.OutputTokenCount.HasValue)
                                    outputTokens = usage.Details.OutputTokenCount.Value;
                                if (usage.Details.TotalTokenCount.HasValue)
                                    totalTokens = usage.Details.TotalTokenCount.Value;
                                break;
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(update.Text)) sb.Append(update.Text);
                }
            }
            finally
            {
                TracingChatClient.ResetCallState(sessionKey);
                TracingChatClient.CurrentSessionKey = null;
            }

            if (totalTokens == 0 && (inputTokens > 0 || outputTokens > 0))
                totalTokens = inputTokens + outputTokens;

            await sessionStore.SaveAsync(agent, session, sessionKey, cancellationToken);
            var response = sb.Length > 0 ? sb.ToString() : null;

            // Run Stop hooks after agent finishes responding
            if (hookRunner != null)
            {
                var stopInput = new HookInput { SessionId = sessionKey, Response = response };
                await hookRunner.RunAsync(HookEvent.Stop, stopInput, cancellationToken);
            }

            if (response != null)
            {
                AnsiConsole.MarkupLine($"[grey][[{tag}]][/] Response: [dim]{Markup.Escape(response.Length > 200 ? response[..200] + "..." : response)}[/]");
            }

            if (totalTokens > 0)
            {
                tokenTracker?.Update(inputTokens, outputTokens);
                var displayInput = tokenTracker?.LastInputTokens ?? inputTokens;
                var displayOutput = tokenTracker?.TotalOutputTokens ?? outputTokens;
                AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [blue]↑ {displayInput} input[/] [green]↓ {displayOutput} output[/]");
            }

            if (agentFactory is { Compactor: not null, MaxContextTokens: > 0 } &&
                inputTokens >= agentFactory.MaxContextTokens)
            {
                AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [yellow]Context compacting...[/]");
                if (await agentFactory.Compactor.TryCompactAsync(session))
                {
                    tokenTracker?.Reset();
                    traceCollector?.RecordContextCompaction(sessionKey);
                }
            }

            _ = agentFactory?.TryConsolidateMemory(session, sessionKey);

            return response;
        }
        finally
        {
            gateLock?.Dispose();
        }
    }
}