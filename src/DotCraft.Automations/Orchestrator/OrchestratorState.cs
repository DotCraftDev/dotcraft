using System.Collections.Concurrent;

namespace DotCraft.Automations.Orchestrator;

/// <summary>
/// In-memory orchestrator state (active task IDs and concurrency tracking).
/// </summary>
public sealed class OrchestratorState
{
    private readonly ConcurrentDictionary<string, byte> _activeTaskIds = new(StringComparer.Ordinal);

    /// <summary>Returns whether a task ID is currently being processed.</summary>
    public bool IsTaskActive(string taskId) => _activeTaskIds.ContainsKey(taskId);

    /// <summary>
    /// Attempts to mark a task as active. Returns false if the ID is already active.
    /// </summary>
    public bool TryBeginTask(string taskId) => _activeTaskIds.TryAdd(taskId, 0);

    /// <summary>Removes a task from the active set when work completes.</summary>
    public void EndTask(string taskId) => _activeTaskIds.TryRemove(taskId, out _);

    /// <summary>Number of tasks currently in the active set.</summary>
    public int ActiveTaskCount => _activeTaskIds.Count;
}
