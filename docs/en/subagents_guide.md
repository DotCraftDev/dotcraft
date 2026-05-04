# SubAgent Configuration Guide

SubAgents let the main Agent delegate a bounded task to a child Agent. Keep these two concepts separate:

- `agentRole` controls behavior, tool boundaries, and role instructions.
- `profile` controls the runtime, such as DotCraft native runtime or an external CLI.

If you only need safe first-level delegation, you usually do not need to change any settings. The default allows the root Agent to spawn one SubAgent and prevents that SubAgent from spawning another SubAgent.

## Quick Start

The default configuration is equivalent to:

```json
{
  "SubAgent": {
    "MaxDepth": 1
  }
}
```

With this setting:

- The root Agent can call `SpawnAgent` to create a first-level SubAgent.
- A first-level SubAgent is already at the depth limit and cannot create another SubAgent.
- Omitted `agentRole` means `default`.
- Native SubAgents use the lightweight prompt profile: `subagent-light`.

## Built-In Roles

| Role | Best for | Tool policy |
|------|----------|-------------|
| `default` | First-level collaboration, summaries, local analysis | Disables AgentTools and uses a conservative tool surface |
| `worker` | Implementation, validation, file changes | Allows read/write, shell, and web tools; AgentTools still obey `MaxDepth` |
| `explorer` | Read-only code exploration and research | Keeps read-only exploration and web tools; disables writes, shell, Plan/Todo, SkillManage, and AgentTools |

`worker` has the capability model for recursive delegation, but the default `SubAgent.MaxDepth = 1` prevents a first-level SubAgent from calling `SpawnAgent`. Recursive SubAgents are therefore an explicit advanced option.

## Configuration

SubAgent settings can live in global `~/.craft/config.json` or workspace `<workspace>/.craft/config.json`. For team projects, prefer workspace config.

### Enable Second-Level Worker Delegation

```json
{
  "SubAgent": {
    "MaxDepth": 2
  }
}
```

After this:

- A first-level `worker` SubAgent spawned by the root Agent can see `SpawnAgent`.
- A second-level SubAgent reaches the depth limit and cannot spawn a third level.
- `default` and `explorer` still disable AgentTools according to their role policies.

### Override Built-In Worker Instructions

A configured role with the same name overrides the built-in role. This example keeps the `worker` lightweight prompt profile and adds team-specific instructions:

```json
{
  "SubAgent": {
    "Roles": [
      {
        "Name": "worker",
        "Description": "Team worker role for bounded implementation tasks.",
        "AgentControlToolAccess": "Full",
        "PromptProfile": "subagent-light",
        "ToolDenyList": ["UpdateTodos", "TodoWrite", "CreatePlan"],
        "Instructions": "Complete the assigned task within the requested files. Summarize changed paths and validation results."
      }
    ]
  }
}
```

### Add a Read-Only Explorer Role

```json
{
  "SubAgent": {
    "Roles": [
      {
        "Name": "docs-explorer",
        "Description": "Read-only documentation and code explorer.",
        "ToolAllowList": ["ReadFile", "GrepFiles", "FindFiles", "WebSearch", "WebFetch", "SkillView"],
        "AgentControlToolAccess": "Disabled",
        "PromptProfile": "subagent-light",
        "Instructions": "Inspect files and web sources only. Do not edit files, execute shell commands, manage skills, or spawn agents."
      }
    ]
  }
}
```

The main Agent passes the task as `agentPrompt` when it calls `SpawnAgent`, and can set `agentRole` to `docs-explorer`.

### Use the Full Prompt

Native SubAgents use `subagent-light` by default. If a role needs the full root context, set `PromptProfile` to `full`:

```json
{
  "SubAgent": {
    "Roles": [
      {
        "Name": "full-context-worker",
        "Description": "Worker role with the full root prompt context.",
        "AgentControlToolAccess": "Disabled",
        "PromptProfile": "full",
        "Instructions": "Use the full project context to complete the assigned task."
      }
    ]
  }
}
```

## Lightweight Prompt

`subagent-light` is the default prompt profile for native session-backed SubAgents. It keeps:

- DotCraft base identity
- Current workspace and environment
- Role instructions
- Tool capabilities and restrictions
- `.craft/AGENTS.md`
- Necessary file references and working style

It skips by default:

- Full memory context
- Long Skill self-learning instructions
- Custom command summaries
- Long deferred MCP discovery instructions
- Plan/Todo injection

This helps short-task SubAgents start faster and avoids copying the main thread's long-term context into every child thread.

## Profile and External CLI

`profile` chooses the runtime. Common values include:

| Profile | Description |
|---------|-------------|
| `native` | DotCraft native SubAgent with role-resolved tool filtering |
| `codex-cli` | Uses Codex CLI as a one-shot external SubAgent |
| `cursor-cli` | Uses Cursor CLI as a one-shot external SubAgent |

External CLI profile setup is covered in [External CLI SubAgents Guide](./external_cli_subagents_guide.md).

DotCraft passes role instructions to external CLIs, but it cannot forcibly filter tools built into those external CLIs. If you need strong isolation, prefer the `native` profile with role allow/deny lists and [Security Configuration](./config/security.md).

## Field Reference

See [Full Configuration Reference](./reference/config.md#subagent) for the complete field table.
