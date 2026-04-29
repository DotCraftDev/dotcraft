using DotCraft.Configuration;

namespace DotCraft.Context.Compaction;

/// <summary>
/// Configuration for the layered context-compaction pipeline
/// (pre-flight estimation, microcompact, partial summary, reactive retry).
/// </summary>
[ConfigSection("Compaction", DisplayName = "Compaction", Order = 12)]
public sealed class CompactionConfig
{
    /// <summary>
    /// Enables the auto-compact pipeline. When disabled, the pipeline is a no-op;
    /// the model call may still fail with prompt_too_long which the reactive
    /// fallback (if enabled) will attempt to recover from.
    /// </summary>
    [ConfigField(Hint = "Enable automatic context compaction when estimated tokens cross the threshold.")]
    public bool AutoCompactEnabled { get; set; } = true;

    /// <summary>
    /// Enables reactive compaction: on prompt_too_long-style errors mid-turn,
    /// attempt to compact and retry the turn once before failing.
    /// </summary>
    [ConfigField(Hint = "Retry compaction when the model rejects the request for being too long.")]
    public bool ReactiveCompactEnabled { get; set; } = true;

    /// <summary>
    /// Model context window in tokens (default tuned for 200K-class models).
    /// </summary>
    [ConfigField(Min = 1000, Hint = "Model context window in tokens.")]
    public int ContextWindow { get; set; } = 200_000;

    /// <summary>
    /// Tokens reserved for the summary output so auto-compact triggers before
    /// the prefix + expected summary exceed the window.
    /// </summary>
    [ConfigField(Min = 0, Hint = "Tokens reserved for the compaction summary output.")]
    public int SummaryReserveTokens { get; set; } = 20_000;

    /// <summary>
    /// Additional safety buffer below (ContextWindow - SummaryReserve) at which
    /// auto-compact fires. Mirrors openclaude's AUTOCOMPACT_BUFFER_TOKENS (13k).
    /// </summary>
    [ConfigField(Min = 0, Hint = "Auto-compact fires when tokens reach ContextWindow - SummaryReserve - AutoCompactBuffer.")]
    public int AutoCompactBufferTokens { get; set; } = 13_000;

    /// <summary>
    /// Warning threshold buffer: emit compactWarning event when tokens reach
    /// (autoThreshold - WarningBuffer).
    /// </summary>
    [ConfigField(Min = 0, Hint = "Emit compactWarning event this many tokens before the auto threshold.")]
    public int WarningBufferTokens { get; set; } = 20_000;

    /// <summary>
    /// Error threshold buffer: emit compactError event when tokens reach
    /// (autoThreshold - ErrorBuffer). Typically equal to WarningBuffer.
    /// </summary>
    [ConfigField(Min = 0, Hint = "Emit compactError event this many tokens before the auto threshold.")]
    public int ErrorBufferTokens { get; set; } = 10_000;

    /// <summary>
    /// Margin kept when computing the manual blocking limit so that /compact
    /// always has room to run even near the hard ceiling.
    /// </summary>
    [ConfigField(Min = 0, Hint = "Headroom kept below the hard context ceiling.")]
    public int ManualCompactBufferTokens { get; set; } = 3_000;

    /// <summary>
    /// Minimum tokens the partial compactor must keep verbatim after summarizing
    /// the prefix. Floor prevents dropping too much recent context.
    /// </summary>
    [ConfigField(Min = 0, Hint = "Minimum tokens to preserve verbatim after compaction.")]
    public int KeepRecentMinTokens { get; set; } = 10_000;

    /// <summary>
    /// Minimum number of API-round groups to keep verbatim. Walks from newest
    /// backwards until both min-tokens and min-groups are satisfied.
    /// </summary>
    [ConfigField(Min = 1, Hint = "Minimum API-round groups preserved verbatim after compaction.")]
    public int KeepRecentMinGroups { get; set; } = 3;

    /// <summary>
    /// Hard cap on tokens preserved verbatim; once the tail exceeds this cap
    /// the compactor stops extending the tail and summarizes the rest.
    /// </summary>
    [ConfigField(Min = 1000, Hint = "Hard cap on tokens preserved verbatim; floor for summary room.")]
    public int KeepRecentMaxTokens { get; set; } = 40_000;

    /// <summary>
    /// Enables the microcompact pass (pre-summary trimming of stale tool
    /// results). Runs before the full summary; may fully resolve pressure.
    /// </summary>
    [ConfigField(Hint = "Enable pre-summary trimming of stale tool results.")]
    public bool MicrocompactEnabled { get; set; } = true;

    /// <summary>
    /// Number of compactable tool results that triggers microcompact. The
    /// N-most-recent (see MicrocompactKeepRecent) are preserved intact.
    /// </summary>
    [ConfigField(Min = 1, Hint = "Tool-result count that triggers microcompact.")]
    public int MicrocompactTriggerCount { get; set; } = 30;

    /// <summary>
    /// Number of tool results to keep at full fidelity when microcompact fires.
    /// </summary>
    [ConfigField(Min = 1, Hint = "Number of tool results kept at full fidelity during microcompact.")]
    public int MicrocompactKeepRecent { get; set; } = 8;

    /// <summary>
    /// Idle gap (in minutes) since the last assistant message that also
    /// triggers microcompact. Assumes the provider cache has expired so the
    /// prefix will be fully rewritten on the next request regardless.
    /// Set to 0 to disable the time-based trigger.
    /// </summary>
    [ConfigField(Min = 0, Hint = "Minutes of idle time that trigger microcompact (0 to disable).")]
    public int MicrocompactGapMinutes { get; set; } = 20;

    /// <summary>
    /// Maximum consecutive compaction failures before the pipeline trips its
    /// circuit breaker for this thread and skips future attempts until reset.
    /// </summary>
    [ConfigField(Min = 1, Hint = "Circuit-breaker threshold: consecutive failures before giving up.")]
    public int MaxConsecutiveFailures { get; set; } = 3;

    /// <summary>
    /// Computes the effective context window: ContextWindow − SummaryReserve,
    /// floored so the auto-compact threshold cannot go negative.
    /// </summary>
    public int EffectiveContextWindow()
    {
        var effective = ContextWindow - SummaryReserveTokens;
        var floor = SummaryReserveTokens + AutoCompactBufferTokens;
        return Math.Max(effective, floor);
    }

    /// <summary>
    /// Token count at which auto-compact fires.
    /// </summary>
    public int AutoCompactThreshold()
    {
        var effective = EffectiveContextWindow();
        return Math.Max(1, effective - AutoCompactBufferTokens);
    }

    /// <summary>
    /// Token count at which the warning event is emitted.
    /// </summary>
    public int WarningThreshold()
    {
        var threshold = AutoCompactThreshold() - WarningBufferTokens;
        return Math.Max(1, threshold);
    }

    /// <summary>
    /// Token count at which the error event is emitted (always at or above the warning threshold).
    /// </summary>
    public int ErrorThreshold()
    {
        var threshold = AutoCompactThreshold() - ErrorBufferTokens;
        return Math.Max(WarningThreshold(), threshold);
    }

    /// <summary>
    /// Hard ceiling above which even manual /compact refuses to run.
    /// </summary>
    public int BlockingLimit()
    {
        var limit = EffectiveContextWindow() - ManualCompactBufferTokens;
        return Math.Max(AutoCompactThreshold(), limit);
    }
}
