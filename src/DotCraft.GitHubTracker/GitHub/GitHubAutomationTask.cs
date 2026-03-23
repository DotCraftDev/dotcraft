using DotCraft.Automations.Abstractions;
using DotCraft.GitHubTracker.Tracker;

namespace DotCraft.GitHubTracker.GitHub;

/// <summary>
/// An <see cref="AutomationTask"/> backed by a GitHub issue or pull request.
/// </summary>
public sealed class GitHubAutomationTask : AutomationTask
{
    /// <summary>GitHub repository in "owner/name" form.</summary>
    public required string RepositoryFullName { get; init; }

    /// <summary>GitHub issue or PR number.</summary>
    public required int IssueNumber { get; init; }

    /// <summary>Whether this work item is a PR or an issue.</summary>
    public required WorkItemKind Kind { get; init; }

    /// <summary>The underlying tracked work item from <see cref="IWorkItemTracker"/>.</summary>
    public required TrackedWorkItem WorkItem { get; init; }

    /// <summary>
    /// For PR re-review: the head SHA at which the last review was submitted.
    /// Null for issues and for PRs not yet reviewed.
    /// </summary>
    public string? ReviewedAtSha { get; set; }

    /// <summary>
    /// Creates a <see cref="GitHubAutomationTask"/> from a <see cref="TrackedWorkItem"/>,
    /// populating all base-class fields.
    /// </summary>
    public static GitHubAutomationTask FromWorkItem(
        TrackedWorkItem workItem,
        string repositoryFullName,
        string? toolProfileOverride = null)
    {
        return new GitHubAutomationTask
        {
            Id = workItem.Id,
            Title = $"{workItem.Identifier}: {workItem.Title}",
            Status = AutomationTaskStatus.Pending,
            SourceName = "github",
            Description = workItem.Description,
            CreatedAt = workItem.CreatedAt,
            UpdatedAt = workItem.UpdatedAt,
            ToolProfileOverride = toolProfileOverride,
            RepositoryFullName = repositoryFullName,
            IssueNumber = int.TryParse(
                workItem.Identifier.TrimStart('#'),
                out var num) ? num : 0,
            Kind = workItem.Kind,
            WorkItem = workItem
        };
    }
}
