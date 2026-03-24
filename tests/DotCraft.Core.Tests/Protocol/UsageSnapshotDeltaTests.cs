using DotCraft.Context;
using DotCraft.Protocol;

namespace DotCraft.Tests.Protocol;

/// <summary>
/// Streaming providers often send cumulative <c>UsageContent</c> snapshots; <see cref="UsageSnapshotDelta"/>
/// converts them to true deltas for <c>item/usage/delta</c> (see appserver-protocol §6.6).
/// </summary>
public sealed class UsageSnapshotDeltaTests
{
    [Fact]
    public void MonotonicSnapshots_2000_2100_2100_YieldDeltasSummingToFinalInput()
    {
        long lastIn = 0, lastOut = 0;
        long sumIn = 0, sumOut = 0;

        void Step(long curIn, long curOut)
        {
            UsageSnapshotDelta.Compute(curIn, curOut, ref lastIn, ref lastOut, out var dIn, out var dOut);
            sumIn += dIn;
            sumOut += dOut;
        }

        Step(2000, 0);
        Step(2100, 0);
        Step(2100, 50);

        Assert.Equal(2100, sumIn);
        Assert.Equal(50, sumOut);
        Assert.Equal(2100, lastIn);
        Assert.Equal(50, lastOut);
    }

    [Fact]
    public void NewSubRound_InputDecreases_FirstSnapshotTreatedAsDelta()
    {
        long lastIn = 0, lastOut = 0;

        UsageSnapshotDelta.Compute(5000, 200, ref lastIn, ref lastOut, out var d1In, out var d1Out);
        Assert.Equal(5000, d1In);
        Assert.Equal(200, d1Out);

        // New LLM HTTP call: cumulative resets (e.g. 3000 total for this request only)
        UsageSnapshotDelta.Compute(3000, 100, ref lastIn, ref lastOut, out var d2In, out var d2Out);
        Assert.Equal(3000, d2In);
        Assert.Equal(100, d2Out);
    }

    /// <summary>
    /// Invariant: sum of emitted deltas for the main agent stream matches the final cumulative snapshot
    /// (aligns with appserver-protocol §6.6 — client sum of <c>item/usage/delta</c> vs turn totals).
    /// </summary>
    [Fact]
    public void TokenTracker_UpdateWithStreamingDeltas_AccumulatesTotalsAndKeepsLastInputSnapshot()
    {
        var tracker = new TokenTracker();
        tracker.UpdateWithStreamingDeltas(2000, 0, 2000);
        Assert.Equal(2000, tracker.TotalInputTokens);
        Assert.Equal(2000, tracker.LastInputTokens);

        tracker.UpdateWithStreamingDeltas(100, 50, 2100);
        Assert.Equal(2100, tracker.TotalInputTokens);
        Assert.Equal(50, tracker.TotalOutputTokens);
        Assert.Equal(2100, tracker.LastInputTokens);
    }

    [Fact]
    public void RepeatedIdenticalSnapshots_YieldZeroAdditionalDelta()
    {
        long lastIn = 0, lastOut = 0;

        UsageSnapshotDelta.Compute(2100, 0, ref lastIn, ref lastOut, out var d1In, out var d1Out);
        Assert.Equal(2100, d1In);

        UsageSnapshotDelta.Compute(2100, 0, ref lastIn, ref lastOut, out var d2In, out var d2Out);
        Assert.Equal(0, d2In);
        Assert.Equal(0, d2Out);
    }
}
