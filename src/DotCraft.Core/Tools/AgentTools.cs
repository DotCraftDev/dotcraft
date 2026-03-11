using System.ComponentModel;
using DotCraft.Agents;

namespace DotCraft.Tools;

/// <summary>
/// Core tools for DotCraft agent.
/// </summary>
public sealed class AgentTools(SubAgentManager? subAgentManager = null)
{
    [Description("""
        Launch a subagent to autonomously handle a research or exploration task and report back.

        The subagent has access to: ReadFile, WriteFile, GrepFiles, FindFiles, Exec (shell), WebSearch, WebFetch.
        It runs independently in parallel and returns a single result message when done.

        ### When to Use (prefer SpawnSubagent over doing the work yourself)
        - Open-ended codebase exploration that requires multiple rounds of search (e.g. "find all places that handle X", "understand how Y is implemented across the project")
        - Researching an unfamiliar part of the codebase before you start editing
        - Any investigation where you are not confident you will find the answer in 1-2 tool calls
        - When multiple independent questions can be investigated in parallel — launch multiple subagents concurrently in a single response for maximum performance
        - Web research tasks (searching docs, fetching references) that run in parallel with other work

        ### When NOT to Use (do the work directly instead)
        - Reading a specific file at a known path — use ReadFile directly
        - Searching within 1-2 specific files — use ReadFile or GrepFiles directly
        - Looking up a specific class/symbol whose location you already know — use GrepFiles directly
        - Trivial single-step lookups that will succeed on the first try
        - Edit and write file

        ### Usage Notes
        1. Launch multiple subagents concurrently whenever possible: include multiple SpawnSubagent calls in a single response
        2. Each subagent is stateless — provide a self-contained, highly detailed task prompt so it can work autonomously
        3. Specify exactly what information the subagent should return in its result
        4. The subagent's output is trusted; use it directly to inform your next actions
        """)]
    [Tool(Icon = "🐧", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.SpawnSubagent))]
    public async Task<string> SpawnSubagent(
        [Description("A detailed, self-contained description of the task for the subagent to execute autonomously. Include what to investigate, what tools to use, and exactly what to report back.")] string task,
        [Description("Optional short human-readable label shown in the UI (e.g. 'Explore auth module').")] string? label = null)
    {
        if (subAgentManager == null)
        {
            return "Subagent functionality is not available.";
        }

        return await subAgentManager.SpawnAsync(task, label);
    }
}
