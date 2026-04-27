# Agent Skill Self-Learning

DotCraft allows the agent to turn successful workflows into workspace skills by default. When enabled, the agent receives one aggregated `SkillManage(action, ...)` tool for creating, updating, patching, and maintaining skill files in the current workspace.

This capability is enabled by default. When disabled, `SkillManage` is not registered in the model context and the built-in `skill-authoring` workflow skill is not loaded.

## Enable

Set the workspace configuration:

```json
{
  "Skills": {
    "SelfLearning": {
      "Enabled": true,
      "AllowDelete": false
    }
  }
}
```

Configuration changes require a new session or host restart because the tool list and system prompt are fixed when the agent is created.

## Settings

| Setting | Default | Description |
|---|---:|---|
| `Skills.SelfLearning.Enabled` | `true` | Master switch, enabled by default. When disabled, `SkillManage` is not exposed and `skill-authoring` is not loaded. |
| `Skills.SelfLearning.AllowDelete` | `false` | Enables `SkillManage(action: "delete")`. Deletion is destructive and must be enabled separately. |
| `Skills.SelfLearning.MaxSkillContentChars` | `100000` | Maximum size of one `SKILL.md`, in characters. |
| `Skills.SelfLearning.MaxSupportingFileBytes` | `1048576` | Maximum size of one supporting file, in bytes. |

## Agent Tools

When enabled, the agent sees a single tool:

```text
SkillManage(action, name, content?, oldString?, newString?, filePath?, fileContent?, replaceAll?)
```

Action reference:

| Action | Required parameters | Purpose |
|---|---|---|
| `create` | `name`, `content` | Create a new workspace skill. |
| `patch` | `name`, `oldString`, `newString` | Patch `SKILL.md` or a supporting file. |
| `edit` | `name`, `content` | Replace an existing workspace skill's `SKILL.md`. |
| `write_file` | `name`, `filePath`, `fileContent` | Write a supporting file. |
| `remove_file` | `name`, `filePath` | Remove a supporting file. |
| `delete` | `name` | Delete a workspace skill, only when `AllowDelete=true`. |

## Built-In Workflow Skill

When self-learning is enabled, DotCraft injects lightweight self-learning guidance whenever `SkillManage` is available, describing when to create or patch workspace skills. The built-in `skill-authoring` skill appears in the skills summary as an on-demand authoring reference for `SKILL.md` frontmatter, action selection, supporting-file directory rules, common pitfalls, and verification guidance.

`SkillManage` triggers DotCraft approval (`kind: skill`) before `create` and `delete`, consistent with file and Shell approvals. `edit` / `patch` / `write_file` / `remove_file` do not require approval.

`skill-authoring` declares `tools: SkillManage`, so when self-learning is disabled and `SkillManage` is unavailable, it does not appear in the available skills list.

## Boundaries

Self-learning tools only write to the current workspace skill directory. Built-in skills and user-global skills are read-only; if they need changes, the agent should create a new workspace skill copy.

Supporting files can only be written under:

- `scripts/`
- `assets/`

The tools reject absolute paths and `..` path traversal.

## Good Skill Candidates

- A complex task produced a reusable workflow.
- A tricky error was fixed and may recur.
- The user corrected the agent and the correction became a stable procedure.
- An existing skill was used and found stale, incomplete, or missing pitfalls.

Simple one-off answers should not be saved as skills.
