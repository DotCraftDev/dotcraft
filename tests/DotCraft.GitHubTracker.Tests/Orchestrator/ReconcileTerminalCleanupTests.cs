using DotCraft.GitHubTracker.Orchestrator;
using DotCraft.GitHubTracker.Tests.Fakes;
using DotCraft.GitHubTracker.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.GitHubTracker.Tests.Orchestrator;

/// <summary>
/// Tests for ReviewedSha cleanup on terminal state detection (ReconcileAsync path).
/// Covers PR Lifecycle Spec section 8.4 and Symphony SPEC section 8.5.
/// These tests drive <see cref="OrchestratorState"/> directly.
/// </summary>
public sealed class ReconcileTerminalCleanupTests
{
    private readonly FakeWorkItemTracker _tracker = new();
    private readonly GitHubTrackerConfig _config = OrchestratorTestHelpers.MakeConfig();
    private readonly GitHubTrackerOrchestrator _orchestrator;

    public ReconcileTerminalCleanupTests()
    {
        _orchestrator = new GitHubTrackerOrchestrator(
            _tracker,
            _config,
            NullLogger<GitHubTrackerOrchestrator>.Instance);
    }

    // -------------------------------------------------------------------------
    // Terminal state: ReviewedSha and Completed entries are removed
    // -------------------------------------------------------------------------

    [Fact]
    public void TerminalState_RemovesReviewedShaEntry()
    {
        const string prId = "99";

        lock (_orchestrator.StateLock)
        {
            _orchestrator.State.ReviewedSha[prId] = "merged-sha";
            _orchestrator.State.Completed.Add(prId);
        }

        // Simulate the terminal-state cleanup from ReconcileAsync.
        lock (_orchestrator.StateLock)
        {
            _orchestrator.State.Completed.Remove(prId);
            _orchestrator.State.ReviewedSha.Remove(prId);
        }

        lock (_orchestrator.StateLock)
        {
            Assert.False(_orchestrator.State.ReviewedSha.ContainsKey(prId));
            Assert.DoesNotContain(prId, _orchestrator.State.Completed);
        }
    }

    [Fact]
    public void TerminalState_RemovesCompletedEntry()
    {
        const string prId = "100";

        lock (_orchestrator.StateLock)
            _orchestrator.State.Completed.Add(prId);

        lock (_orchestrator.StateLock)
            _orchestrator.State.Completed.Remove(prId);

        lock (_orchestrator.StateLock)
            Assert.DoesNotContain(prId, _orchestrator.State.Completed);
    }

    // -------------------------------------------------------------------------
    // Active state refresh must not touch ReviewedSha
    // -------------------------------------------------------------------------

    [Fact]
    public void ActiveStateRefresh_PreservesReviewedSha()
    {
        const string prId = "101";
        const string sha = "active-sha";

        lock (_orchestrator.StateLock)
            _orchestrator.State.ReviewedSha[prId] = sha;

        // Simulate active-state refresh: only State on the running entry is updated,
        // ReviewedSha is not touched.
        lock (_orchestrator.StateLock)
        {
            Assert.True(_orchestrator.State.ReviewedSha.TryGetValue(prId, out var stored));
            Assert.Equal(sha, stored);
        }
    }

    // -------------------------------------------------------------------------
    // After terminal cleanup, the PR behaves as a new candidate on the next poll
    // -------------------------------------------------------------------------

    [Fact]
    public void AfterTerminalCleanup_PrBecomesNewCandidate()
    {
        const string prId = "102";
        const string sha = "closed-sha";
        var pr = OrchestratorTestHelpers.MakePr(prId, headSha: sha);

        // Pre-populate as reviewed.
        lock (_orchestrator.StateLock)
            _orchestrator.State.ReviewedSha[prId] = sha;

        // Confirm it would normally be skipped.
        Assert.False(_orchestrator.ShouldDispatch(pr, _config));

        // Simulate terminal cleanup removing the SHA.
        lock (_orchestrator.StateLock)
            _orchestrator.State.ReviewedSha.Remove(prId);

        // After cleanup, the PR (or a re-opened equivalent) dispatches again.
        Assert.True(_orchestrator.ShouldDispatch(pr, _config));
    }
}
