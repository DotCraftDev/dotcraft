using System.ComponentModel;
using DotCraft.Agents;

namespace DotCraft.Tools;

/// <summary>
/// Core tools for DotCraft agent.
/// </summary>
public sealed class AgentTools(SubAgentManager? subAgentManager = null)
{
    [Description("Spawn a subagent to execute a task in the background. The subagent will work independently and report back when done.")]
    [Tool(Icon = "🐧", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.SpawnSubagent))]
    public async Task<string> SpawnSubagent(
        [Description("The task description for the subagent.")] string task,
        [Description("Optional human-readable label for the task.")] string? label = null)
    {
        if (subAgentManager == null)
        {
            return "Subagent functionality is not available.";
        }

        return await subAgentManager.SpawnAsync(task, label);
    }
}
