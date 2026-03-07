namespace DotCraft.Context;

public sealed class TokenTracker
{
    private long _lastInputTokens;
    
    private long _totalOutputTokens;

    public long LastInputTokens => Interlocked.Read(ref _lastInputTokens);

    public long TotalOutputTokens => Interlocked.Read(ref _totalOutputTokens);

    public void Update(long inputTokens, long outputTokens)
    {
        Interlocked.Exchange(ref _lastInputTokens, inputTokens);
        Interlocked.Add(ref _totalOutputTokens, outputTokens);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _lastInputTokens, 0);
        Interlocked.Exchange(ref _totalOutputTokens, 0);
    }
}
