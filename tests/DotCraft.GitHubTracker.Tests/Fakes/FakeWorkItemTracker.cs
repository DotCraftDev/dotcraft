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

    public Task<string> FetchPullRequestDiffAsync(string pullNumber, CancellationToken ct = default)
        => Task.FromResult(string.Empty);
}
