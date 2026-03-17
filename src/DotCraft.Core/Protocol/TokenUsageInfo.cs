namespace DotCraft.Protocol;

/// <summary>
/// Accumulated token usage for a Turn.
/// </summary>
public sealed record TokenUsageInfo
{
    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long TotalTokens { get; init; }

    public static TokenUsageInfo operator +(TokenUsageInfo a, TokenUsageInfo b) =>
        new()
        {
            InputTokens = a.InputTokens + b.InputTokens,
            OutputTokens = a.OutputTokens + b.OutputTokens,
            TotalTokens = a.TotalTokens + b.TotalTokens
        };
}
