using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

/// <summary>
/// Result of a partial compaction run.
/// </summary>
public sealed record PartialCompactResult(
    IReadOnlyList<ChatMessage> SummarizedPrefix,
    IReadOnlyList<ChatMessage> PreservedTail,
    string FormattedSummary,
    string RawSummary,
    int PrefixEstimatedTokens,
    int TailEstimatedTokens);

/// <summary>
/// Summarizes the older portion of a conversation while preserving a tail of
/// recent API-round groups verbatim. Port of openclaude's
/// <c>sessionMemoryCompact.ts</c> / <c>calculateMessagesToKeepIndex</c>.
/// </summary>
public sealed class PartialCompactor
{
    private readonly IChatClient _chatClient;
    private readonly CompactionConfig _config;

    public PartialCompactor(IChatClient chatClient, CompactionConfig config)
    {
        _chatClient = chatClient;
        _config = config;
    }

    /// <summary>
    /// Picks the split point in <paramref name="messages"/> that preserves
    /// <paramref name="config"/>.KeepRecent*... as the tail, returning the
    /// index of the first preserved message. Visible for tests.
    /// </summary>
    public static int CalculateSplitIndex(
        IReadOnlyList<ChatMessage> messages,
        CompactionConfig config)
    {
        if (messages.Count == 0)
            return 0;

        var groups = MessageGrouper.GroupByApiRound(messages);
        if (groups.Count == 0)
            return 0;

        long tailTokens = 0;
        int tailGroups = 0;
        int splitGroupIndex = groups.Count;

        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var group = groups[i];
            var nextTokens = tailTokens + group.EstimatedTokens;
            if (nextTokens > config.KeepRecentMaxTokens && tailGroups > 0)
                break;

            tailTokens = nextTokens;
            tailGroups++;
            splitGroupIndex = i;

            if (tailTokens >= config.KeepRecentMinTokens && tailGroups >= config.KeepRecentMinGroups)
                break;
        }

        // All groups may be tail; in that case there's nothing to summarize.
        if (splitGroupIndex == 0)
            return 0;

        var splitMessageIndex = 0;
        for (var i = 0; i < splitGroupIndex; i++)
            splitMessageIndex += groups[i].Messages.Count;

        return splitMessageIndex;
    }

    /// <summary>
    /// Runs the partial summary. Returns <c>null</c> when the model rejected
    /// the request, nothing meaningful was summarized, or there is no prefix
    /// to summarize. The caller is responsible for replacing the prefix in the
    /// session's chat history.
    /// </summary>
    public async Task<PartialCompactResult?> CompactAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0)
            return null;

        var splitIndex = CalculateSplitIndex(messages, _config);
        if (splitIndex <= 0)
            return null;

        var prefix = messages.Take(splitIndex).ToList();
        var tail = messages.Skip(splitIndex).ToList();

        if (prefix.Count == 0)
            return null;

        var paired = MessageGrouper.EnsurePairing(prefix);
        if (paired.Count == 0)
            return null;

        var prefixTokens = MessageTokenEstimator.Estimate(prefix);
        var tailTokens = MessageTokenEstimator.Estimate(tail);

        var summaryPrompt = CompactionPrompts.GetPartialCompactPrompt();
        var summaryMessages = new List<ChatMessage>(paired.Count + 1)
        {
            new(ChatRole.System, summaryPrompt)
        };
        summaryMessages.AddRange(paired);

        ChatResponse? response;
        try
        {
            response = await _chatClient.GetResponseAsync(
                summaryMessages,
                new ChatOptions { Tools = null },
                cancellationToken);
        }
        catch
        {
            return null;
        }

        var rawSummary = response?.Text;
        if (string.IsNullOrWhiteSpace(rawSummary))
            return null;

        var formatted = CompactionPrompts.GetCompactUserSummaryMessage(
            rawSummary,
            transcriptPath: null,
            recentMessagesPreserved: tail.Count > 0);

        return new PartialCompactResult(
            SummarizedPrefix: prefix,
            PreservedTail: tail,
            FormattedSummary: formatted,
            RawSummary: rawSummary,
            PrefixEstimatedTokens: prefixTokens,
            TailEstimatedTokens: tailTokens);
    }
}
