using DotCraft.GitHubTracker.Tracker;

namespace DotCraft.GitHubTracker.Tests.Fakes;

/// <summary>
/// In-memory fake implementation of <see cref="IWorkItemTracker"/> for unit tests.
/// Configure <see cref="Candidates"/> and <see cref="StateSnapshots"/> before each test.
/// Captured calls are recorded in <see cref="SubmittedReviews"/>.
/// </summary>
public class FakeWorkItemTracker : IWorkItemTracker
{
    public List<TrackedWorkItem> Candidates { get; set; } = [];

    public Dictionary<string, string> StateSnapshots { get; set; } = [];

    public List<(string PullNumber, string Body, string Event)> SubmittedReviews { get; } = [];
    public List<(string PullNumber, PullRequestReviewSummary Summary, IReadOnlyList<PullRequestInlineComment> Comments)> SubmittedStructuredReviews { get; } = [];
    public StructuredReviewSubmitResult StructuredReviewResult { get; set; } = new()
    {
        SummaryPosted = true,
        InlineRequestedCount = 0,
        InlinePostedCount = 0,
        InlineFailedCount = 0,
    };
    public Dictionary<string, IReadOnlyList<PullRequestChangedFile>> PullRequestFiles { get; set; } = [];
    public Dictionary<string, IReadOnlyList<PullRequestReviewFinding>> PullRequestFindings { get; set; } = [];

    /// <summary>Optional callback invoked at the start of FetchWorkItemStatesByIdsAsync for observation in tests.</summary>
    public Action<IReadOnlyList<string>>? OnFetchWorkItemStatesByIds { get; set; }

    public Task<IReadOnlyList<TrackedWorkItem>> FetchCandidateWorkItemsAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TrackedWorkItem>>(Candidates);

    public virtual Task<IReadOnlyList<WorkItemStateSnapshot>> FetchWorkItemStatesByIdsAsync(
        IReadOnlyList<string> workItemIds, CancellationToken ct = default)
    {
        OnFetchWorkItemStatesByIds?.Invoke(workItemIds);
        var result = workItemIds
            .Where(id => StateSnapshots.ContainsKey(id))
            .Select(id => new WorkItemStateSnapshot { Id = id, State = StateSnapshots[id] })
            .ToList();
        return Task.FromResult<IReadOnlyList<WorkItemStateSnapshot>>(result);
    }

    public Task<IReadOnlyList<TrackedWorkItem>> FetchWorkItemsByStatesAsync(
        IReadOnlyList<string> stateNames, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TrackedWorkItem>>([]);

    public Task CloseIssueAsync(string issueId, string reason, CancellationToken ct = default)
        => Task.CompletedTask;

    public virtual Task SubmitReviewAsync(string pullNumber, string body, string @event, CancellationToken ct = default)
    {
        SubmittedReviews.Add((pullNumber, body, @event));
        return Task.CompletedTask;
    }

    public virtual Task<StructuredReviewSubmitResult> SubmitStructuredReviewAsync(
        string pullNumber,
        PullRequestReviewSummary summary,
        IReadOnlyList<PullRequestInlineComment> comments,
        CancellationToken ct = default)
    {
        SubmittedStructuredReviews.Add((pullNumber, summary, comments));
        return Task.FromResult(new StructuredReviewSubmitResult
        {
            SummaryPosted = StructuredReviewResult.SummaryPosted,
            UsedFallback = StructuredReviewResult.UsedFallback,
            InlineRequestedCount = comments.Count,
            InlinePostedCount = StructuredReviewResult.InlinePostedCount,
            InlineFailedCount = StructuredReviewResult.InlineFailedCount,
            Warnings = StructuredReviewResult.Warnings,
            PostedComments = StructuredReviewResult.PostedComments,
        });
    }

    public Task<string> FetchPullRequestDiffAsync(string pullNumber, CancellationToken ct = default)
        => Task.FromResult(string.Empty);

    public Task<IReadOnlyList<PullRequestChangedFile>> FetchPullRequestFilesAsync(
        string pullNumber, CancellationToken ct = default)
    {
        PullRequestFiles.TryGetValue(pullNumber, out var files);
        return Task.FromResult(files ?? (IReadOnlyList<PullRequestChangedFile>)[]);
    }

    public Task<IReadOnlyList<PullRequestReviewFinding>> FetchBotReviewsAsync(
        string pullNumber, CancellationToken ct = default)
    {
        PullRequestFindings.TryGetValue(pullNumber, out var findings);
        return Task.FromResult(findings ?? (IReadOnlyList<PullRequestReviewFinding>)[]);
    }
}
