using System.Text;
using System.Text.Json;
using DotCraft.Memory;
using OpenAI.Chat;
using Spectre.Console;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

namespace DotCraft.Context;

/// <summary>
/// Consolidates thread history into dual-layer long-term memory:
/// - MEMORY.md: updated structured long-term facts
/// - HISTORY.md: appended grep-searchable event paragraph
/// Session Core schedules consolidation after successful turns; context
/// compaction does not drive this workflow.
/// </summary>
public sealed class MemoryConsolidator(ChatClient chatClient, MemoryStore memoryStore, Action<string>? onStatus = null)
{
    private const string SystemPrompt =
        "You are a memory consolidation agent. " +
        "Call the save_memory tool with your consolidation of the conversation.";

    private static readonly ChatTool SaveMemoryTool = ChatTool.CreateFunctionTool(
        "save_memory",
        "Save the memory consolidation result to persistent storage.",
        BinaryData.FromString("""
        {
          "type": "object",
          "properties": {
            "history_entry": {
              "type": "string",
              "description": "A paragraph (2-5 sentences) summarizing key events, decisions, and topics. Start with [YYYY-MM-DD HH:MM]. Include detail useful for grep search."
            },
            "memory_update": {
              "type": "string",
              "description": "Full updated long-term memory as markdown. Include all existing facts plus new ones learned. Return unchanged content if nothing new was learned."
            }
          },
          "required": ["history_entry", "memory_update"]
        }
        """));

    /// <summary>
    /// Consolidate the given thread-history snapshot into MEMORY.md and HISTORY.md.
    /// Runs the LLM consolidation call and writes results to disk.
    /// </summary>
    public async Task ConsolidateAsync(
        IReadOnlyList<AiChatMessage> messagesToArchive,
        CancellationToken cancellationToken = default)
    {
        if (messagesToArchive.Count == 0)
            return;

        var currentMemory = memoryStore.ReadLongTerm();
        var conversationText = FormatMessages(messagesToArchive);

        var prompt =
            $"""
            Process this conversation and call the save_memory tool with your consolidation.

            ## Current Long-term Memory
            {(string.IsNullOrWhiteSpace(currentMemory) ? "(empty)" : currentMemory)}

            ## Conversation to Process
            {conversationText}
            """;

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(prompt)
        };

        var options = new ChatCompletionOptions
        {
            Tools = { SaveMemoryTool }
            // Notice: ToolChoice conflicts with some Reasoning models.
            // , ToolChoice = ChatToolChoice.CreateFunctionChoice("save_memory")
        };

        try
        {
            var response = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
            var completion = response.Value;

            if (completion.FinishReason != ChatFinishReason.ToolCalls)
            {
                onStatus?.Invoke("[grey][[Memory]][/] [yellow]Consolidation: LLM did not call save_memory, skipping.[/]");
                return;
            }

            foreach (var toolCall in completion.ToolCalls)
            {
                if (toolCall.FunctionName != "save_memory")
                    continue;

                using var doc = JsonDocument.Parse(toolCall.FunctionArguments);
                var root = doc.RootElement;

                if (root.TryGetProperty("history_entry", out var historyEl))
                {
                    var entry = historyEl.GetString();
                    if (!string.IsNullOrWhiteSpace(entry))
                        memoryStore.AppendHistory(entry);
                }

                if (root.TryGetProperty("memory_update", out var memoryEl))
                {
                    var updated = memoryEl.GetString();
                    if (!string.IsNullOrWhiteSpace(updated) && updated != currentMemory)
                        memoryStore.WriteLongTerm(updated);
                }

                onStatus?.Invoke("[grey][[Memory]][/] [green]Consolidation complete.[/]");
                break;
            }
        }
        catch (Exception ex)
        {
            onStatus?.Invoke($"[grey][[Memory]][/] [red]Consolidation failed: {Markup.Escape(ex.Message)}[/]");
            throw;
        }
    }

    /// <summary>
    /// Fire-and-forget consolidation that does not block the caller.
    /// </summary>
    public void ConsolidateInBackground(IReadOnlyList<AiChatMessage> messagesToArchive)
    {
        if (messagesToArchive.Count == 0)
            return;

        // Snapshot the list so the caller can safely mutate the source after this call.
        var snapshot = messagesToArchive.ToList();
        _ = Task.Run(async () =>
        {
            try
            {
                await ConsolidateAsync(snapshot);
            }
            catch (Exception ex)
            {
                onStatus?.Invoke($"[grey][[Memory]][/] [red]Background consolidation error: {Markup.Escape(ex.Message)}[/]");
            }
        });
    }

    private static string FormatMessages(IReadOnlyList<AiChatMessage> messages)
    {
        var sb = new StringBuilder();
        var now = DateTime.Now;

        foreach (var msg in messages)
        {
            var role = msg.Role == AiChatRole.User ? "USER"
                : msg.Role == AiChatRole.Assistant ? "ASSISTANT"
                : msg.Role.ToString().ToUpperInvariant();
            
            if (string.IsNullOrWhiteSpace(msg.Text))
                continue;

            sb.AppendLine($"[{now:yyyy-MM-dd}] {role}: {msg.Text.Trim()}");
        }

        return sb.ToString();
    }
}
