namespace DotCraft.Context;

public sealed class TokenTracker
{
    private static readonly AsyncLocal<TokenTracker?> _current = new();

    /// <summary>
    /// Ambient token tracker for the current async flow.
    /// Set by terminal runners / AgentRunner before the agent runs;
    /// read in SubAgentManager to aggregate SubAgent token costs.
    /// </summary>
    public static TokenTracker? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    private long _lastInputTokens;
    private long _totalInputTokens;
    private long _totalOutputTokens;
    private long _subAgentInputTokens;
    private long _subAgentOutputTokens;

    /// <summary>
    /// Input tokens from the most recent LLM call (for context compaction threshold).
    /// </summary>
    public long LastInputTokens => Interlocked.Read(ref _lastInputTokens);

    /// <summary>
    /// Cumulative input tokens across all LLM calls in this session turn.
    /// </summary>
    public long TotalInputTokens => Interlocked.Read(ref _totalInputTokens);

    public long TotalOutputTokens => Interlocked.Read(ref _totalOutputTokens);
    public long SubAgentInputTokens => Interlocked.Read(ref _subAgentInputTokens);
    public long SubAgentOutputTokens => Interlocked.Read(ref _subAgentOutputTokens);

    public void Update(long inputTokens, long outputTokens)
    {
        Interlocked.Exchange(ref _lastInputTokens, inputTokens);
        Interlocked.Add(ref _totalInputTokens, inputTokens);
        Interlocked.Add(ref _totalOutputTokens, outputTokens);
    }

    /// <summary>
    /// Accumulate per-notification usage deltas from streaming (where snapshots are cumulative)
    /// and record the latest cumulative input token count for <see cref="LastInputTokens"/> compaction checks.
    /// </summary>
    public void UpdateWithStreamingDeltas(long deltaInput, long deltaOutput, long cumulativeInputSnapshot)
    {
        Interlocked.Exchange(ref _lastInputTokens, cumulativeInputSnapshot);
        Interlocked.Add(ref _totalInputTokens, deltaInput);
        Interlocked.Add(ref _totalOutputTokens, deltaOutput);
    }

    public void AddSubAgentTokens(long input, long output)
    {
        Interlocked.Add(ref _subAgentInputTokens, input);
        Interlocked.Add(ref _subAgentOutputTokens, output);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _lastInputTokens, 0);
        Interlocked.Exchange(ref _totalInputTokens, 0);
        Interlocked.Exchange(ref _totalOutputTokens, 0);
        Interlocked.Exchange(ref _subAgentInputTokens, 0);
        Interlocked.Exchange(ref _subAgentOutputTokens, 0);
    }

    /// <summary>
    /// Format token count in compact human-readable form for spinner display.
    /// </summary>
    public static string FormatCompact(long tokens) => tokens switch
    {
        >= 1_000_000 => $"{tokens / 1_000_000.0:0.#}M",
        >= 1_000 => $"{tokens / 1_000.0:0.#}k",
        _ => tokens.ToString()
    };
}
