using DotCraft.Context.Compaction;

namespace DotCraft.Tests.Context.Compaction;

public sealed class CompactionPipelineTests
{
    private static CompactionConfig DefaultConfig() => new()
    {
        ContextWindow = 200_000,
        SummaryReserveTokens = 20_000,
        AutoCompactBufferTokens = 13_000,
        WarningBufferTokens = 20_000,
        ErrorBufferTokens = 10_000,
        ManualCompactBufferTokens = 3_000,
        MaxConsecutiveFailures = 3,
    };

    [Fact]
    public void EvaluateThreshold_BelowWarning()
    {
        var cfg = DefaultConfig();
        var pipeline = new CompactionPipeline(cfg, new DummyChatClient());

        var threshold = pipeline.EvaluateThreshold(10_000);
        Assert.False(threshold.AboveWarning);
        Assert.False(threshold.AboveError);
        Assert.False(threshold.AboveAuto);
    }

    [Fact]
    public void EvaluateThreshold_AboveAuto()
    {
        var cfg = DefaultConfig();
        var pipeline = new CompactionPipeline(cfg, new DummyChatClient());

        // AutoCompact threshold = (200k - 20k) - 13k = 167k.
        var threshold = pipeline.EvaluateThreshold(180_000);
        Assert.True(threshold.AboveWarning);
        Assert.True(threshold.AboveError);
        Assert.True(threshold.AboveAuto);
    }

    [Fact]
    public void EvaluateThreshold_AboveWarningBelowAuto()
    {
        var cfg = DefaultConfig();
        var pipeline = new CompactionPipeline(cfg, new DummyChatClient());

        // Auto = 167k; warning = 147k; error = 157k.
        var threshold = pipeline.EvaluateThreshold(150_000);
        Assert.True(threshold.AboveWarning);
        Assert.False(threshold.AboveError);
        Assert.False(threshold.AboveAuto);
    }

    [Fact]
    public void FailureTracker_TripsAfterConfiguredFailures()
    {
        var cfg = DefaultConfig();
        var pipeline = new CompactionPipeline(cfg, new DummyChatClient());

        var tracker = pipeline.Failures;
        Assert.False(tracker.IsTripped("t"));
        tracker.RecordFailure("t");
        tracker.RecordFailure("t");
        Assert.False(tracker.IsTripped("t"));
        tracker.RecordFailure("t");
        Assert.True(tracker.IsTripped("t"));

        tracker.RecordSuccess("t");
        Assert.False(tracker.IsTripped("t"));
    }

    [Fact]
    public void PercentLeft_IsZeroWhenAtWindow()
    {
        var cfg = DefaultConfig();
        var pipeline = new CompactionPipeline(cfg, new DummyChatClient());

        var threshold = pipeline.EvaluateThreshold(long.MaxValue);
        Assert.Equal(0.0, threshold.PercentLeft);
    }

    [Fact]
    public void PercentLeft_IsOneWhenEmpty()
    {
        var cfg = DefaultConfig();
        var pipeline = new CompactionPipeline(cfg, new DummyChatClient());

        var threshold = pipeline.EvaluateThreshold(0);
        Assert.Equal(1.0, threshold.PercentLeft, 3);
    }

    private sealed class DummyChatClient : Microsoft.Extensions.AI.IChatClient
    {
        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<Microsoft.Extensions.AI.ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages,
            Microsoft.Extensions.AI.ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
