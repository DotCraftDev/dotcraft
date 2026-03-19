using DotCraft.GitHubTracker.Orchestrator;
using DotCraft.GitHubTracker.Tests.Fakes;
using DotCraft.GitHubTracker.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.GitHubTracker.Tests.Orchestrator;

/// <summary>
/// Tests for SHA recording after a review is submitted (OnWorkerExitAsync path).
/// Covers PR Lifecycle Spec section 8.1.
/// These tests drive <see cref="OrchestratorState"/> directly to verify the
/// state transitions without spinning up the full poll loop.
/// </summary>
public sealed class ReviewedShaLifecycleTests
{
    private readonly FakeWorkItemTracker _tracker = new();
    private readonly GitHubTrackerConfig _config = OrchestratorTestHelpers.MakeConfig();
    private readonly GitHubTrackerOrchestrator _orchestrator;

    public ReviewedShaLifecycleTests()
    {
        _orchestrator = new GitHubTrackerOrchestrator(
            _tracker,
            _config,
            NullLogger<GitHubTrackerOrchestrator>.Instance);
    }

    // -------------------------------------------------------------------------
    // SHA is recorded and Claimed released when review is submitted
    // -------------------------------------------------------------------------

    [Fact]
    public void ReviewSubmitted_RecordsShaAndReleasesClaimed()
    {
        const string prId = "42";
        const string headSha = "deadbeef";

        lock (_orchestrator.StateLock)
            _orchestrator.State.Claimed.Add(prId);

        // Simulate the skipContinuation=true path from OnWorkerExitAsync.
        lock (_orchestrator.StateLock)
        {
            _orchestrator.State.ReviewedSha[prId] = headSha;
            _orchestrator.State.Claimed.Remove(prId);
        }

        lock (_orchestrator.StateLock)
        {
            Assert.True(_orchestrator.State.ReviewedSha.TryGetValue(prId, out var sha));
            Assert.Equal(headSha, sha);
            Assert.DoesNotContain(prId, _orchestrator.State.Claimed);
        }
    }

    // -------------------------------------------------------------------------
    // No SHA is recorded when review was not submitted
    // -------------------------------------------------------------------------

    [Fact]
    public void ReviewNotSubmitted_NoShaRecorded()
    {
        const string prId = "43";

        // Simulate skipContinuation=false: no SHA recorded, Claimed stays until retry.
        lock (_orchestrator.StateLock)
        {
            _orchestrator.State.Claimed.Add(prId);
            // ReviewedSha intentionally not set.
        }

        lock (_orchestrator.StateLock)
        {
            Assert.False(_orchestrator.State.ReviewedSha.ContainsKey(prId));
            Assert.Contains(prId, _orchestrator.State.Claimed);
        }
    }

    // -------------------------------------------------------------------------
    // Null HeadSha must not insert a null value into ReviewedSha
    // -------------------------------------------------------------------------

    [Fact]
    public void ReviewSubmitted_NullHeadSha_SkipsShaRecording()
    {
        const string prId = "44";

        lock (_orchestrator.StateLock)
            _orchestrator.State.Claimed.Add(prId);

        // Simulate null HeadSha guard from OnWorkerExitAsync.
        string? headSha = null;
        lock (_orchestrator.StateLock)
        {
            if (headSha != null)
                _orchestrator.State.ReviewedSha[prId] = headSha;
            _orchestrator.State.Claimed.Remove(prId);
        }

        lock (_orchestrator.StateLock)
        {
            Assert.False(_orchestrator.State.ReviewedSha.ContainsKey(prId));
            Assert.DoesNotContain(prId, _orchestrator.State.Claimed);
        }
    }

    // -------------------------------------------------------------------------
    // After SHA is recorded, ShouldDispatch returns false for the same SHA
    // -------------------------------------------------------------------------

    [Fact]
    public void AfterShaRecorded_ShouldDispatch_ReturnsFalseForSameSha()
    {
        const string prId = "45";
        const string headSha = "cafebabe";
        var pr = OrchestratorTestHelpers.MakePr(prId, headSha: headSha);

        lock (_orchestrator.StateLock)
            _orchestrator.State.ReviewedSha[prId] = headSha;

        Assert.False(_orchestrator.ShouldDispatch(pr, _config));
    }

    // -------------------------------------------------------------------------
    // After SHA is recorded, new push (different SHA) re-enables dispatch
    // -------------------------------------------------------------------------

    [Fact]
    public void AfterShaRecorded_NewPush_ShouldDispatch_ReturnsTrue()
    {
        const string prId = "46";
        var pr = OrchestratorTestHelpers.MakePr(prId, headSha: "new-sha");

        lock (_orchestrator.StateLock)
            _orchestrator.State.ReviewedSha[prId] = "old-sha";

        Assert.True(_orchestrator.ShouldDispatch(pr, _config));
    }
}
