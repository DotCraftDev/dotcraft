using System.Text;
using DotCraft.Context;
using DotCraft.Tracing;
using DotCraft.Hooks;
using DotCraft.Sessions;
using DotCraft.Sessions.Protocol;
using DotCraft.Memory;
using DotCraft.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace DotCraft.Agents;

/// <summary>
/// Delegate for running an agent session.
/// </summary>
public delegate Task<string?> AgentRunSessionDelegate(
    string prompt,
    string sessionKey,
    CancellationToken cancellationToken = default);

/// <summary>
/// Shared agent execution logic used across all channel modes for running an agent session.
/// When <paramref name="sessionService"/> is provided, execution is delegated to Session Core
/// and legacy session files are automatically wrapped as Threads.
/// </summary>
public sealed class AgentRunner(
    AIAgent agent,
    SessionStore sessionStore,
    AgentFactory? agentFactory = null,
    TraceCollector? traceCollector = null,
    SessionGate? sessionGate = null,
    HookRunner? hookRunner = null,
    ISessionService? sessionService = null)
{
    private static string AppendRuntimeContext(string prompt) => RuntimeContextBuilder.AppendTo(prompt);

    /// <summary>
    /// Run agent with a prompt, manage session lifecycle, stream output, and log results.
    /// When a SessionService is configured, delegates to Session Core for unified Thread management.
    /// </summary>
    public async Task<string?> RunAsync(string prompt, string sessionKey, CancellationToken cancellationToken = default)
    {
        if (sessionService != null)
            return await RunViaSessionServiceAsync(prompt, sessionKey, cancellationToken);
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
            {
                gateLock = await sessionGate.AcquireAsync(sessionKey, cancellationToken);
            }
        }
        catch (SessionGateOverflowException)
        {
            AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [yellow]Request evicted for session {Markup.Escape(sessionKey)} (queue overflow)[/]");
            return null;
        }

        try
        {
            var session = await sessionStore.LoadOrCreateAsync(agent, sessionKey, cancellationToken);
            var sb = new StringBuilder();
            long inputTokens = 0, outputTokens = 0;
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
            TokenTracker.Current = tokenTracker;
            try
            {
                await foreach (var update in agent.RunStreamingAsync(prompt, session).WithCancellation(cancellationToken))
                {
                    foreach (var content in update.Contents)
                    {
                        switch (content)
                        {
                            case TextReasoningContent reasoning:
                            {
                                if (ReasoningContentHelper.TryGetText(reasoning, out var text))
                                {
                                    var preview = ReasoningContentHelper.ToInlinePreview(text);
                                    AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [cyan]💭 Thinking[/] [grey]{Markup.Escape(preview)}[/]");
                                    ReasoningContentHelper.AppendBlock(sb, text);
                                }
                                break;
                            }

                            case FunctionCallContent fc:
                            {
                                var icon = ToolRegistry.GetToolIcon(fc.Name);
                                var displayText = ToolRegistry.FormatToolCall(fc.Name, fc.Arguments) ?? fc.Name;
                                AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [yellow]{Markup.Escape($"{icon} {displayText}")}[/]");
                                break;
                            }
                            case FunctionResultContent fr:
                            {
                                var result = ImageContentSanitizingChatClient.DescribeResult(fr.Result);
                                var preview = result.Length > 200 ? result[..200] + "..." : result;
                                var normalized = preview.Replace("\r\n", " ")
                                    .Replace('\n', ' ')
                                    .Replace('\r', ' ')
                                    .Trim();
                                AnsiConsole.MarkupLine($"[grey][[{tag}]][/]   [grey]{Markup.Escape(normalized)}[/]");
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
                        }
                    }

                    if (!string.IsNullOrEmpty(update.Text)) sb.Append(update.Text);
                }
            }
            finally
            {
                TracingChatClient.ResetCallState(sessionKey);
                TracingChatClient.CurrentSessionKey = null;
                TokenTracker.Current = null;
            }

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

            if (inputTokens > 0 || outputTokens > 0)
            {
                var displayInput = (tokenTracker?.TotalInputTokens ?? inputTokens)
                                 + (tokenTracker?.SubAgentInputTokens ?? 0);
                var displayOutput = (tokenTracker?.TotalOutputTokens ?? outputTokens)
                                  + (tokenTracker?.SubAgentOutputTokens ?? 0);
                AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [blue]↑ {displayInput} input[/] [green]↓ {displayOutput} output[/]");
            }

            // Use LastInputTokens for compaction: it reflects the most recent context window size
            if (agentFactory is { Compactor: not null, MaxContextTokens: > 0 } &&
                (tokenTracker?.LastInputTokens ?? inputTokens) >= agentFactory.MaxContextTokens)
            {
                AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [yellow]Context compacting...[/]");
                if (await agentFactory.Compactor.TryCompactAsync(session, cancellationToken))
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

    // -------------------------------------------------------------------------
    // Session Core backward-compatibility path
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs via ISessionService while preserving the string return value contract.
    /// Finds or creates a Thread keyed by the legacy session key, runs the Turn,
    /// accumulates AgentMessage text from events, and returns it.
    /// </summary>
    private async Task<string?> RunViaSessionServiceAsync(
        string prompt,
        string sessionKey,
        CancellationToken cancellationToken)
    {
        // Apply cron prefix before passing to Session Core (runtime context is appended inside SessionService)
        if (sessionKey.StartsWith("cron:"))
        {
            prompt = $"[System: Scheduled Task Triggered]\n" +
                     $"The following is a scheduled cron job that has just been triggered. " +
                     $"Execute the task described below directly and respond with the result. " +
                     $"Do NOT treat this as a user conversation or create a new scheduled task.\n\n" +
                     $"Task: {prompt}";
        }

        // Derive a synthetic workspace path from the legacy key (best-effort)
        const string defaultWorkspace = "/";
        const string legacyChannel = "legacy";

        // Look for an existing Thread with a matching legacy session key
        var identity = new SessionIdentity
        {
            ChannelName = legacyChannel,
            UserId = sessionKey,
            WorkspacePath = defaultWorkspace
        };

        IReadOnlyList<ThreadSummary> existing;
        try
        {
            existing = await sessionService!.FindThreadsAsync(identity, cancellationToken);
        }
        catch
        {
            existing = [];
        }

        // Filter to threads that have the exact legacy session key in metadata
        var matchingThread = existing
            .FirstOrDefault(s => s.Metadata.TryGetValue("legacySessionKey", out var lk) && lk == sessionKey);

        SessionThread thread;
        if (matchingThread != null)
        {
            thread = await sessionService!.ResumeThreadAsync(matchingThread.Id, cancellationToken);
        }
        else
        {
            thread = await sessionService!.CreateThreadAsync(
                identity,
                config: null,
                ct: cancellationToken);

            // Record the legacy key so future lookups find this Thread
            thread.Metadata["legacySessionKey"] = sessionKey;
        }

        // Consume the event stream and accumulate the agent response text
        var sb = new StringBuilder();
        await foreach (var evt in sessionService!.SubmitInputAsync(thread.Id, prompt, ct: cancellationToken))
        {
            if (evt.EventType == SessionEventType.ItemCompleted
                && evt.ItemPayload?.Type == ItemType.AgentMessage
                && evt.ItemPayload.AsAgentMessage is { } agentMsg)
            {
                sb.Append(agentMsg.Text);
            }
            else if (evt.EventType == SessionEventType.TurnFailed)
            {
                var errMsg = evt.TurnPayload?.Error ?? "Turn failed";
                AnsiConsole.MarkupLine($"[grey][[Session]][/] [red]Turn failed: {Markup.Escape(errMsg)}[/]");
                return null;
            }
        }

        var response = sb.Length > 0 ? sb.ToString() : null;

        if (response != null)
        {
            AnsiConsole.MarkupLine(
                $"[grey][[Session]][/] Response: [dim]{Markup.Escape(response.Length > 200 ? response[..200] + "..." : response)}[/]");
        }

        return response;
    }
}