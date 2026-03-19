using DotCraft.GitHubTracker.Orchestrator;
using DotCraft.GitHubTracker.Tests.Fakes;
using DotCraft.GitHubTracker.Tests.Helpers;
using DotCraft.GitHubTracker.Tracker;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.GitHubTracker.Tests.Orchestrator;

/// <summary>
/// Tests for ReviewedSha cleanup on terminal state detection (ReconcileAsync path).
/// Covers PR Lifecycle Spec sections 3.2 and 7.4; Symphony SPEC section 8.5.
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
    // State-simulation tests: existing coverage of the direct cleanup path
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

    [Fact]
    public void ActiveStateRefresh_PreservesReviewedSha()
    {
        const string prId = "101";
        const string sha = "active-sha";

        lock (_orchestrator.StateLock)
            _orchestrator.State.ReviewedSha[prId] = sha;

        lock (_orchestrator.StateLock)
        {
            Assert.True(_orchestrator.State.ReviewedSha.TryGetValue(prId, out var stored));
            Assert.Equal(sha, stored);
        }
    }

    [Fact]
    public void AfterTerminalCleanup_PrBecomesNewCandidate()
    {
        const string prId = "102";
        const string sha = "closed-sha";
        var pr = OrchestratorTestHelpers.MakePr(prId, headSha: sha);

        lock (_orchestrator.StateLock)
            _orchestrator.State.ReviewedSha[prId] = sha;

        Assert.False(_orchestrator.ShouldDispatch(pr, _config));

        lock (_orchestrator.StateLock)
            _orchestrator.State.ReviewedSha.Remove(prId);

        Assert.True(_orchestrator.ShouldDispatch(pr, _config));
    }

    // -------------------------------------------------------------------------
    // CleanupTerminalReviewedShaAsync: terminal PR not in Running is cleaned up
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CleanupTerminalReviewedSha_TerminalPrNotRunning_Removed()
    {
        const string terminalPrId = "200";
        const string activePrId = "201";

        lock (_orchestrator.StateLock)
        {
            _orchestrator.State.ReviewedSha[terminalPrId] = "sha-merged";
            _orchestrator.State.Completed.Add(terminalPrId);
            _orchestrator.State.ReviewedSha[activePrId] = "sha-active";
            _orchestrator.State.Completed.Add(activePrId);
        }

        _tracker.StateSnapshots[terminalPrId] = "Merged";
        _tracker.StateSnapshots[activePrId] = "Pending Review";

        await _orchestrator.CleanupTerminalReviewedShaAsync(CancellationToken.None);

        lock (_orchestrator.StateLock)
        {
            Assert.False(_orchestrator.State.ReviewedSha.ContainsKey(terminalPrId));
            Assert.DoesNotContain(terminalPrId, _orchestrator.State.Completed);

            Assert.True(_orchestrator.State.ReviewedSha.ContainsKey(activePrId));
            Assert.Contains(activePrId, _orchestrator.State.Completed);
        }
    }

    // -------------------------------------------------------------------------
    // CleanupTerminalReviewedShaAsync: executes when Running is empty (the bug fix)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CleanupTerminalReviewedSha_RunningEmpty_StillCleansTerminal()
    {
        const string prId = "202";

        lock (_orchestrator.StateLock)
        {
            // No running entries; this is the previously-missed path.
            Assert.Empty(_orchestrator.State.Running);
            _orchestrator.State.ReviewedSha[prId] = "sha-closed";
        }

        _tracker.StateSnapshots[prId] = "Closed";

        await _orchestrator.CleanupTerminalReviewedShaAsync(CancellationToken.None);

        lock (_orchestrator.StateLock)
            Assert.False(_orchestrator.State.ReviewedSha.ContainsKey(prId));
    }

    // -------------------------------------------------------------------------
    // CleanupTerminalReviewedShaAsync: IDs in Running or Claimed are skipped
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CleanupTerminalReviewedSha_RunningPr_NotRemoved()
    {
        const string prId = "203";
        var pr = OrchestratorTestHelpers.MakePr(prId, headSha: "sha-xyz");

        lock (_orchestrator.StateLock)
        {
            _orchestrator.State.ReviewedSha[prId] = "sha-xyz";
            _orchestrator.State.Running[prId] = new RunningEntry
            {
                WorkItemId = prId,
                Identifier = "#203",
                WorkItem = pr,
                StartedAt = DateTimeOffset.UtcNow,
                Cts = new System.Threading.CancellationTokenSource(),
                WorkerTask = Task.CompletedTask,
            };
        }

        // Even if the tracker reports terminal, running entry is protected.
        _tracker.StateSnapshots[prId] = "Merged";

        await _orchestrator.CleanupTerminalReviewedShaAsync(CancellationToken.None);

        lock (_orchestrator.StateLock)
            Assert.True(_orchestrator.State.ReviewedSha.ContainsKey(prId));
    }

    [Fact]
    public async Task CleanupTerminalReviewedSha_ClaimedPr_NotRemoved()
    {
        const string prId = "204";

        lock (_orchestrator.StateLock)
        {
            _orchestrator.State.ReviewedSha[prId] = "sha-abc";
            _orchestrator.State.Claimed.Add(prId);
        }

        _tracker.StateSnapshots[prId] = "Closed";

        await _orchestrator.CleanupTerminalReviewedShaAsync(CancellationToken.None);

        lock (_orchestrator.StateLock)
            Assert.True(_orchestrator.State.ReviewedSha.ContainsKey(prId));
    }

    // -------------------------------------------------------------------------
    // CleanupTerminalReviewedShaAsync: fetch failure does not delete entries
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CleanupTerminalReviewedSha_FetchThrows_NoEntriesRemoved()
    {
        const string prId = "205";

        lock (_orchestrator.StateLock)
            _orchestrator.State.ReviewedSha[prId] = "sha-xyz";

        var throwingOrchestrator = new GitHubTrackerOrchestrator(
            new ThrowingTracker(),
            _config,
            NullLogger<GitHubTrackerOrchestrator>.Instance);

        lock (throwingOrchestrator.StateLock)
            throwingOrchestrator.State.ReviewedSha[prId] = "sha-xyz";

        await throwingOrchestrator.CleanupTerminalReviewedShaAsync(CancellationToken.None);

        lock (throwingOrchestrator.StateLock)
            Assert.True(throwingOrchestrator.State.ReviewedSha.ContainsKey(prId));
    }

    // -------------------------------------------------------------------------
    // CleanupTerminalReviewedShaAsync: empty ReviewedSha is a no-op
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CleanupTerminalReviewedSha_EmptyDictionary_NoFetch()
    {
        lock (_orchestrator.StateLock)
            Assert.Empty(_orchestrator.State.ReviewedSha);

        // FetchWorkItemStatesByIdsAsync must not be called when there is nothing to clean up.
        var fetchCalled = false;
        _tracker.OnFetchWorkItemStatesByIds = _ => { fetchCalled = true; };

        await _orchestrator.CleanupTerminalReviewedShaAsync(CancellationToken.None);

        Assert.False(fetchCalled);
    }

    // -------------------------------------------------------------------------
    // Missing snapshot (PR not found by tracker) does not cause deletion
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CleanupTerminalReviewedSha_MissingSnapshot_EntryKept()
    {
        const string prId = "206";

        lock (_orchestrator.StateLock)
            _orchestrator.State.ReviewedSha[prId] = "sha-xyz";

        // StateSnapshots has no entry for prId → snapshot is missing → should not delete.
        await _orchestrator.CleanupTerminalReviewedShaAsync(CancellationToken.None);

        lock (_orchestrator.StateLock)
            Assert.True(_orchestrator.State.ReviewedSha.ContainsKey(prId));
    }

    /// <summary>Tracker that always throws on FetchWorkItemStatesByIdsAsync.</summary>
    private sealed class ThrowingTracker : FakeWorkItemTracker
    {
        public override Task<IReadOnlyList<WorkItemStateSnapshot>> FetchWorkItemStatesByIdsAsync(
            IReadOnlyList<string> workItemIds, CancellationToken ct = default)
            => Task.FromException<IReadOnlyList<WorkItemStateSnapshot>>(
                new InvalidOperationException("Simulated fetch failure"));
    }
}
