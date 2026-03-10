using DotCraft.GitHubTracker.Tracker;

namespace DotCraft.GitHubTracker.Orchestrator;

/// <summary>
/// Sorts candidate issues for dispatch per SPEC.md Section 8.2:
/// priority ascending (null last) -> created_at oldest first -> identifier lexicographic.
/// </summary>
public static class DispatchSorter
{
    public static IReadOnlyList<TrackedIssue> Sort(IEnumerable<TrackedIssue> issues)
    {
        return issues
            .OrderBy(i => i.Priority.HasValue ? 0 : 1)
            .ThenBy(i => i.Priority ?? int.MaxValue)
            .ThenBy(i => i.CreatedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(i => i.Identifier, StringComparer.Ordinal)
            .ToList();
    }
}
