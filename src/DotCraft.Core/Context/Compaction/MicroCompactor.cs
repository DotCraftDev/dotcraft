using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

/// <summary>
/// Result of a microcompact pass.
/// </summary>
public sealed record MicroCompactResult(
    IReadOnlyList<ChatMessage> Messages,
    int ClearedCount,
    long EstimatedTokensSaved,
    MicroCompactTrigger Trigger);

/// <summary>
/// Which trigger fired the microcompact pass.
/// </summary>
public enum MicroCompactTrigger
{
    None,
    CountBased,
    TimeBased,
}

/// <summary>
/// Pre-summary pass that replaces the payload of stale tool results with a
/// cleared-marker so the prefix shrinks before the full partial compaction
/// runs. Ports the relevant half of openclaude's <c>microCompact.ts</c>
/// (count- and time-based triggers; the provider cache-editing path is
/// intentionally not ported — see <c>plan.md</c>).
/// </summary>
public sealed class MicroCompactor
{
    private readonly CompactionConfig _config;

    public MicroCompactor(CompactionConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Runs the microcompact pass if its configured triggers fire. Returns
    /// <paramref name="messages"/> unchanged (with <c>Trigger=None</c>) when
    /// nothing needs to be cleared or microcompact is disabled.
    /// </summary>
    /// <param name="messages">Current conversation history.</param>
    /// <param name="lastAssistantTimestampUtc">Timestamp of the most recent
    /// assistant reply. When null or in the future, time-based trigger is
    /// skipped.</param>
    public MicroCompactResult Run(
        IReadOnlyList<ChatMessage> messages,
        DateTimeOffset? lastAssistantTimestampUtc = null)
    {
        if (!_config.MicrocompactEnabled || messages.Count == 0)
            return new MicroCompactResult(messages, 0, 0, MicroCompactTrigger.None);

        var trigger = EvaluateTrigger(messages, lastAssistantTimestampUtc);
        if (trigger == MicroCompactTrigger.None)
            return new MicroCompactResult(messages, 0, 0, MicroCompactTrigger.None);

        var compactableCallIds = CollectCompactableCallIds(messages);
        if (compactableCallIds.Count == 0)
            return new MicroCompactResult(messages, 0, 0, MicroCompactTrigger.None);

        var keepRecent = Math.Max(1, _config.MicrocompactKeepRecent);
        var keepFrom = Math.Max(0, compactableCallIds.Count - keepRecent);
        var clearIds = new HashSet<string>(compactableCallIds.Take(keepFrom), StringComparer.Ordinal);

        if (clearIds.Count == 0)
            return new MicroCompactResult(messages, 0, 0, MicroCompactTrigger.None);

        var result = new List<ChatMessage>(messages.Count);
        var cleared = 0;
        long tokensSaved = 0;

        foreach (var msg in messages)
        {
            var rewritten = false;
            List<AIContent>? newContents = null;

            for (var i = 0; i < msg.Contents.Count; i++)
            {
                var content = msg.Contents[i];
                if (content is FunctionResultContent fr
                    && !string.IsNullOrEmpty(fr.CallId)
                    && clearIds.Contains(fr.CallId)
                    && !IsAlreadyCleared(fr))
                {
                    newContents ??= new List<AIContent>(msg.Contents);
                    tokensSaved += MessageTokenEstimator.EstimateContent(fr);
                    newContents[i] = new FunctionResultContent(fr.CallId, CompactableToolNames.ClearedResultMarker);
                    cleared++;
                    rewritten = true;
                }
            }

            if (!rewritten)
            {
                result.Add(msg);
                continue;
            }

            result.Add(new ChatMessage(msg.Role, newContents!)
            {
                AuthorName = msg.AuthorName,
                MessageId = msg.MessageId,
            });
        }

        if (cleared == 0)
            return new MicroCompactResult(messages, 0, 0, MicroCompactTrigger.None);

        return new MicroCompactResult(result, cleared, tokensSaved, trigger);
    }

    private MicroCompactTrigger EvaluateTrigger(
        IReadOnlyList<ChatMessage> messages,
        DateTimeOffset? lastAssistantTimestampUtc)
    {
        var compactableCount = CountCompactableTools(messages);
        if (compactableCount >= _config.MicrocompactTriggerCount)
            return MicroCompactTrigger.CountBased;

        if (_config.MicrocompactGapMinutes > 0 && lastAssistantTimestampUtc is { } lastTime)
        {
            var gap = DateTimeOffset.UtcNow - lastTime;
            if (gap.TotalMinutes >= _config.MicrocompactGapMinutes && compactableCount > _config.MicrocompactKeepRecent)
                return MicroCompactTrigger.TimeBased;
        }

        return MicroCompactTrigger.None;
    }

    private static int CountCompactableTools(IReadOnlyList<ChatMessage> messages)
    {
        var count = 0;
        foreach (var msg in messages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent fc && CompactableToolNames.IsCompactable(fc.Name))
                    count++;
            }
        }
        return count;
    }

    private static List<string> CollectCompactableCallIds(IReadOnlyList<ChatMessage> messages)
    {
        var callIdToIsCompactable = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var msg in messages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionCallContent fc && !string.IsNullOrEmpty(fc.CallId))
                    callIdToIsCompactable[fc.CallId] = CompactableToolNames.IsCompactable(fc.Name);
            }
        }

        var ordered = new List<string>();
        foreach (var msg in messages)
        {
            foreach (var content in msg.Contents)
            {
                if (content is FunctionResultContent fr
                    && !string.IsNullOrEmpty(fr.CallId)
                    && callIdToIsCompactable.TryGetValue(fr.CallId, out var isCompactable)
                    && isCompactable)
                {
                    ordered.Add(fr.CallId);
                }
            }
        }

        return ordered;
    }

    private static bool IsAlreadyCleared(FunctionResultContent fr)
    {
        return fr.Result is string s && string.Equals(s, CompactableToolNames.ClearedResultMarker, StringComparison.Ordinal);
    }
}
