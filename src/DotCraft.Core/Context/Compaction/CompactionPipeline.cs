using System.Diagnostics.CodeAnalysis;
using DotCraft.Memory;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.Context.Compaction;

/// <summary>
/// Outcome of a single compaction attempt.
/// </summary>
public enum CompactionOutcome
{
    /// <summary>No action was needed (below thresholds or nothing to summarize).</summary>
    Skipped,
    /// <summary>The microcompact pass cleared enough content to stay below the auto threshold.</summary>
    Micro,
    /// <summary>A partial summary was produced and the prefix was replaced in-place.</summary>
    Partial,
    /// <summary>The attempt failed (LLM error, cancellation, circuit breaker tripped).</summary>
    Failed,
}

/// <summary>
/// Rich status record returned by pipeline entry points for logging / eventing.
/// </summary>
public sealed record CompactionStatus(
    CompactionOutcome Outcome,
    int EstimatedTokensBefore,
    int EstimatedTokensAfter,
    CompactionThreshold ThresholdBefore,
    CompactionThreshold ThresholdAfter,
    int ClearedToolResults = 0,
    string? FailureReason = null)
{
    public bool Success => Outcome is CompactionOutcome.Micro or CompactionOutcome.Partial;
}

/// <summary>
/// Threshold evaluation for a given token count. Mirrors openclaude's
/// <c>calculateTokenWarningState</c>.
/// </summary>
public sealed record CompactionThreshold(
    long Tokens,
    int AutoThreshold,
    int WarningThreshold,
    int ErrorThreshold,
    int BlockingLimit,
    double PercentLeft)
{
    public bool AboveWarning => Tokens >= WarningThreshold;
    public bool AboveError => Tokens >= ErrorThreshold;
    public bool AboveAuto => Tokens >= AutoThreshold;
    public bool AboveBlocking => Tokens >= BlockingLimit;
}

/// <summary>
/// Orchestrator for the layered compaction pipeline. Owns the microcompactor,
/// partial compactor, failure tracker, and the token-based threshold
/// evaluation. Emits <see cref="CompactionStatus"/> values that
/// <see cref="Protocol.SessionService"/> translates into lifecycle events.
/// </summary>
public sealed class CompactionPipeline
{
    private readonly CompactionConfig _config;
    private readonly MicroCompactor _micro;
    private readonly PartialCompactor _partial;
    private readonly MemoryConsolidator? _memoryConsolidator;
    private readonly CompactionFailureTracker _failures;

    public CompactionPipeline(
        CompactionConfig config,
        IChatClient summaryChatClient,
        MemoryConsolidator? memoryConsolidator = null)
    {
        _config = config;
        _micro = new MicroCompactor(config);
        _partial = new PartialCompactor(summaryChatClient, config);
        _memoryConsolidator = memoryConsolidator;
        _failures = new CompactionFailureTracker(config.MaxConsecutiveFailures);
    }

    /// <summary>
    /// Exposed for unit tests that need to poke the breaker directly.
    /// </summary>
    internal CompactionFailureTracker Failures => _failures;

    /// <summary>
    /// Computes the <see cref="CompactionThreshold"/> for a given token count.
    /// </summary>
    public CompactionThreshold EvaluateThreshold(long tokens)
    {
        var effective = _config.EffectiveContextWindow();
        var autoThreshold = _config.AutoCompactThreshold();
        var warning = _config.WarningThreshold();
        var error = _config.ErrorThreshold();
        var blocking = _config.BlockingLimit();

        var percentLeft = effective <= 0
            ? 0.0
            : Math.Max(0.0, 1.0 - (tokens / (double)effective));

        return new CompactionThreshold(
            tokens,
            autoThreshold,
            warning,
            error,
            blocking,
            percentLeft);
    }

    /// <summary>
    /// Evaluates whether auto-compact should run for the given session.
    /// Primary token source is <paramref name="inputTokenHint"/> (typically
    /// <c>tokenTracker.LastInputTokens</c>); when that is zero or negative,
    /// falls back to <see cref="MessageTokenEstimator.Estimate"/>.
    /// </summary>
    public async Task<CompactionStatus> TryAutoCompactAsync(
        AgentSession session,
        string threadId,
        long inputTokenHint,
        DateTimeOffset? lastAssistantTimestampUtc,
        CancellationToken cancellationToken)
    {
        if (!_config.AutoCompactEnabled)
            return new CompactionStatus(
                CompactionOutcome.Skipped,
                0, 0,
                EvaluateThreshold(0), EvaluateThreshold(0));

        if (!TryGetProvider(session, out var provider))
            return new CompactionStatus(
                CompactionOutcome.Skipped,
                0, 0,
                EvaluateThreshold(0), EvaluateThreshold(0));

        if (_failures.IsTripped(threadId))
            return new CompactionStatus(
                CompactionOutcome.Skipped,
                0, 0,
                EvaluateThreshold(0), EvaluateThreshold(0),
                FailureReason: "circuit_breaker_tripped");

        var history = SnapshotHistory(provider);
        var before = inputTokenHint > 0
            ? (int)Math.Min(int.MaxValue, inputTokenHint)
            : MessageTokenEstimator.Estimate(history);
        var beforeThreshold = EvaluateThreshold(before);

        if (!beforeThreshold.AboveAuto)
            return new CompactionStatus(
                CompactionOutcome.Skipped,
                before, before, beforeThreshold, beforeThreshold);

        return await RunCompactionAsync(
            provider,
            history,
            before,
            beforeThreshold,
            threadId,
            lastAssistantTimestampUtc,
            cancellationToken);
    }

    /// <summary>
    /// Reactive compaction: called from the mid-turn error path when the
    /// model rejected the request for being too long. Skips threshold
    /// evaluation and always runs micro+partial.
    /// </summary>
    public async Task<CompactionStatus> TryReactiveCompactAsync(
        AgentSession session,
        string threadId,
        DateTimeOffset? lastAssistantTimestampUtc,
        CancellationToken cancellationToken)
    {
        if (!_config.ReactiveCompactEnabled)
            return new CompactionStatus(
                CompactionOutcome.Skipped,
                0, 0,
                EvaluateThreshold(0), EvaluateThreshold(0),
                FailureReason: "reactive_disabled");

        if (!TryGetProvider(session, out var provider))
            return new CompactionStatus(
                CompactionOutcome.Skipped,
                0, 0,
                EvaluateThreshold(0), EvaluateThreshold(0),
                FailureReason: "no_history_provider");

        if (_failures.IsTripped(threadId))
            return new CompactionStatus(
                CompactionOutcome.Failed,
                0, 0,
                EvaluateThreshold(0), EvaluateThreshold(0),
                FailureReason: "circuit_breaker_tripped");

        var history = SnapshotHistory(provider);
        var before = MessageTokenEstimator.Estimate(history);
        var beforeThreshold = EvaluateThreshold(before);

        return await RunCompactionAsync(
            provider,
            history,
            before,
            beforeThreshold,
            threadId,
            lastAssistantTimestampUtc,
            cancellationToken,
            forcePartial: true);
    }

    /// <summary>
    /// Manual compaction (slash command, dashboard button). Skips the auto
    /// threshold check but still respects the circuit breaker and blocking
    /// limit.
    /// </summary>
    public async Task<CompactionStatus> TryManualCompactAsync(
        AgentSession session,
        string threadId,
        DateTimeOffset? lastAssistantTimestampUtc,
        CancellationToken cancellationToken)
    {
        if (!TryGetProvider(session, out var provider))
            return new CompactionStatus(
                CompactionOutcome.Skipped,
                0, 0,
                EvaluateThreshold(0), EvaluateThreshold(0),
                FailureReason: "no_history_provider");

        if (_failures.IsTripped(threadId))
            return new CompactionStatus(
                CompactionOutcome.Failed,
                0, 0,
                EvaluateThreshold(0), EvaluateThreshold(0),
                FailureReason: "circuit_breaker_tripped");

        var history = SnapshotHistory(provider);
        var before = MessageTokenEstimator.Estimate(history);
        var beforeThreshold = EvaluateThreshold(before);

        return await RunCompactionAsync(
            provider,
            history,
            before,
            beforeThreshold,
            threadId,
            lastAssistantTimestampUtc,
            cancellationToken,
            forcePartial: true);
    }

    /// <summary>
    /// Drops any per-thread state (called on thread deletion / clear).
    /// </summary>
    public void Forget(string threadId) => _failures.Forget(threadId);

    private async Task<CompactionStatus> RunCompactionAsync(
        InMemoryChatHistoryProvider provider,
        IReadOnlyList<ChatMessage> history,
        int before,
        CompactionThreshold beforeThreshold,
        string threadId,
        DateTimeOffset? lastAssistantTimestampUtc,
        CancellationToken cancellationToken,
        bool forcePartial = false)
    {
        var microResult = _micro.Run(history, lastAssistantTimestampUtc);
        var afterMicroHistory = microResult.Messages;
        var afterMicroTokens = MessageTokenEstimator.Estimate(afterMicroHistory);
        var afterMicroThreshold = EvaluateThreshold(afterMicroTokens);

        if (microResult.Trigger != MicroCompactTrigger.None && !forcePartial)
        {
            ApplyHistoryReplacement(provider, afterMicroHistory);
            if (!afterMicroThreshold.AboveAuto)
            {
                _failures.RecordSuccess(threadId);
                return new CompactionStatus(
                    CompactionOutcome.Micro,
                    before,
                    afterMicroTokens,
                    beforeThreshold,
                    afterMicroThreshold,
                    microResult.ClearedCount);
            }
        }

        var historyForPartial = microResult.Trigger != MicroCompactTrigger.None
            ? afterMicroHistory
            : history;

        PartialCompactResult? partial;
        try
        {
            partial = await _partial.CompactAsync(historyForPartial, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _failures.RecordFailure(threadId);
            return new CompactionStatus(
                CompactionOutcome.Failed,
                before,
                MessageTokenEstimator.Estimate(historyForPartial),
                beforeThreshold,
                EvaluateThreshold(MessageTokenEstimator.Estimate(historyForPartial)),
                microResult.ClearedCount,
                FailureReason: ex.Message);
        }

        if (partial is null)
        {
            _failures.RecordFailure(threadId);
            return new CompactionStatus(
                CompactionOutcome.Failed,
                before,
                MessageTokenEstimator.Estimate(historyForPartial),
                beforeThreshold,
                EvaluateThreshold(MessageTokenEstimator.Estimate(historyForPartial)),
                microResult.ClearedCount,
                FailureReason: "summary_unavailable");
        }

        var summaryMessage = new ChatMessage(ChatRole.Assistant, partial.FormattedSummary);
        var newHistory = new List<ChatMessage>(1 + partial.PreservedTail.Count) { summaryMessage };
        newHistory.AddRange(partial.PreservedTail);
        ApplyHistoryReplacement(provider, newHistory);

        if (_memoryConsolidator != null && ShouldConsolidate(partial))
        {
            _memoryConsolidator.ConsolidateInBackground(partial.SummarizedPrefix);
        }

        var afterTokens = MessageTokenEstimator.Estimate(newHistory);
        var afterThreshold = EvaluateThreshold(afterTokens);
        _failures.RecordSuccess(threadId);

        return new CompactionStatus(
            CompactionOutcome.Partial,
            before,
            afterTokens,
            beforeThreshold,
            afterThreshold,
            microResult.ClearedCount);
    }

    private bool ShouldConsolidate(PartialCompactResult partial)
    {
        if (_memoryConsolidator is null)
            return false;

        if (_config.MemoryConsolidationPrefixTokens <= 0)
            return true;

        return partial.PrefixEstimatedTokens >= _config.MemoryConsolidationPrefixTokens
            || partial.SummarizedPrefix.Count >= 10;
    }

    private static IReadOnlyList<ChatMessage> SnapshotHistory(InMemoryChatHistoryProvider provider)
    {
        var snapshot = new List<ChatMessage>(provider.Count);
        for (var i = 0; i < provider.Count; i++)
            snapshot.Add(provider[i]);
        return snapshot;
    }

    private static void ApplyHistoryReplacement(
        InMemoryChatHistoryProvider provider,
        IReadOnlyList<ChatMessage> newHistory)
    {
        while (provider.Count > 0)
            provider.RemoveAt(provider.Count - 1);

        foreach (var msg in newHistory)
            provider.Add(msg);
    }

    private static bool TryGetProvider(
        AgentSession session,
        [NotNullWhen(true)] out InMemoryChatHistoryProvider? provider)
    {
        var chatHistory = session.GetService<ChatHistoryProvider>();
        provider = chatHistory as InMemoryChatHistoryProvider;
        return provider is not null;
    }
}
