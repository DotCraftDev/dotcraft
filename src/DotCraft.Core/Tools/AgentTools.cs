using System.ComponentModel;
using DotCraft.Agents;

namespace DotCraft.Tools;

/// <summary>
/// Core tools for DotCraft agent.
/// </summary>
public sealed class AgentTools(SubAgentCoordinator? subAgentManager = null)
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
        5. Available subagent profiles are listed in the system prompt
        6. Do not guess profile names that are not listed there
        7. External CLI profiles may expose stage-level progress, but not native tool-by-tool execution details
        8. Pass workingDirectory only when the selected profile requires it
        """)]
    [Tool(Icon = "🐧", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.SpawnSubagent))]
    public async Task<string> SpawnSubagent(
        [Description("A detailed, self-contained description of the task for the subagent to execute autonomously. Include what to investigate, what tools to use, and exactly what to report back.")] string task,
        [Description("Optional short human-readable label shown in the UI (e.g. 'Explore auth module').")] string? label = null,
        [Description("Optional named subagent profile. Defaults to dotcraft-native when omitted.")] string? profile = null,
        [Description("Optional working directory used when the selected profile requires a specified working directory.")] string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        if (subAgentManager == null)
        {
            return "Subagent functionality is not available.";
        }

        return await subAgentManager.RunAsync(
            new SubAgentTaskRequest
            {
                Task = task,
                Label = label,
                WorkingDirectory = workingDirectory
            },
            profile,
            cancellationToken);
    }

    [Description("""
        Inspect configured subagent profiles and report runtime availability, binary resolution, and configuration warnings.

        Use this before testing external CLI profiles to confirm:
        - which built-in profiles are available
        - whether a CLI binary resolves on PATH
        - whether required fields like bin, outputJsonPath, or outputFileArgTemplate are missing
        - whether a profile requires workingDirectory to be passed to SpawnSubagent
        """)]
    [Tool(Icon = "🩺", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.InspectSubagentProfiles))]
    public string InspectSubagentProfiles()
    {
        if (subAgentManager == null)
            return "Subagent functionality is not available.";

        var diagnostics = subAgentManager.GetProfileDiagnostics();
        if (diagnostics.Count == 0)
            return "No subagent profiles are configured.";

        var lines = new List<string>();
        foreach (var profile in diagnostics)
        {
            var kind = profile.IsBuiltIn ? "built-in" : "configured";
            var runtimeStatus = profile.RuntimeRegistered ? "runtime ready" : "runtime missing";
            lines.Add($"{profile.Name} [{kind}]");
            lines.Add($"  runtime: {profile.Runtime} ({runtimeStatus})");
            lines.Add($"  workingDirectoryMode: {profile.WorkingDirectoryMode}");
            lines.Add(
                profile.HiddenFromPrompt
                    ? $"  promptVisibility: hidden ({profile.HiddenReason ?? "unknown reason"})"
                    : "  promptVisibility: visible");

            if (!string.IsNullOrWhiteSpace(profile.Bin))
            {
                var binaryStatus = !string.IsNullOrWhiteSpace(profile.ResolvedBinary)
                    ? $"resolved -> {profile.ResolvedBinary}"
                    : "not resolved";
                lines.Add($"  binary: {profile.Bin} ({binaryStatus})");
            }

            if (profile.Warnings.Count == 0)
            {
                lines.Add("  warnings: none");
            }
            else
            {
                lines.Add("  warnings:");
                foreach (var warning in profile.Warnings)
                    lines.Add($"    - {warning}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}
