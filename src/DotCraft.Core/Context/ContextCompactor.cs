using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.Context;

public sealed class ContextCompactor(OpenAI.Chat.ChatClient chatClient, MemoryConsolidator? memoryConsolidator = null)
{
    private const string CompactionPrompt =
        """
        Summarize the conversation so far into a concise prompt that can be used to continue our conversation.
        Focus on:
        1. What tasks were discussed and their current status
        2. Key decisions made and important context
        3. Files and paths that were referenced or modified
        4. Any ongoing work or next steps planned
        5. Important constraints or requirements mentioned

        Be detailed enough that the conversation can continue seamlessly, but concise enough to save context space.
        Do NOT include tool call details or intermediate outputs — only the essential information.
        Write in the same language the user has been using.
        """;

    public async Task<bool> TryCompactAsync(AgentSession session, CancellationToken cancellationToken = default)
    {
        var chatHistory = session.GetService<ChatHistoryProvider>();
        if (chatHistory is not InMemoryChatHistoryProvider memoryProvider)
            return false;

        if (memoryProvider.Count < 3)
            return false;

        var messagesToSummarize = new List<ChatMessage>();
        int retainCount = Math.Min(2, memoryProvider.Count);
        int summarizeEnd = memoryProvider.Count - retainCount;

        for (int i = 0; i < summarizeEnd; i++)
        {
            messagesToSummarize.Add(memoryProvider[i]);
        }

        if (messagesToSummarize.Count == 0)
            return false;

        // Persist knowledge from about-to-be-discarded messages before compacting.
        memoryConsolidator?.ConsolidateInBackground(messagesToSummarize);

        var summary = await GenerateSummaryAsync(messagesToSummarize, cancellationToken);
        if (string.IsNullOrWhiteSpace(summary))
            return false;

        var retainedMessages = new List<ChatMessage>();
        for (int i = summarizeEnd; i < memoryProvider.Count; i++)
        {
            retainedMessages.Add(memoryProvider[i]);
        }

        while (memoryProvider.Count > 0)
            memoryProvider.RemoveAt(memoryProvider.Count - 1);

        memoryProvider.Add(new ChatMessage(ChatRole.Assistant,
            $"[Context Summary]\n{summary}"));

        foreach (var msg in retainedMessages)
            memoryProvider.Add(msg);

        return true;
    }

    private async Task<string?> GenerateSummaryAsync(
        List<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var summaryMessages = new List<ChatMessage>
        {
            new(ChatRole.System, CompactionPrompt)
        };

        foreach (var msg in messages)
        {
            if (msg.Role == ChatRole.Tool)
                continue;

            var textParts = new List<AIContent>();
            foreach (var content in msg.Contents)
            {
                switch (content)
                {
                    case TextContent tc:
                        textParts.Add(tc);
                        break;
                    case FunctionCallContent:
                    case FunctionResultContent:
                        break;
                    default:
                        textParts.Add(content);
                        break;
                }
            }

            if (textParts.Count > 0)
                summaryMessages.Add(new ChatMessage(msg.Role, textParts));
        }

        if (summaryMessages.Count <= 1)
            return null;

        try
        {
            var response = await chatClient.AsIChatClient().GetResponseAsync(
                summaryMessages,
                cancellationToken: cancellationToken);

            return response.Text;
        }
        catch
        {
            return null;
        }
    }
}
