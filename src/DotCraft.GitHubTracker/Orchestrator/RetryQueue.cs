namespace DotCraft.GitHubTracker.Orchestrator;

/// <summary>
/// Manages retry scheduling with exponential backoff per SPEC.md Section 8.4.
/// </summary>
public static class RetryQueue
{
    /// <summary>
    /// Compute delay for a continuation retry (normal exit).
    /// </summary>
    public static int ContinuationDelayMs => 1_000;

    /// <summary>
    /// Compute delay for a failure-driven retry with exponential backoff.
    /// Formula: min(10000 * 2^(attempt-1), maxBackoffMs)
    /// </summary>
    public static int ComputeBackoffDelayMs(int attempt, int maxBackoffMs)
    {
        if (attempt <= 0) attempt = 1;
        var exponent = Math.Min(attempt - 1, 30);
        var delay = (long)(10_000 * Math.Pow(2, exponent));
        return (int)Math.Min(delay, maxBackoffMs);
    }

    /// <summary>
    /// Get current monotonic time in milliseconds for scheduling.
    /// </summary>
    public static long NowMs => Environment.TickCount64;
}
