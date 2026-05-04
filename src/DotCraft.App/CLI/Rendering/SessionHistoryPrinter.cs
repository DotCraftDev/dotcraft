using System.Text.Json;
using DotCraft.Plugins;
using DotCraft.Protocol;
using DotCraft.Tools;
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
    /// Prints a compact history of the last <paramref name="maxTurns"/> turns from a
    /// <see cref="SessionWireThread"/> received over the wire protocol.
    /// </summary>
    public static void Print(SessionWireThread thread, int maxTurns = 10)
    {
        var turns = thread.Turns;
        if (turns == null || turns.Count == 0) return;

        var slice = turns.Count > maxTurns
            ? turns.Skip(turns.Count - maxTurns).ToList()
            : turns;

        AnsiConsole.WriteLine();

        var isFirstTurn = true;
        foreach (var turn in slice)
        {
            if (turn.Status == TurnStatus.Cancelled) continue;

            if (!isFirstTurn)
                PrintSeparator();
            isFirstTurn = false;

            // User message text
            var userText = GetWirePayloadString(turn.Items?.FirstOrDefault(i => i.Type == ItemType.UserMessage)?.Payload, "text");
            if (!string.IsNullOrWhiteSpace(userText))
                PrintUserMessageText(StripRuntimeContext(userText));

            // Build tool-result lookup by callId
            var resultsByCallId = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in turn.Items ?? [])
            {
                if (item.Type != ItemType.ToolResult) continue;
                var callId = GetWirePayloadString(item.Payload, "callId");
                var result = GetWirePayloadString(item.Payload, "result");
                if (!string.IsNullOrEmpty(callId))
                    resultsByCallId[callId!] = result ?? string.Empty;
            }

            // Render agent messages and tool calls
            foreach (var item in turn.Items ?? [])
            {
                switch (item.Type)
                {
                    case ItemType.AgentMessage:
                    {
                        var text = GetWirePayloadString(item.Payload, "text") ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(text))
                            PrintAssistantText(text);
                        break;
                    }
                    case ItemType.ToolCall:
                    {
                        var toolName = GetWirePayloadString(item.Payload, "toolName") ?? string.Empty;
                        var callId = GetWirePayloadString(item.Payload, "callId");
                        var argsJson = GetWirePayloadRaw(item.Payload, "arguments");
                        var argsNode = argsJson != null
                            ? System.Text.Json.Nodes.JsonNode.Parse(argsJson) as System.Text.Json.Nodes.JsonObject
                            : null;
                        var tc = new ToolCallPayload
                        {
                            ToolName = toolName,
                            CallId = callId ?? string.Empty,
                            Arguments = argsNode
                        };
                        PrintToolCallWithResult(tc, resultsByCallId);
                        break;
                    }
                    case ItemType.PluginFunctionCall:
                    {
                        var functionName = GetWirePayloadString(item.Payload, "functionName") ?? string.Empty;
                        var argsNode = ParseJsonObject(GetWirePayloadRaw(item.Payload, "arguments"));
                        var result = GetPluginFunctionResult(item.Payload);
                        PrintToolCallInline(functionName, argsNode, result);
                        break;
                    }
                }
            }
        }

        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Reads a string field from a wire item payload.
    /// The payload may be a typed C# object or a <see cref="JsonElement"/>.
    /// </summary>
    private static string? GetWirePayloadString(object? payload, string field) =>
        payload switch
        {
            UserMessagePayload um => field == "text" ? um.Text : null,
            AgentMessagePayload am => field == "text" ? am.Text : null,
            ToolCallPayload tc => field switch
            {
                "toolName" => tc.ToolName,
                "callId" => tc.CallId,
                _ => null
            },
            PluginFunctionCallPayload pc => field switch
            {
                "functionName" => pc.FunctionName,
                "callId" => pc.CallId,
                _ => null
            },
            ToolResultPayload tr => field switch
            {
                "callId" => tr.CallId,
                "result" => tr.Result,
                _ => null
            },
            JsonElement je when je.ValueKind == JsonValueKind.Object
                                && je.TryGetProperty(field, out var v)
                                && v.ValueKind == JsonValueKind.String
                => v.GetString(),
            _ => null
        };

    /// <summary>
    /// Reads a field from a wire item payload and returns its raw JSON text,
    /// for use when the field contains a nested object (e.g., tool arguments).
    /// </summary>
    private static string? GetWirePayloadRaw(object? payload, string field) =>
        payload switch
        {
            ToolCallPayload tc when field == "arguments" => tc.Arguments?.ToJsonString(),
            PluginFunctionCallPayload pc when field == "arguments" => pc.Arguments?.ToJsonString(),
            JsonElement je when je.ValueKind == JsonValueKind.Object
                                && je.TryGetProperty(field, out var v)
                => v.GetRawText(),
            _ => null
        };

    private static System.Text.Json.Nodes.JsonObject? ParseJsonObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject;
        }
        catch
        {
            return null;
        }
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
        string? rawResult = null;
        if (!string.IsNullOrEmpty(tc.CallId)
            && resultsByCallId.TryGetValue(tc.CallId, out var matchedResult))
        {
            rawResult = matchedResult;
        }
        PrintToolCallInline(tc.ToolName, tc.Arguments, rawResult);
    }

    private static void PrintToolCallInline(
        string toolName,
        System.Text.Json.Nodes.JsonObject? arguments,
        string? rawResult)
    {
        var icon = ToolRegistry.GetToolIcon(toolName);
        var display = ToolRegistry.FormatToolCall(toolName, arguments) ?? toolName;
        AnsiConsole.MarkupLine($"  [yellow]{Markup.Escape($"{icon} {display}")}[/]");

        var formatted = rawResult is null ? null : ToolRegistry.FormatToolResult(toolName, rawResult);
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

    private static string? GetPluginFunctionResult(object? payload)
    {
        if (payload is PluginFunctionCallPayload typed)
            return FormatPluginFunctionResult(typed.ContentItems, typed.StructuredResult, typed.ErrorMessage);

        if (payload is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            var parts = new List<string>();
            if (je.TryGetProperty("contentItems", out var contentItems)
                && contentItems.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in contentItems.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var type = item.TryGetProperty("type", out var typeEl) && typeEl.ValueKind == JsonValueKind.String
                        ? typeEl.GetString()
                        : "text";
                    if (type == "text")
                    {
                        if (item.TryGetProperty("text", out var textEl)
                            && textEl.ValueKind == JsonValueKind.String
                            && !string.IsNullOrEmpty(textEl.GetString()))
                        {
                            parts.Add(textEl.GetString()!);
                        }
                    }
                    else if (type == "image")
                    {
                        var mediaType = item.TryGetProperty("mediaType", out var mediaTypeEl)
                            && mediaTypeEl.ValueKind == JsonValueKind.String
                                ? mediaTypeEl.GetString()
                                : "image";
                        parts.Add($"[image: {mediaType}]");
                    }
                }
            }

            if (parts.Count > 0)
                return string.Join(Environment.NewLine, parts);

            if (je.TryGetProperty("structuredResult", out var structured)
                && structured.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                return JsonSerializer.Serialize(structured, new JsonSerializerOptions { WriteIndented = true });
            }

            return je.TryGetProperty("errorMessage", out var error)
                && error.ValueKind == JsonValueKind.String
                    ? error.GetString()
                    : null;
        }

        return null;
    }

    private static string? FormatPluginFunctionResult(
        IReadOnlyList<PluginFunctionContentItem>? contentItems,
        System.Text.Json.Nodes.JsonNode? structuredResult,
        string? errorMessage)
    {
        var parts = new List<string>();
        foreach (var item in contentItems ?? [])
        {
            if (item.Type == "text" && !string.IsNullOrEmpty(item.Text))
                parts.Add(item.Text!);
            else if (item.Type == "image")
                parts.Add($"[image: {item.MediaType ?? "image"}]");
        }

        if (parts.Count > 0)
            return string.Join(Environment.NewLine, parts);

        if (structuredResult != null)
            return structuredResult.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        return errorMessage;
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

    private static void PrintAssistantText(string text)
    {
        MarkdownConsoleRenderer.Render(text.Trim());
    }

    /// <summary>
    /// Removes the [Runtime Context] block appended by RuntimeContextBuilder from stored user messages,
    /// so dynamic metadata (timestamps, workspace path, etc.) is not shown in history replay.
    /// </summary>
    private static string StripRuntimeContext(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var idx = text.IndexOf("\n[Runtime Context]", StringComparison.Ordinal);
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
