namespace DotCraft.Automations.Abstractions;

/// <summary>
/// Source-agnostic representation of a unit of automation work.
/// Each <see cref="IAutomationSource"/> produces a concrete subclass.
/// </summary>
public abstract class AutomationTask
{
    /// <summary>Stable unique identifier, unique within its source.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable title for display in the Desktop Automations view.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>Current lifecycle state of the task.</summary>
    public AutomationTaskStatus Status { get; set; }

    /// <summary>
    /// Name of the <see cref="IAutomationSource"/> that owns this task.
    /// Used to route lifecycle calls back to the correct source.
    /// </summary>
    public required string SourceName { get; init; }

    /// <summary>
    /// Thread identifier of the active agent session for this task, if any.
    /// Null when no agent session has been started.
    /// </summary>
    public string? ThreadId { get; set; }
}
