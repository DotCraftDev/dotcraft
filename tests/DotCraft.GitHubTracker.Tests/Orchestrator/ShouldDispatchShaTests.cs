using DotCraft.GitHubTracker.Orchestrator;
using DotCraft.GitHubTracker.Tests.Fakes;
using DotCraft.GitHubTracker.Tests.Helpers;
using DotCraft.GitHubTracker.Tracker;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.GitHubTracker.Tests.Orchestrator;

/// <summary>
/// Tests for <see cref="GitHubTrackerOrchestrator.ShouldDispatch"/> SHA comparison logic.
/// Covers PR Lifecycle Spec section 6.1 and Symphony SPEC section 8.2.
/// </summary>
public sealed class ShouldDispatchShaTests
{
    private readonly FakeWorkItemTracker _tracker = new();
    private readonly GitHubTrackerConfig _config = OrchestratorTestHelpers.MakeConfig();
    private readonly GitHubTrackerOrchestrator _orchestrator;

    public ShouldDispatchShaTests()
    {
        _orchestrator = new GitHubTrackerOrchestrator(
            _tracker,
            _config,
            NullLogger<GitHubTrackerOrchestrator>.Instance);
    }

    // -------------------------------------------------------------------------
    // New PR — no reviewed SHA entry
    // -------------------------------------------------------------------------

    [Fact]
    public void ShouldDispatch_NewPr_NoReviewedSha_ReturnsTrue()
    {
        var pr = OrchestratorTestHelpers.MakePr("1", headSha: "sha-abc");

        Assert.True(_orchestrator.ShouldDispatch(pr, _config));
    }

    // -------------------------------------------------------------------------
    // Same SHA — already reviewed at this commit
    // -------------------------------------------------------------------------

    [Fact]
    public void ShouldDispatch_PrWithMatchingSha_ReturnsFalse()
    {
        var pr = OrchestratorTestHelpers.MakePr("2", headSha: "sha-same");
        lock (_orchestrator.StateLock)
            _orchestrator.State.ReviewedSha["2"] = "sha-same";

        Assert.False(_orchestrator.ShouldDispatch(pr, _config));
    }

    // -------------------------------------------------------------------------
    // Different SHA — new push detected
    // -------------------------------------------------------------------------

    [Fact]
    public void ShouldDispatch_PrWithDifferentSha_ReturnsTrue()
    {
        var pr = OrchestratorTestHelpers.MakePr("3", headSha: "sha-new");
        lock (_orchestrator.StateLock)
            _orchestrator.State.ReviewedSha["3"] = "sha-old";

        Assert.True(_orchestrator.ShouldDispatch(pr, _config));
    }

    [Fact]
    public void ShouldDispatch_PrWithDifferentSha_ClearsCompletedEntry()
    {
        var pr = OrchestratorTestHelpers.MakePr("4", headSha: "sha-new");
        lock (_orchestrator.StateLock)
        {
            _orchestrator.State.ReviewedSha["4"] = "sha-old";
            _orchestrator.State.Completed.Add("4");
        }

        _orchestrator.ShouldDispatch(pr, _config);

        lock (_orchestrator.StateLock)
            Assert.DoesNotContain("4", _orchestrator.State.Completed);
    }

    // -------------------------------------------------------------------------
    // Issues are unaffected by the SHA logic
    // -------------------------------------------------------------------------

    [Fact]
    public void ShouldDispatch_Issue_IgnoresShaLogic_ReturnsTrue()
    {
        var issue = OrchestratorTestHelpers.MakeIssue("5");

        Assert.True(_orchestrator.ShouldDispatch(issue, _config));
    }

    // -------------------------------------------------------------------------
    // Symphony SPEC section 8.2: claimed / running guards
    // -------------------------------------------------------------------------

    [Fact]
    public void ShouldDispatch_PrAlreadyRunning_ReturnsFalse()
    {
        var pr = OrchestratorTestHelpers.MakePr("6", headSha: "sha-xyz");
        lock (_orchestrator.StateLock)
        {
            _orchestrator.State.Running["6"] = new RunningEntry
            {
                WorkItemId = "6",
                Identifier = "#6",
                WorkItem = pr,
                StartedAt = DateTimeOffset.UtcNow,
                Cts = new System.Threading.CancellationTokenSource(),
                WorkerTask = Task.CompletedTask,
            };
        }

        Assert.False(_orchestrator.ShouldDispatch(pr, _config));
    }

    [Fact]
    public void ShouldDispatch_PrAlreadyClaimed_ReturnsFalse()
    {
        var pr = OrchestratorTestHelpers.MakePr("7", headSha: "sha-xyz");
        lock (_orchestrator.StateLock)
            _orchestrator.State.Claimed.Add("7");

        Assert.False(_orchestrator.ShouldDispatch(pr, _config));
    }

    // -------------------------------------------------------------------------
    // Null HeadSha edge case — treated as new (no reviewed entry matches null)
    // -------------------------------------------------------------------------

    [Fact]
    public void ShouldDispatch_PrWithNullHeadSha_ReturnsTrue()
    {
        var pr = OrchestratorTestHelpers.MakePr("8", headSha: null);

        Assert.True(_orchestrator.ShouldDispatch(pr, _config));
    }
}
