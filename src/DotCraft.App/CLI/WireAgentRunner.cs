using System.Text;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools;
using Spectre.Console;

namespace DotCraft.CLI;

/// <summary>
/// Wire-protocol equivalent of <see cref="AgentRunner"/> used for heartbeat and cron-triggered
/// agent runs when the CLI is connected to an AppServer subprocess or remote WebSocket.
///
/// Mirrors <see cref="AgentRunner.RunAsync"/> logic but communicates entirely over
/// <see cref="AppServerWireClient"/> rather than calling <see cref="DotCraft.Protocol.ISessionService"/> directly.
/// </summary>
public sealed class WireAgentRunner(AppServerWireClient wire, string workspacePath)
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Runs an agent session for the given prompt and session key over the wire protocol.
    /// Mirrors the <see cref="AgentRunner.RunAsync"/> output format (AnsiConsole log lines).
    /// </summary>
    public async Task<string?> RunAsync(string prompt, string sessionKey, CancellationToken ct = default)
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

        AnsiConsole.MarkupLine($"[grey][[{tag}]][/] Running: [dim]{Markup.Escape(prompt.Length > 120 ? prompt[..120] + "..." : prompt)}[/]");

        var channelName = sessionKey.StartsWith("heartbeat") ? "heartbeat"
            : sessionKey.StartsWith("cron:") ? "cron"
            : "agent";

        var identity = new
        {
            channelName,
            userId = sessionKey,
            channelContext = (string?)null,
            workspacePath
        };

        // Find existing thread for this session key
        string? threadId = null;
        try
        {
            var listResult = await wire.SendRequestAsync(AppServerMethods.ThreadList, new { identity }, ct: ct);
            var dataEl = listResult.RootElement.GetProperty("result").GetProperty("data");
            var summaries = JsonSerializer.Deserialize<List<ThreadSummaryWire>>(dataEl.GetRawText(), ReadOptions) ?? [];
            var existing = summaries.FirstOrDefault(s => s.Status != "archived");
            if (existing != null)
            {
                await wire.SendRequestAsync(AppServerMethods.ThreadResume, new { threadId = existing.Id }, ct: ct);
                threadId = existing.Id;
            }
        }
        catch
        {
            // Treat list/resume errors as "no existing thread"
        }

        if (threadId == null)
        {
            try
            {
                var startResult = await wire.SendRequestAsync(AppServerMethods.ThreadStart, new
                {
                    identity,
                    historyMode = "server"
                }, ct: ct);
                threadId = startResult.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString();
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [red]Failed to create thread: {Markup.Escape(ex.Message)}[/]");
                return null;
            }
        }

        if (string.IsNullOrEmpty(threadId))
        {
            AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [red]No thread ID returned from AppServer.[/]");
            return null;
        }

        // Start the turn
        try
        {
            await wire.SendRequestAsync(AppServerMethods.TurnStart, new
            {
                threadId,
                input = new[] { new { type = "text", text = prompt } }
            }, timeout: TimeSpan.FromSeconds(30), ct: ct);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [red]Failed to start turn: {Markup.Escape(ex.Message)}[/]");
            return null;
        }

        // Consume the notification stream
        var sb = new StringBuilder();

        await foreach (var doc in wire.ReadTurnNotificationsAsync(ct: ct))
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("method", out var methodEl)) continue;
            var method = methodEl.GetString() ?? string.Empty;
            var hasParams = root.TryGetProperty("params", out var @params);

            switch (method)
            {
                case AppServerMethods.ItemAgentMessageDelta:
                {
                    if (!hasParams) break;
                    var delta = @params.TryGetProperty("delta", out var d) ? d.GetString() : null;
                    if (!string.IsNullOrEmpty(delta))
                        sb.Append(delta);
                    break;
                }

                case AppServerMethods.ItemStarted:
                {
                    if (!hasParams || !@params.TryGetProperty("item", out var item)) break;
                    var type = item.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (type == "toolCall")
                    {
                        var payload = item.TryGetProperty("payload", out var p) ? p : default;
                        var toolName = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("toolName", out var tn)
                            ? tn.GetString() : null;
                        string? formattedDisplay = null;
                        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("arguments", out var args)
                            && args.ValueKind == JsonValueKind.Object)
                        {
                            var argsNode = System.Text.Json.Nodes.JsonNode.Parse(args.GetRawText()) as System.Text.Json.Nodes.JsonObject;
                            formattedDisplay = ToolRegistry.FormatToolCall(toolName ?? string.Empty, argsNode);
                        }
                        var icon = ToolRegistry.GetToolIcon(toolName ?? string.Empty);
                        var display = formattedDisplay ?? toolName ?? string.Empty;
                        AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [yellow]{Markup.Escape($"{icon} {display}")}[/]");
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
                        var result = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("result", out var r)
                            ? r.GetString() : null;
                        if (!string.IsNullOrEmpty(result))
                        {
                            var preview = result.Length > 200 ? result[..200] + "..." : result;
                            var normalized = preview.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
                            AnsiConsole.MarkupLine($"[grey][[{tag}]][/]   [grey]{Markup.Escape(normalized)}[/]");
                        }
                    }
                    break;
                }

                case AppServerMethods.TurnCompleted:
                {
                    if (!hasParams) break;
                    var turn = @params.TryGetProperty("turn", out var t) ? t : default;
                    if (turn.ValueKind == JsonValueKind.Object && turn.TryGetProperty("tokenUsage", out var usage)
                        && usage.ValueKind == JsonValueKind.Object)
                    {
                        var input = usage.TryGetProperty("inputTokens", out var i) ? i.GetInt64() : 0;
                        var output = usage.TryGetProperty("outputTokens", out var o) ? o.GetInt64() : 0;
                        if (input > 0 || output > 0)
                            AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [blue]↑ {input} input[/] [green]↓ {output} output[/]");
                    }
                    break;
                }

                case AppServerMethods.TurnFailed:
                {
                    if (!hasParams) break;
                    var errMsg = @params.TryGetProperty("error", out var e) ? e.GetString() : "Turn failed";
                    AnsiConsole.MarkupLine($"[grey][[{tag}]][/] [red]Turn failed: {Markup.Escape(errMsg ?? "unknown error")}[/]");
                    return null;
                }
            }
        }

        var response = sb.Length > 0 ? sb.ToString() : null;
        if (response != null)
            AnsiConsole.MarkupLine($"[grey][[{tag}]][/] Response: [dim]{Markup.Escape(response.Length > 200 ? response[..200] + "..." : response)}[/]");

        return response;
    }

    // Minimal DTO for deserializing thread list entries
    private sealed class ThreadSummaryWire
    {
        public string Id { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }
}
