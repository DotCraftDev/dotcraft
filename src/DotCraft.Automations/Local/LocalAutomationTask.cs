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

    /// <summary>
    /// Absolute path to the provisioned agent workspace directory (set by the orchestrator before workflow load).
    /// </summary>
    public string? AgentWorkspacePath { get; set; }
}
