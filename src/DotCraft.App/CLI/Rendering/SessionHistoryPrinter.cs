using DotCraft.Sessions.Protocol;
using DotCraft.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace DotCraft.CLI.Rendering;

/// <summary>
/// Renders a scrollback of a restored session's conversation history
/// directly to the console after /load. Does not use the streaming render pipeline.
/// </summary>
public static class SessionHistoryPrinter
{
    private const int MaxUserLines = 2;
    private const int UserWrapWidth = 120;

    /// <summary>
    /// Prints a compact history of the last <paramref name="maxTurns"/> user turns.
    /// </summary>
    public static void Print(InMemoryChatHistoryProvider history, int maxTurns = 10)
    {
        var messages = history.ToList();
        if (messages.Count == 0) return;

        // Collect indices of user-role messages, then take the last maxTurns
        var userIndices = messages
            .Select((m, i) => (Message: m, Index: i))
            .Where(x => x.Message.Role == ChatRole.User)
            .Select(x => x.Index)
            .ToList();

        if (userIndices.Count == 0) return;

        var startIndex = userIndices.Count > maxTurns
            ? userIndices[^maxTurns]
            : userIndices[0];

        var slice = messages.Skip(startIndex).ToList();

        // Build a lookup of tool results by CallId from Tool-role messages
        var resultsByCallId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var msg in slice.Where(m => m.Role == ChatRole.Tool))
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionResultContent fr && !string.IsNullOrEmpty(fr.CallId))
                {
                    var text = fr.Result?.ToString() ?? string.Empty;
                    resultsByCallId[fr.CallId] = text;
                }
            }
        }

        AnsiConsole.WriteLine();

        var isFirstTurn = true;
        foreach (var msg in slice)
        {
            switch (msg.Role.Value)
            {
                case "user":
                    if (!isFirstTurn)
                        PrintSeparator();
                    isFirstTurn = false;
                    PrintUserMessage(msg);
                    break;

                case "assistant":
                    PrintAssistantMessage(msg, resultsByCallId);
                    break;

                // Tool-role messages contain FunctionResultContent already rendered
                // inline via the lookup above; skip them here.
            }
        }

        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Prints a compact history of the last <paramref name="maxTurns"/> turns from a
    /// Session Protocol <see cref="SessionThread"/>. Called after /load in Session Protocol mode.
    /// </summary>
    public static void Print(SessionThread thread, int maxTurns = 10)
    {
        if (thread.Turns.Count == 0) return;

        var turns = thread.Turns.Count > maxTurns
            ? thread.Turns.Skip(thread.Turns.Count - maxTurns).ToList()
            : thread.Turns;

        AnsiConsole.WriteLine();

        var isFirstTurn = true;
        foreach (var turn in turns)
        {
            if (turn.Status == TurnStatus.Cancelled) continue;

            if (!isFirstTurn)
                PrintSeparator();
            isFirstTurn = false;

            // User message
            var userText = (turn.Input?.Payload as UserMessagePayload)?.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(userText))
                PrintUserMessageText(StripRuntimeContext(userText));

            // Build a lookup of tool results by CallId for inline display
            var resultsByCallId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in turn.Items)
            {
                if (item.Type == ItemType.ToolResult && item.Payload is ToolResultPayload trp
                    && !string.IsNullOrEmpty(trp.CallId))
                    resultsByCallId[trp.CallId] = trp.Result;
            }

            // Agent and tool items (ToolResult items are rendered inline with their ToolCall)
            foreach (var item in turn.Items)
            {
                switch (item.Type)
                {
                    case ItemType.AgentMessage:
                    {
                        var text = (item.Payload as AgentMessagePayload)?.Text ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(text))
                            PrintAssistantText(text);
                        break;
                    }
                    case ItemType.ToolCall:
                    {
                        if (item.Payload is ToolCallPayload tc)
                            PrintToolCallWithResult(tc, resultsByCallId);
                        break;
                    }
                }
            }
        }

        AnsiConsole.WriteLine();
    }

    private static void PrintUserMessageText(string text)
    {
        var lines = WrapText(text.Trim(), UserWrapWidth - 2);
        var display = lines.Count > MaxUserLines
            ? string.Join("\n", lines.Take(MaxUserLines)) + " [dim]...[/]"
            : string.Join("\n", lines);

        AnsiConsole.MarkupLine($"[grey]>[/] [white]You:[/] {Markup.Escape(display.Split('\n')[0])}");
        foreach (var line in display.Split('\n').Skip(1))
            AnsiConsole.MarkupLine($"       {Markup.Escape(line)}");
    }

    private static void PrintToolCallWithResult(
        ToolCallPayload tc, Dictionary<string, string> resultsByCallId)
    {
        var icon = ToolRegistry.GetToolIcon(tc.ToolName);
        var display = ToolRegistry.FormatToolCall(tc.ToolName, tc.Arguments) ?? tc.ToolName;
        AnsiConsole.MarkupLine($"  [yellow]{Markup.Escape($"{icon} {display}")}[/]");

        if (!string.IsNullOrEmpty(tc.CallId)
            && resultsByCallId.TryGetValue(tc.CallId, out var rawResult))
        {
            var formatted = ToolRegistry.FormatToolResult(tc.ToolName, rawResult);
            if (formatted != null)
            {
                foreach (var line in formatted)
                    AnsiConsole.MarkupLine($"    [grey]{Markup.Escape(line)}[/]");
            }
            else if (!string.IsNullOrWhiteSpace(rawResult))
            {
                AnsiConsole.MarkupLine(
                    $"    [grey]{Markup.Escape(NormalizeInline(Truncate(rawResult, 200)))}[/]");
            }
        }
    }

    private static void PrintSeparator()
    {
        try
        {
            var width = Math.Min(Console.WindowWidth, UserWrapWidth);
            AnsiConsole.MarkupLine($"[dim]{new string('─', Math.Max(width, 20))}[/]");
        }
        catch
        {
            AnsiConsole.MarkupLine($"[dim]{new string('─', 60)}[/]");
        }
    }

    private static void PrintUserMessage(ChatMessage msg)
    {
        var text = StripRuntimeContext(msg.Text).Trim();
        if (string.IsNullOrEmpty(text)) return;

        var lines = WrapText(text, UserWrapWidth - 2);
        var display = lines.Count > MaxUserLines
            ? string.Join("\n", lines.Take(MaxUserLines)) + " [dim]...[/]"
            : string.Join("\n", lines);

        AnsiConsole.MarkupLine($"[grey]>[/] [white]You:[/] {Markup.Escape(display.Split('\n')[0])}");
        foreach (var line in display.Split('\n').Skip(1))
            AnsiConsole.MarkupLine($"       {Markup.Escape(line)}");
    }

    private static void PrintAssistantMessage(ChatMessage msg, Dictionary<string, string> resultsByCallId)
    {
        foreach (var content in msg.Contents)
        {
            switch (content)
            {
                case TextContent tc when !string.IsNullOrWhiteSpace(tc.Text):
                    PrintAssistantText(tc.Text);
                    break;

                case FunctionCallContent fc:
                    PrintToolCall(fc, resultsByCallId);
                    break;
            }
        }
    }

    private static void PrintAssistantText(string text)
    {
        MarkdownConsoleRenderer.Render(text.Trim());
    }

    private static void PrintToolCall(FunctionCallContent fc, Dictionary<string, string> resultsByCallId)
    {
        var icon = ToolRegistry.GetToolIcon(fc.Name);
        var display = ToolRegistry.FormatToolCall(fc.Name, fc.Arguments) ?? fc.Name;

        AnsiConsole.MarkupLine($"  [yellow]{Markup.Escape($"{icon} {display}")}[/]");

        // Print the result summary on an indented sub-line
        if (!string.IsNullOrEmpty(fc.CallId) && resultsByCallId.TryGetValue(fc.CallId, out var rawResult))
        {
            var formatted = ToolRegistry.FormatToolResult(fc.Name, rawResult);
            if (formatted != null)
            {
                foreach (var line in formatted)
                    AnsiConsole.MarkupLine($"    [grey]{Markup.Escape(line)}[/]");
            }
            else if (!string.IsNullOrWhiteSpace(rawResult))
            {
                var summary = NormalizeInline(Truncate(rawResult, 200));
                AnsiConsole.MarkupLine($"    [grey]{Markup.Escape(summary)}[/]");
            }
        }
    }

    /// <summary>
    /// Removes the [Runtime Context] block appended by RuntimeContextBuilder from stored user messages,
    /// so dynamic metadata (timestamps, workspace path, etc.) is not shown in history replay.
    /// </summary>
    private static string StripRuntimeContext(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var idx = text.IndexOf("\n\n[Runtime Context]", StringComparison.Ordinal);
        return idx >= 0 ? text[..idx] : text;
    }

    private static List<string> WrapText(string text, int maxWidth)
    {
        var result = new List<string>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length <= maxWidth)
            {
                result.Add(line);
                continue;
            }

            var pos = 0;
            while (pos < line.Length)
            {
                var chunk = line.Length - pos <= maxWidth
                    ? line[pos..]
                    : line.Substring(pos, maxWidth);
                result.Add(chunk);
                pos += maxWidth;
            }
        }

        return result;
    }

    private static string NormalizeInline(string text)
        => text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "...";
}
