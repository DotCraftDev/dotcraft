# DotCraft Automations Guide

Automations is DotCraft's automation task pipeline. It polls task sources, dispatches Agents for pending work, and moves completed work into a reviewable state flow. Start with a local task, then add [GitHub Automations](./automations/github.md) when the local path is clear.

DotCraft supports two task source types:

- **Local tasks**: file-based tasks under `.craft/tasks/`, fully local.
- **GitHub tasks**: GitHub Issue and Pull Request polling for active work items.

All task sources share the orchestrator, concurrency control, session service, and Desktop Automations panel.

## Quick Start

### 1. Configure Automations

`Automations.Enabled` is enabled by default. Use this configuration when you want explicit polling and concurrency settings:

```json
{
  "Automations": {
    "Enabled": true,
    "LocalTasksRoot": "",
    "PollingInterval": "00:00:30",
    "MaxConcurrentTasks": 3
  }
}
```

When `LocalTasksRoot` is empty, DotCraft uses `<workspace>/.craft/tasks/`.

### 2. Create a Local Task

Create a task-id folder under the task root with `task.md` and `workflow.md`:

```text
<workspace>/
  .craft/
    tasks/
      my-task-001/
        task.md
        workflow.md
```

`task.md` describes the task:

````markdown
---
id: "my-task-001"
title: "Implement feature X"
status: pending
created_at: "2026-03-22T10:00:00Z"
updated_at: "2026-03-22T10:00:00Z"
thread_id: null
agent_summary: null
---

Describe what the Agent should do here. Markdown is supported.
````

`workflow.md` describes how the Agent should run:

```markdown
---
max_rounds: 10
---
You are running a local automation task.

## Task

- ID: {{ task.id }}
- Title: {{ task.title }}

## Instructions

{{ task.description }}

When finished, call `CompleteLocalTask` with a short completion summary.
```

### 3. Start DotCraft

For automation scenarios, prepare the workspace with the explicit non-interactive setup command:

```bash
dotcraft setup --language English --model <model> --endpoint <endpoint> --api-key <key> --profile developer
```

After setup, start Gateway:

```bash
dotcraft gateway
```

The orchestrator discovers `pending` tasks and dispatches Agents. After the Agent calls `CompleteLocalTask`, the task enters `awaiting_review` for human review.

### Command-Line One-Shot Tasks

Use `dotcraft exec` when a script or CI job needs to call the Agent directly:

```bash
dotcraft exec "Check the latest commit and summarize risks"
```

You can also read the prompt from stdin for pipeline-friendly workflows:

```bash
git diff --stat | dotcraft exec -
```

Put remote AppServer connection flags after `exec`:

```bash
dotcraft exec --remote ws://server:9100/ws --token my-secret "Summarize current task status"
```

`dotcraft exec` runs one input and exits. stdout contains only the final answer; connection details, progress, and errors go to stderr. It exits `0` on success and non-zero for configuration errors, model errors, declined approval requests, or cancellation.

## Configuration

| Field | Description | Default |
|-------|-------------|---------|
| `Automations.Enabled` | Enables the Automations orchestrator | `true` |
| `Automations.LocalTasksRoot` | Local task root. Empty uses `.craft/tasks/` | Empty |
| `Automations.PollingInterval` | Polling interval | `00:00:30` |
| `Automations.MaxConcurrentTasks` | Maximum concurrent tasks across sources | `3` |

For every field, template variable, and Agent tool, see [Automations Reference](./automations/reference.md).

## Usage Examples

| Scenario | Choose |
|----------|--------|
| Remind me to check email every day at 9 AM | Cron |
| Send a todo digest every hour | Cron |
| Scan recent commits for bugs every day | Automation Task |
| Attach an Agent to an existing chat thread | Automation Task |
| Automate GitHub Issue handling or PR review | [GitHub Automations](./automations/github.md) |

## Advanced Topics

### Cron vs Automation Task

| Dimension | Cron | Automation Task |
|-----------|------|------------------|
| Entry point | Agent calls `Cron add/list/remove` in chat | Desktop Automations panel, templates, or task files |
| Unit | One message + schedule | Editable `workflow.md` |
| Thread | Each job owns a `cron:<id>` thread | Independent by default, can bind to an existing thread |
| Lifecycle | Runs once and sends one message | `pending -> running -> awaiting_review -> approved/rejected` |
| Typical use | Reminders, notifications, light reports | Multi-turn workflows, periodic outputs, human review |

### Workspace Layout

```text
<workspace>/
  .craft/
    tasks/
      <task-id>/
        task.md
        workflow.md
```

Task state, execution summary, and review result are stored in the task files. The Desktop Automations panel reads the same state.

## Troubleshooting

### Local tasks never appear

Confirm `Automations.Enabled = true`, the task is under `LocalTasksRoot`, and `task.md` has `status: pending`.

### The Agent finishes but the task still waits

Confirm the workflow explicitly tells the Agent to call `CompleteLocalTask`. Without the completion tool call, the task does not enter review.

### You want GitHub Issue or PR automation

Continue with [GitHub Automations](./automations/github.md).
