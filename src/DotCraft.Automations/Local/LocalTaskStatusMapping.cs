using DotCraft.Automations.Abstractions;

namespace DotCraft.Automations.Local;

/// <summary>
/// Maps between YAML status strings in task.md and <see cref="AutomationTaskStatus"/>.
/// </summary>
public static class LocalTaskStatusMapping
{
    public static AutomationTaskStatus FromYaml(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return AutomationTaskStatus.Pending;

        return status.Trim().ToLowerInvariant() switch
        {
            "pending" => AutomationTaskStatus.Pending,
            "dispatched" => AutomationTaskStatus.Dispatched,
            "agent_running" => AutomationTaskStatus.AgentRunning,
            "agent_completed" => AutomationTaskStatus.AgentCompleted,
            "awaiting_review" => AutomationTaskStatus.AwaitingReview,
            "approved" => AutomationTaskStatus.Approved,
            "rejected" => AutomationTaskStatus.Rejected,
            "failed" => AutomationTaskStatus.Failed,
            _ => AutomationTaskStatus.Pending
        };
    }

    public static string ToYaml(AutomationTaskStatus status) => status switch
    {
        AutomationTaskStatus.Pending => "pending",
        AutomationTaskStatus.Dispatched => "dispatched",
        AutomationTaskStatus.AgentRunning => "agent_running",
        AutomationTaskStatus.AgentCompleted => "agent_completed",
        AutomationTaskStatus.AwaitingReview => "awaiting_review",
        AutomationTaskStatus.Approved => "approved",
        AutomationTaskStatus.Rejected => "rejected",
        AutomationTaskStatus.Failed => "failed",
        _ => "pending"
    };
}
