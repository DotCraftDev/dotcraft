using DotCraft.Abstractions;
using DotCraft.GitHubTracker.Tests.Fakes;
using DotCraft.GitHubTracker.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.GitHubTracker.Tests.Tools;

/// <summary>
/// Tests for <see cref="PullRequestReviewToolProvider"/> COMMENT-only policy.
/// Covers PR Lifecycle Spec section 7.1.
/// </summary>
public sealed class PullRequestReviewToolProviderTests
{
    private readonly FakeWorkItemTracker _tracker = new();

    private PullRequestReviewToolProvider CreateProvider(string pullNumber = "7")
        => new(pullNumber, _tracker, NullLogger<PullRequestReviewToolProvider>.Instance);

    private static AIFunction GetSubmitReviewTool(PullRequestReviewToolProvider provider)
    {
        // ToolProviderContext is not used by this provider; pass null! safely.
        var tools = provider.CreateTools(null!).ToList();
        return (AIFunction)Assert.Single(tools, t => t.Name == "SubmitReview");
    }

    private static ValueTask<object?> InvokeAsync(AIFunction tool, string reviewEvent, string body)
        => tool.InvokeAsync(
            new AIFunctionArguments(new Dictionary<string, object?>
            {
                ["reviewEvent"] = reviewEvent,
                ["body"] = body,
            }));

    // -------------------------------------------------------------------------
    // All reviewEvent inputs are submitted as COMMENT
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("APPROVE")]
    [InlineData("REQUEST_CHANGES")]
    [InlineData("COMMENT")]
    [InlineData("random-value")]
    public async Task SubmitReview_AlwaysUsesComment_RegardlessOfInput(string reviewEvent)
    {
        var provider = CreateProvider("7");
        var tool = GetSubmitReviewTool(provider);

        await InvokeAsync(tool, reviewEvent, "Test review body");

        Assert.Single(_tracker.SubmittedReviews);
        Assert.Equal("COMMENT", _tracker.SubmittedReviews[0].Event);
    }

    // -------------------------------------------------------------------------
    // ReviewCompleted is set on success
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SubmitReview_SetsReviewCompleted()
    {
        var provider = CreateProvider("8");
        var tool = GetSubmitReviewTool(provider);

        Assert.False(provider.ReviewCompleted);
        await InvokeAsync(tool, "COMMENT", "LGTM");
        Assert.True(provider.ReviewCompleted);
    }

    // -------------------------------------------------------------------------
    // ReviewCompleted stays false if submission throws
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SubmitReview_OnFailure_ReviewNotCompleted()
    {
        var throwingTracker = new ThrowingTracker();
        var provider = new PullRequestReviewToolProvider(
            "9", throwingTracker, NullLogger<PullRequestReviewToolProvider>.Instance);
        var tool = GetSubmitReviewTool(provider);

        var result = await InvokeAsync(tool, "COMMENT", "body").AsTask();

        Assert.False(provider.ReviewCompleted);
        Assert.Contains("Warning", result?.ToString());
    }

    // -------------------------------------------------------------------------
    // The correct PR number is passed to SubmitReviewAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SubmitReview_PassesCorrectPullNumber()
    {
        var provider = CreateProvider("123");
        var tool = GetSubmitReviewTool(provider);

        await InvokeAsync(tool, "COMMENT", "body");

        Assert.Equal("123", _tracker.SubmittedReviews[0].PullNumber);
    }

    /// <summary>Tracker that always throws on SubmitReviewAsync.</summary>
    private sealed class ThrowingTracker : FakeWorkItemTracker
    {
        public override Task SubmitReviewAsync(
            string pullNumber, string body, string @event, CancellationToken ct = default)
            => Task.FromException(new InvalidOperationException("Simulated failure"));
    }
}
