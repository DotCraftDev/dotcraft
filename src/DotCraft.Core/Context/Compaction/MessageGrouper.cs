using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

/// <summary>
/// A contiguous span of messages that forms a single API round (one user turn +
/// the assistant's response, including any tool-call / tool-result pairs).
/// </summary>
public sealed record MessageGroup(IReadOnlyList<ChatMessage> Messages, int EstimatedTokens);

/// <summary>
/// Groups <see cref="ChatMessage"/> sequences into API-round groups so that
/// compaction never splits a <see cref="FunctionCallContent"/> from its
/// matching <see cref="FunctionResultContent"/>. Mirrors openclaude's
/// <c>groupMessagesByApiRound</c>.
/// </summary>
public static class MessageGrouper
{
    /// <summary>
    /// Splits <paramref name="messages"/> into API-round groups.
    /// Boundary rule: a new group begins at a user message that does not carry
    /// a <see cref="FunctionResultContent"/> (i.e. a real user turn, not the
    /// tool-result envelope some providers surface as role=User). Tool-result
    /// user messages and the subsequent assistant reply stay in the previous
    /// group so call/result pairs are preserved.
    /// </summary>
    public static IReadOnlyList<MessageGroup> GroupByApiRound(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0)
            return Array.Empty<MessageGroup>();

        var groups = new List<MessageGroup>();
        var current = new List<ChatMessage>();
        int currentTokens = 0;

        foreach (var msg in messages)
        {
            var startsNewGroup = IsUserTurnBoundary(msg) && current.Count > 0;
            if (startsNewGroup)
            {
                groups.Add(new MessageGroup(current, currentTokens));
                current = new List<ChatMessage>();
                currentTokens = 0;
            }

            current.Add(msg);
            currentTokens += MessageTokenEstimator.EstimateMessage(msg);
        }

        if (current.Count > 0)
            groups.Add(new MessageGroup(current, currentTokens));

        return groups;
    }

    /// <summary>
    /// Filters <paramref name="messages"/> so that any dangling
    /// <see cref="FunctionCallContent"/> — a tool call whose matching
    /// <see cref="FunctionResultContent"/> was dropped — is removed before the
    /// slice is handed to the summarizer. This mirrors openclaude's
    /// <c>ensureToolUseResultPairing</c> so the summary call never sees
    /// half a tool-use pair.
    /// </summary>
    public static List<ChatMessage> EnsurePairing(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0)
            return new List<ChatMessage>();

        var resultCallIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var msg in messages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionResultContent fr && !string.IsNullOrEmpty(fr.CallId))
                    resultCallIds.Add(fr.CallId);
            }
        }

        var result = new List<ChatMessage>(messages.Count);
        foreach (var msg in messages)
        {
            var keepAll = true;
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent fc && !string.IsNullOrEmpty(fc.CallId)
                    && !resultCallIds.Contains(fc.CallId))
                {
                    keepAll = false;
                    break;
                }
            }

            if (keepAll)
            {
                result.Add(msg);
                continue;
            }

            var filtered = new List<AIContent>(msg.Contents.Count);
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent fc && !string.IsNullOrEmpty(fc.CallId)
                    && !resultCallIds.Contains(fc.CallId))
                    continue;
                filtered.Add(content);
            }

            if (filtered.Count > 0)
            {
                var rebuilt = new ChatMessage(msg.Role, filtered)
                {
                    AuthorName = msg.AuthorName,
                    MessageId = msg.MessageId,
                };
                result.Add(rebuilt);
            }
        }

        return result;
    }

    private static bool IsUserTurnBoundary(ChatMessage msg)
    {
        if (msg.Role != ChatRole.User)
            return false;

        foreach (var content in msg.Contents)
        {
            if (content is FunctionResultContent)
                return false;
        }

        return true;
    }
}
