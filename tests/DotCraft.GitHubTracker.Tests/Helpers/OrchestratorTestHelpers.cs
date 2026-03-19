using DotCraft.GitHubTracker.Orchestrator;
using DotCraft.GitHubTracker.Tracker;

namespace DotCraft.GitHubTracker.Tests.Helpers;

/// <summary>
/// Factory helpers for building test work items and orchestrator state.
/// </summary>
internal static class OrchestratorTestHelpers
{
    internal static TrackedWorkItem MakePr(
        string id,
        string? headSha = "abc123",
        string state = "Pending Review",
        bool isDraft = false)
        => new()
        {
            Id = id,
            Identifier = $"#{id}",
            Title = $"PR #{id}",
            State = state,
            Kind = WorkItemKind.PullRequest,
            IsDraft = isDraft,
            HeadSha = headSha,
        };

    internal static TrackedWorkItem MakeIssue(string id, string state = "Todo")
        => new()
        {
            Id = id,
            Identifier = $"#{id}",
            Title = $"Issue #{id}",
            State = state,
            Kind = WorkItemKind.Issue,
        };

    internal static GitHubTrackerConfig MakeConfig() => new()
    {
        Tracker = new GitHubTrackerTrackerConfig
        {
            Repository = "owner/repo",
            PullRequestActiveStates = ["Pending Review", "Review Requested"],
            PullRequestTerminalStates = ["Merged", "Closed", "Approved"],
            TerminalStates = ["Done", "Closed", "Cancelled"],
        }
    };
}
