using DotCraft.Protocol.AppServer;

namespace DotCraft.Automations.Abstractions;

/// <summary>
/// Source-agnostic representation of a unit of automation work.
/// Each <see cref="IAutomationSource"/> produces a concrete subclass.
/// </summary>
public abstract class AutomationTask : IAutomationTaskEventPayload
{
    private AutomationTaskStatus _status;
    private readonly object _statusLock = new();

    /// <summary>Stable unique identifier, unique within its source.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable title for display in the Desktop Automations view.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>Current lifecycle state of the task. Thread-safe via lock.</summary>
    public AutomationTaskStatus Status
    {
        get
        {
            lock (_statusLock)
            {
                return _status;
            }
        }
        set
        {
            lock (_statusLock)
            {
                _status = value;
            }
        }
    }

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

    /// <summary>
    /// Optional per-task override for the tool profile name.
    /// When set, takes precedence over <see cref="IAutomationSource.ToolProfileName"/>.
    /// Used when a single source produces tasks that require different tool sets (e.g. PR vs Issue).
    /// </summary>
    public string? ToolProfileOverride { get; init; }

    /// <summary>Markdown description / body of the task.</summary>
    public string? Description { get; set; }

    /// <summary>Summary written by the agent upon completion.</summary>
    public string? AgentSummary { get; set; }

    /// <summary>UTC creation time.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>UTC last update time.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
