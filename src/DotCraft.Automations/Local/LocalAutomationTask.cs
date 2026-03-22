using DotCraft.Automations.Abstractions;

namespace DotCraft.Automations.Local;

/// <summary>
/// Automation task backed by a <c>task.md</c> file under the local tasks root.
/// </summary>
public sealed class LocalAutomationTask : AutomationTask
{
    /// <summary>Absolute path to the task directory (contains task.md, workflow.md, workspace/).</summary>
    public required string TaskDirectory { get; init; }

    /// <summary>Absolute path to task.md.</summary>
    public string TaskFilePath => Path.Combine(TaskDirectory, "task.md");

    /// <summary>Absolute path to workflow.md.</summary>
    public string WorkflowFilePath => Path.Combine(TaskDirectory, "workflow.md");

    /// <summary>Markdown body of task.md (below YAML front matter).</summary>
    public string? Description { get; set; }

    /// <summary>Summary written by the agent upon completion.</summary>
    public string? AgentSummary { get; set; }

    /// <summary>
    /// Absolute path to the provisioned agent workspace directory (set by the orchestrator before workflow load).
    /// </summary>
    public string? AgentWorkspacePath { get; set; }

    /// <summary>UTC creation time from front matter.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>UTC last update time from front matter.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
