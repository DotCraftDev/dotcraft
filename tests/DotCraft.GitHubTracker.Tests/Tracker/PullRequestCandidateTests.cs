using System.Text.Json;
using DotCraft.GitHubTracker.Tests.Fakes;
using DotCraft.GitHubTracker.Tracker;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.GitHubTracker.Tests.Tracker;

/// <summary>
/// Tests for PR candidate selection and HeadSha mapping in <see cref="GitHubTrackerAdapter"/>.
/// Covers PR Lifecycle Spec sections 3.1–3.3.
/// Uses <see cref="MockHttpMessageHandler"/> to avoid real GitHub API calls.
/// </summary>
public sealed class PullRequestCandidateTests
{
    // GitHub review states used in responses
    private const string ReviewsEmpty = "[]";

    private static string BuildPrListJson(params object[] prs)
        => JsonSerializer.Serialize(prs);

    private static object MakePrJson(
        int number,
        string state = "open",
        bool draft = false,
        bool merged = false,
        string? headSha = "abc123",
        string? headRef = "feature/branch",
        string[]? labels = null)
        => new
        {
            number,
            title = $"PR #{number}",
            body = (string?)null,
            state,
            draft,
            merged,
            html_url = $"https://github.com/owner/repo/pull/{number}",
            diff_url = $"https://github.com/owner/repo/pull/{number}.diff",
            head = new { @ref = headRef, sha = headSha },
            @base = new { @ref = "main" },
            labels = labels?.Select(l => new { name = l }).ToArray() ?? Array.Empty<object>(),
            created_at = DateTimeOffset.UtcNow,
            updated_at = DateTimeOffset.UtcNow,
        };

    private GitHubTrackerAdapter CreateAdapter(MockHttpMessageHandler handler)
    {
        var config = new GitHubTrackerConfig
        {
            PullRequestWorkflowPath = "PR_WORKFLOW.md",
            Tracker = new GitHubTrackerTrackerConfig
            {
                Repository = "owner/repo",
                ApiKey = "test-token",
                PullRequestActiveStates = ["Pending Review"],
                PullRequestTerminalStates = ["Merged", "Closed", "Approved"],
            }
        };

        return new GitHubTrackerAdapter(
            config,
            handler,
            NullLogger<GitHubTrackerAdapter>.Instance);
    }

    // -------------------------------------------------------------------------
    // All open non-draft PRs in active states are returned (no label gate)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FetchCandidates_NoLabelFilter_AllNonDraftPrsReturned()
    {
        var handler = new MockHttpMessageHandler();
        // Two open PRs without any labels
        handler.AddResponse("/pulls?", BuildPrListJson(
            MakePrJson(1),
            MakePrJson(2)));
        handler.AddResponse("/pulls/1/reviews", ReviewsEmpty);
        handler.AddResponse("/pulls/2/reviews", ReviewsEmpty);

        var adapter = CreateAdapter(handler);
        var candidates = await adapter.FetchCandidateWorkItemsAsync();

        var prs = candidates.Where(c => c.Kind == WorkItemKind.PullRequest).ToList();
        Assert.Equal(2, prs.Count);
    }

    // -------------------------------------------------------------------------
    // Draft PRs are excluded
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FetchCandidates_DraftPr_IsExcluded()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse("/pulls?", BuildPrListJson(
            MakePrJson(3, draft: true),
            MakePrJson(4, draft: false)));
        handler.AddResponse("/pulls/4/reviews", ReviewsEmpty);

        var adapter = CreateAdapter(handler);
        var candidates = await adapter.FetchCandidateWorkItemsAsync();

        var prs = candidates.Where(c => c.Kind == WorkItemKind.PullRequest).ToList();
        Assert.Single(prs);
        Assert.Equal("4", prs[0].Id);
    }

    // -------------------------------------------------------------------------
    // HeadSha is populated from head.sha in the GitHub response
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FetchCandidates_HeadSha_IsMapped()
    {
        const string expectedSha = "deadbeef1234567890";

        var handler = new MockHttpMessageHandler();
        handler.AddResponse("/pulls?", BuildPrListJson(
            MakePrJson(5, headSha: expectedSha)));
        handler.AddResponse("/pulls/5/reviews", ReviewsEmpty);

        var adapter = CreateAdapter(handler);
        var candidates = await adapter.FetchCandidateWorkItemsAsync();

        var pr = Assert.Single(candidates, c => c.Kind == WorkItemKind.PullRequest);
        Assert.Equal(expectedSha, pr.HeadSha);
    }

    // -------------------------------------------------------------------------
    // HeadSha is null when head.sha is absent in the response
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FetchCandidates_HeadSha_NullWhenMissing()
    {
        var handler = new MockHttpMessageHandler();
        handler.AddResponse("/pulls?", BuildPrListJson(
            MakePrJson(6, headSha: null)));
        handler.AddResponse("/pulls/6/reviews", ReviewsEmpty);

        var adapter = CreateAdapter(handler);
        var candidates = await adapter.FetchCandidateWorkItemsAsync();

        var pr = Assert.Single(candidates, c => c.Kind == WorkItemKind.PullRequest);
        Assert.Null(pr.HeadSha);
    }
}
