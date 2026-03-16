using System.Text;
using DotCraft.Sessions.Protocol;
using DotCraft.Tools;
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
/// Shared agent execution logic used across all channel modes for running an agent session
/// via <see cref="ISessionService"/>. Used for heartbeat and cron-triggered runs.
/// </summary>
public sealed class AgentRunner(string workspacePath, ISessionService? sessionService = null)
{
    /// <summary>
    /// Run agent with a prompt, manage session lifecycle, stream output, and log results.
    /// Delegates to <see cref="ISessionService"/> for unified Thread management.
    /// </summary>
    public async Task<string?> RunAsync(string prompt, string sessionKey, CancellationToken cancellationToken = default)
    {
        if (sessionService == null)
            return null;

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

        AnsiConsole.MarkupLine($"[grey][[{tag}]][/] Running: [dim]{Markup.Escape(prompt.Length > 120 ? prompt[..120] + "..." : prompt)}[/]");

        // Build identity for this session type
        var channelName = sessionKey.StartsWith("heartbeat") ? "heartbeat"
            : sessionKey.StartsWith("cron:") ? "cron"
            : "agent";
        var identity = new SessionIdentity
        {
            ChannelName = channelName,
            UserId = sessionKey,
            WorkspacePath = workspacePath
        };

        // Find or create a Thread for this session key
        IReadOnlyList<ThreadSummary> existing;
        try { existing = await sessionService.FindThreadsAsync(identity, ct: cancellationToken); }
        catch { existing = []; }

        SessionThread thread;
        var matchingThread = existing.FirstOrDefault(s => s.Status != ThreadStatus.Archived);
        if (matchingThread != null)
            thread = await sessionService.ResumeThreadAsync(matchingThread.Id, cancellationToken);
        else
            thread = await sessionService.CreateThreadAsync(identity, ct: cancellationToken);

        // Consume the event stream and accumulate the agent response text
        var sb = new StringBuilder();
        await foreach (var evt in sessionService.SubmitInputAsync(thread.Id, prompt, ct: cancellationToken))
        {
            switch (evt.EventType)
            {
                case SessionEventType.ItemDelta when evt.DeltaPayload is { } delta && !string.IsNullOrEmpty(delta.TextDelta):
                    sb.Append(delta.TextDelta);
                    break;

                case SessionEventType.ItemStarted when evt.ItemPayload?.Type == ItemType.ToolCall &&
                                                       evt.ItemPayload.AsToolCall is { } tc:
                {
                    var icon = ToolRegistry.GetToolIcon(tc.ToolName);
                    var displayText = tc.Arguments != null
                        ? ToolRegistry.FormatToolCall(tc.ToolName, tc.Arguments) ?? tc.ToolName
                        : tc.ToolName;
                    AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [yellow]{Markup.Escape($"{icon} {displayText}")}[/]");
                    break;
                }

                case SessionEventType.ItemCompleted when evt.ItemPayload?.Type == ItemType.ToolResult &&
                                                         evt.ItemPayload.AsToolResult is { } tr:
                {
                    var result = tr.Result;
                    var preview = result.Length > 200 ? result[..200] + "..." : result;
                    var normalized = preview.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
                    AnsiConsole.MarkupLine($"[grey][[{tag}]][/]   [grey]{Markup.Escape(normalized)}[/]");
                    break;
                }

                case SessionEventType.TurnCompleted:
                {
                    var usage = evt.TurnPayload?.TokenUsage;
                    if (usage != null && (usage.InputTokens > 0 || usage.OutputTokens > 0))
                        AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [blue]↑ {usage.InputTokens} input[/] [green]↓ {usage.OutputTokens} output[/]");
                    break;
                }

                case SessionEventType.TurnFailed:
                {
                    var errMsg = evt.TurnPayload?.Error ?? "Turn failed";
                    AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [red]Turn failed: {Markup.Escape(errMsg)}[/]");
                    return null;
                }
            }
        }

        var response = sb.Length > 0 ? sb.ToString() : null;
        if (response != null)
        {
            AnsiConsole.MarkupLine($"[grey][[{tag}]][/] Response: [dim]{Markup.Escape(response.Length > 200 ? response[..200] + "..." : response)}[/]");
        }

        return response;
    }
}
