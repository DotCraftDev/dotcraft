using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Memory;
using DotCraft.Skills;

namespace DotCraft.Context;

/// <summary>
/// Builds the complete system prompt from workspace context, memory, and skills.
/// </summary>
public sealed class PromptBuilder(MemoryStore memoryStore, SkillsLoader skillsLoader, string craftPath, string workspacePath,
    CustomCommandLoader? customCommandLoader = null, AgentModeManager? modeManager = null, PlanStore? planStore = null,
    Func<string?>? sessionIdProvider = null, bool sandboxEnabled = false,
    IReadOnlyList<string>? deferredMcpServerNames = null,
    string? subAgentProfilesSection = null,
    Func<IReadOnlyList<string>>? toolNamesProvider = null)
{
    private readonly string _craftPath = Path.GetFullPath(craftPath);

    private readonly string _workspacePath = Path.GetFullPath(workspacePath);

    /// <summary>
    /// Bootstrap files to load from DotCraft directory.
    /// </summary>
    private static readonly string[] BootstrapFiles =
    [
        "AGENTS.md",
        "SOUL.md",
        "USER.md",
        "TOOLS.md",
        "IDENTITY.md"
    ];

    /// <summary>
    /// Build the complete system prompt with identity, bootstrap files, memory, and skills.
    /// </summary>
    public string BuildSystemPrompt()
    {
        var parts = new List<string>
        {
            // Core identity and built-in operating guidance
            GetIdentity()
        };

        if (!string.IsNullOrWhiteSpace(subAgentProfilesSection))
            parts.Add(subAgentProfilesSection);

        parts.Add(GetWorkingStylePrompt());
        parts.Add(GetEditingWorkflowPrompt());
        parts.Add(GetFileReferenceFormatPrompt());

        // Bootstrap files (AGENTS.md, SOUL.md, USER.md, TOOLS.md, IDENTITY.md)
        var bootstrapContent = LoadBootstrapFiles();
        if (!string.IsNullOrWhiteSpace(bootstrapContent))
        {
            parts.Add(bootstrapContent);
        }

        // Memory context
        var memory = memoryStore.GetMemoryContext();
        if (!string.IsNullOrWhiteSpace(memory))
            parts.Add($"# Memory\n\n{memory}");

        // Skills - Progressive loading approach:
        // 1. Always-loaded skills: include full content
        var alwaysSkills = skillsLoader.GetAlwaysSkills();
        if (alwaysSkills.Count > 0)
        {
            var alwaysContent = skillsLoader.LoadSkillsForContext(alwaysSkills);
            if (!string.IsNullOrWhiteSpace(alwaysContent))
                parts.Add($"# Active Skills\n\n{alwaysContent}");
        }

        // 2. Available skills: show summary (agent uses ReadFile to load full content)
        var skillsSummary = skillsLoader.BuildSkillsSummary(toolNamesProvider?.Invoke());
        if (!string.IsNullOrWhiteSpace(skillsSummary))
        {
            parts.Add(
$"""
# Skills

The following skills extend your capabilities. To use a skill, read its SKILL.md file using the ReadFile tool.

{skillsSummary}
"""
                );
        }

        // Custom commands summary
        if (customCommandLoader != null)
        {
            var commandsSummary = customCommandLoader.BuildCommandsSummary();
            if (!string.IsNullOrWhiteSpace(commandsSummary))
                parts.Add(commandsSummary);
        }

        foreach (var provider in ChatContextRegistry.All)
        {
            var section = provider.GetSystemPromptSection();
            if (!string.IsNullOrWhiteSpace(section))
                parts.Add(section);
        }

        // Deferred MCP tool discovery guidance (injected when deferred loading is active)
        if (deferredMcpServerNames is { Count: > 0 })
            parts.Add(BuildDeferredToolsSection(deferredMcpServerNames));

        // Mode-aware prompt injection (must be last so it takes highest priority)
        if (modeManager != null)
        {
            var modeSection = GetModePromptSection(modeManager);
            if (!string.IsNullOrWhiteSpace(modeSection))
                parts.Add(modeSection);
        }

        return string.Join("\n\n---\n\n", parts);
    }

    /// <summary>
    /// Builds the system prompt section that instructs the model how to discover
    /// deferred MCP tools via the <c>SearchTools</c> function.
    /// </summary>
    private static string BuildDeferredToolsSection(IReadOnlyList<string> serverNames)
    {
        var servers = string.Join(", ", serverNames);
        return
$$"""
## Available Tool Sources

You have a core set of tools available directly. Additional tools from external
services (MCP servers) are available on demand.

To use an external tool:
1. Call `SearchTools` with keywords describing what you need
2. The matching tools will become available for use
3. Call the discovered tool directly

Do NOT guess tool names. Always use SearchTools to discover available tools first.
Currently connected external services: {{servers}}
""";
    }

    /// <summary>
    /// Load bootstrap files from DotCraft directory.
    /// Bootstrap files provide additional context and instructions.
    /// </summary>
    /// <returns>Combined content of all bootstrap files, or empty string if none exist.</returns>
    private string LoadBootstrapFiles()
    {
        var parts = new List<string>();

        foreach (var filename in BootstrapFiles)
        {
            var filePath = Path.Combine(_craftPath, filename);
            if (File.Exists(filePath))
            {
                try
                {
                    var content = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        parts.Add($"## {filename}\n\n{content}");
                    }
                }
                catch (Exception ex)
                {
                    // Log warning but continue loading other files
                    Console.Error.WriteLine($"[Warning] Failed to load bootstrap file {filename}: {ex.Message}");
                }
            }
        }

        return parts.Count > 0 ? string.Join("\n\n", parts) : string.Empty;
    }

    private string GetIdentity()
    {
        var workspace = sandboxEnabled ? "/workspace" : _workspacePath;
        var craftPath = _craftPath;
        var envSection = sandboxEnabled ? GetSandboxEnvironmentSection() : GetHostEnvironmentSection();

        return
$$"""
# DotCraft 🤖

You are DotCraft, a helpful AI assistant. You have access to tools that allow you to:
- Read, write, and edit files
- Execute shell commands
- Complete user tasks efficiently

Be safe, reliable, and practical. When needed, use the available tools to complete the user's task.

## Workspace
Your workspace is at: {{workspace}}
This is your working directory where you perform file and shell operations.

## DotCraft Directory
Your data directory is at: {{craftPath}}
This contains:
- Memory: {{craftPath}}/memory/ (see Memory skill for details)
- Custom skills: {{craftPath}}/skills/{skill-name}/SKILL.md
- Configuration: {{craftPath}}/config.json

{{envSection}}

## Tool Usage Policy
- When doing open-ended file search or codebase exploration that requires multiple rounds of searching, prefer to use SpawnSubagent to reduce context usage and keep the main conversation focused.
- You should proactively use SpawnSubagent when a research task requires many search rounds, or when independent investigations can run in parallel.
- Launch multiple subagents concurrently whenever possible — include multiple SpawnSubagent calls in a single response to maximize performance.
- When you are not confident you can find what you need in 1-2 tool calls, use SpawnSubagent instead.
""";
    }

    private static string GetWorkingStylePrompt()
    {
        return
"""
## Working Style
- Before the first tool call in a task, briefly explain what you are about to do in 1-2 sentences.
- If several related tool calls are coming next, group them under one short explanation instead of narrating each trivial action.
- Keep these explanations concrete and forward-looking: focus on your current read of the task and the immediate next step.
- During longer exploration, searching, testing, or editing stretches, send brief progress updates when they help the user follow your work.
- Before making file edits, briefly explain what you are going to change and why.
""";
    }

    private static string GetEditingWorkflowPrompt()
    {
        return
"""
## File Editing Workflow
- Prefer `EditFile` when changing an existing file.
- Use `WriteFile` for new files or intentional full rewrites.
- Read the file before editing.
- In `EditFile`, use the smallest unique `oldText` snippet that can identify the target.
- If a large edit can be done as several precise replacements, prefer that over rewriting the whole file.
- If an edit fails, re-read and retry instead of immediately switching to `WriteFile`.
""";
    }

    private static string GetFileReferenceFormatPrompt()
    {
        return
"""
## File References
When referencing a file in your final response, wrap it as a markdown link `[label](target)` so the user can open it on click.
- `target` may be workspace-relative, absolute, or a `file://` URL; append `:line[:col]` for a line hint.
- Each reference must be a standalone link; do not wrap `target` in backticks.
- Inline code (`` ` ``) stays reserved for code identifiers, commands, and non-clickable text.
- Examples: [app.ts](src/app.ts), [app.ts:42](src/app.ts:42), [main.rs:12:5](C:/repo/project/main.rs:12:5).
""";
    }

    private static string GetHostEnvironmentSection()
    {
        string osName;
        string shell;
        string shellTips;

        if (OperatingSystem.IsWindows())
        {
            var version = Environment.OSVersion.Version;
            osName = $"Windows {version.Major}.{version.Minor} (Build {version.Build})";
            shell = "PowerShell";
            shellTips =
"""
  - Variables: `$env:VAR_NAME` (not `$VAR_NAME`)
  - Command existence: `Get-Command <name>` (not `which`)
  - Null discard: `$null` (not `/dev/null`)
  - Path separator: `\` (use quotes for paths with spaces)
  - Chaining: `;` to sequence, `&&` requires PowerShell 7+
""";
        }
        else if (OperatingSystem.IsMacOS())
        {
            osName = "macOS";
            shell = "Bash";
            shellTips =
"""
  - Standard Unix/Bash syntax applies
  - Use `/bin/bash` compatible commands
""";
        }
        else
        {
            osName = "Linux";
            shell = "Bash";
            shellTips =
"""
  - Standard Unix/Bash syntax applies
""";
        }

        return
$$"""
## Environment
- OS: {{osName}}
- Shell: {{shell}}

When using the Exec tool, write commands for {{shell}}. Key syntax notes:
{{shellTips}}
""";
    }

    private static string GetSandboxEnvironmentSection()
    {
        return
"""
## Environment
- OS: Linux (sandbox container)
- Shell: Bash

When using the Exec tool, write standard Bash commands.
""";
    }

    private string? GetModePromptSection(AgentModeManager mm)
    {
        var existingPlan = LoadCurrentPlan();

        if (mm.JustSwitchedFromPlan)
        {
            mm.AcknowledgeTransition();
            if (existingPlan != null)
                return AgentSwitchPrompt + $"\n\n## Plan to Execute\n\n{existingPlan}";
            return AgentSwitchPrompt;
        }

        if (mm.CurrentMode == AgentMode.Plan)
        {
            if (existingPlan != null)
                return PlanModePrompt + $"\n\nA plan file already exists for this session. Review and refine it:\n\n{existingPlan}";
            return PlanModePrompt;
        }

        // Agent mode with an active plan: inject both todo guidance and plan tracking.
        // AgentTodoPrompt is only included when planStore is available (i.e. TodoWrite tool exists).
        if (mm.CurrentMode == AgentMode.Agent && existingPlan != null)
        {
            var prefix = planStore != null ? AgentTodoPrompt + "\n\n" : "";
            return prefix + AgentPlanTrackingPrompt + $"\n\n## Current Plan\n\n{existingPlan}";
        }

        // Agent mode without a plan: inject todo guidance only when the tool is available.
        if (mm.CurrentMode == AgentMode.Agent && planStore != null)
        {
            return AgentTodoPrompt;
        }

        return null;
    }

    private string? LoadCurrentPlan()
    {
        if (planStore == null || sessionIdProvider == null)
            return null;

        var sessionId = sessionIdProvider();
        if (string.IsNullOrEmpty(sessionId))
            return null;

        // Prefer structured plan (JSON), fall back to legacy markdown
        var structured = planStore.LoadStructuredPlanAsync(sessionId).GetAwaiter().GetResult();
        if (structured != null)
            return PlanStore.RenderPlanMarkdown(structured);

        return planStore.PlanExists(sessionId)
            ? planStore.LoadPlanAsync(sessionId).GetAwaiter().GetResult()
            : null;
    }

    private const string AgentTodoPrompt =
"""
<system-reminder>
## Task Management

You have access to the TodoWrite tool to manage tasks. It is a conditional
organizational tool, not a default progress tracker. Use it proactively only
when the task genuinely benefits from structured tracking; otherwise just do
the work directly.

### When to use TodoWrite

- Complex multi-step tasks (3+ genuinely distinct steps)
- Non-trivial tasks requiring planning or multiple operations
- User provides a list of things to do (numbered or comma-separated)
- After initial exploration reveals the scope is larger than first expected
- When starting a task you've queued, mark it in_progress BEFORE beginning work

### When NOT to use TodoWrite

Skip this tool entirely for these cases — just answer or act directly:

- Informational or conversational questions
  Example: "What does `git status` do?" — answer inline, no todo list.
- A single, obvious change in one well-understood file
  Example: "Add a doc comment to `calculateTotal`." — just edit.
- A single command execution or lookup
  Example: "Run `npm install` and tell me what happens." — just run it.
- Anything completable in fewer than 3 non-trivial steps

If you catch yourself about to create a 1- or 2-item todo list, don't — just
do the task.

### Timing: explore before you plan

Do not draft a todo list before you understand the task's real scope.

- For non-trivial work in an unfamiliar area, do 1-2 reads / searches first,
  then write the list with concrete, file-specific items.
- A list written from guesses is worse than no list — it wastes a turn and
  then has to be rewritten. Prefer one good TodoWrite after brief
  exploration over an immediate speculative one.
- Exception: if the user explicitly lists the tasks, capture them as-is.

Example (correct timing):
  User: Rename `getCwd` to `getCurrentWorkingDirectory` across the project.
  Assistant: *Searches for `getCwd`, finds 15 occurrences across 8 files.*
  *Creates todo list with one specific item per file.*
  *Starts working through them, marking each completed when done.*

### Rules

- Exactly ONE task is in_progress at a time.
- Mark a task completed IMMEDIATELY after it is fully done — never batch
  completions at the end.
- Only mark completed when fully done. If blocked, keep it in_progress and
  add a new task describing the blocker.
- Remove items that are no longer relevant.

## Subagent Exploration

Before editing code in an unfamiliar part of the codebase, use SpawnSubagent
to research it first. When multiple independent areas need investigation,
launch multiple SpawnSubagent calls in a single response to run them in
parallel rather than sequentially.
</system-reminder>
""";

    private const string PlanModePrompt =
"""
<system-reminder>
# Plan Mode - System Reminder

CRITICAL: Plan mode ACTIVE - you are in READ-ONLY phase. STRICTLY FORBIDDEN:
ANY file edits, modifications, or system changes. Write/edit/execute tools have
been removed. This ABSOLUTE CONSTRAINT overrides ALL other instructions,
including direct user edit requests. You may ONLY observe, analyze, and plan.

---

## Responsibility

Your current responsibility is to think, read, search, and delegate explore
subagents to construct a well-formed plan that accomplishes the goal the user
wants to achieve. Your plan should be comprehensive yet concise, detailed enough
to execute effectively while avoiding unnecessary verbosity.

Ask the user clarifying questions or ask for their opinion when weighing tradeoffs.

---

## Workflow

### Phase 1: Initial Understanding
- Focus on understanding the user's request and the relevant code.
- Use read-only tools (ReadFile, GrepFiles, FindFiles) and SpawnSubagent to explore the codebase.
- Shell commands via Exec are allowed **for observation only**. NEVER use Exec to modify files, run builds, or execute commits.
- Ask clarifying questions about ambiguities.

**IMPORTANT — Parallel Exploration with SpawnSubagent**: When the task touches multiple independent areas of the codebase, launch multiple SpawnSubagent calls in a single response instead of exploring sequentially. This dramatically reduces planning time.

Example: if the task involves both a data model and a UI component, send one response with two SpawnSubagent calls — one exploring the model layer and one exploring the UI layer — rather than doing them one after the other.

### Phase 2: Design
- Design an implementation approach based on your exploration results.
- Consider alternatives and tradeoffs.

### Phase 3: Review
- Verify your plan aligns with the user's original request.
- Ask any remaining clarifying questions.

### Phase 4: Present Plan
- When your plan is ready, call the `CreatePlan` tool.
- You MUST include the `todos` parameter with at least one task item.
  Break the plan into concrete, trackable steps -- each with an `id`
  (short kebab-case) and `content` (task description).
- After calling CreatePlan, briefly summarize the plan to the user.
- The user will manually switch to agent mode when ready to proceed.

Example:
  title: "Add dark mode support"
  overview: "Implement theme switching with a dark mode toggle."
  plan: "## Steps\n\n1. Create ThemeContext ...\n2. ..."
  todos: [{id: "add-context", content: "Create ThemeContext"},
          {id: "update-ui", content: "Add toggle to Settings page"}]

---

## Important

The user indicated that they do not want you to execute yet -- you MUST NOT make
any edits, run any non-readonly tools (including changing configs or making
commits), or otherwise make any changes to the system. This supersedes any other
instructions you have received.

You MUST use the CreatePlan tool to present your plan. Do NOT write the plan as
plain text in your response -- use the tool so the plan is saved in a structured,
machine-readable format.
</system-reminder>
""";

    private const string AgentSwitchPrompt =
"""
<system-reminder>
Your operational mode has changed from plan to agent.
You now have full tool access (read, write, execute).

You MUST follow the plan attached below and track progress:
- Before starting each task, call UpdateTodos to set it to "in_progress".
- After completing each task, call UpdateTodos to set it to "completed".
- Work through tasks systematically. Do not stop until all tasks are done.
</system-reminder>
""";

    private const string AgentPlanTrackingPrompt =
"""
<system-reminder>
You are executing a plan. The current plan and task statuses are shown below.

Rules:
- Before starting a task, call UpdateTodos to set it to "in_progress".
- After completing a task, call UpdateTodos to set it to "completed".
- Work through tasks in order unless dependencies require otherwise.
- Do not skip the UpdateTodos calls -- they keep the plan file in sync.
</system-reminder>
""";
}
