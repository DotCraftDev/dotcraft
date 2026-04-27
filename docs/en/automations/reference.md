# DotCraft Automations Reference

This page collects Automations fields, template variables, Agent tools, directory layout, and Hooks integration. For the beginner path, read [Automations Guide](../automations_guide.md). For GitHub workflows, read [GitHub Automations](./github.md).

## Configuration Fields

| Field | Description | Default |
|-------|-------------|---------|
| `Automations.Enabled` | Enables the Automations orchestrator | `true` |
| `Automations.LocalTasksRoot` | Local task root. Empty uses `.craft/tasks/` | Empty |
| `Automations.WorkspaceRoot` | Task workspace root. Empty uses system temp | Empty |
| `Automations.PollingInterval` | Polling interval | `00:00:30` |
| `Automations.MaxConcurrentTasks` | Maximum concurrent tasks across sources | `3` |
| `Automations.TurnTimeout` | Single-turn timeout | `00:30:00` |
| `Automations.StallTimeout` | Stall timeout without response | `00:10:00` |
| `Automations.MaxRetries` | Maximum retry count | `3` |
| `Automations.RetryInitialDelay` | Initial retry delay | `00:00:30` |
| `Automations.RetryMaxDelay` | Maximum retry delay | `00:10:00` |

## Agent Tools

### `CompleteLocalTask`

Local task completion tool. The Agent calls it when work is finished, and the task enters `awaiting_review`.

| Parameter | Description |
|-----------|-------------|
| `summary` | Short completion summary |

### `CompleteIssue`

GitHub Issue completion tool. The Agent calls it when work is finished; the orchestrator closes the corresponding Issue and records the summary.

| Parameter | Description |
|-----------|-------------|
| `summary` | Short completion summary |

### `SubmitReview`

GitHub PR review tool. The Agent calls it after review; the orchestrator submits the review and records the current HEAD SHA.

| Parameter | Description |
|-----------|-------------|
| `body` | Review body |
| `event` | Review event, such as `COMMENT`, `APPROVE`, or `REQUEST_CHANGES` |

## Workflow Templates

### Local Task Variables

| Variable | Description |
|----------|-------------|
| `task.id` | Task id |
| `task.title` | Task title |
| `task.description` | Body of `task.md` |
| `task.thread_id` | Bound thread id |

### GitHub Variables

| Variable | Description |
|----------|-------------|
| `work_item.identifier` | Issue or PR identifier |
| `work_item.title` | Title |
| `work_item.description` | Body description |
| `work_item.url` | GitHub URL |
| `work_item.repository` | Repository name |

### GitHub YAML Front Matter Fields

| Field | Description |
|-------|-------------|
| `tracker.active_states` | States that are dispatched |
| `tracker.terminal_states` | Terminal states |
| `agent.max_turns` | Maximum turns for one Agent |
| `agent.max_concurrent_agents` | Maximum concurrent Agents inside the GitHub source |

## Directory Layout

### Local Tasks

```text
<workspace>/
  .craft/
    tasks/
      <task-id>/
        task.md
        workflow.md
```

### GitHub Work Items

GitHub work-item state is stored under workspace `.craft/` and maintained by GitHubTracker. Users usually maintain only `WORKFLOW.md`, `PR_WORKFLOW.md`, and `.craft/config.json`.

## Hooks Integration

Automations can use Hooks to run scripts during task lifecycle events, such as checking the environment before a task starts, sending notifications after completion, or writing failures to an external system. For events, input payloads, and exit-code semantics, see [Hooks Reference](../hooks/reference.md).

## GitHub Token Permissions

| Capability | Required permission |
|------------|---------------------|
| Read Issues / PRs | Repository contents and issues / pull requests read |
| Close Issues | Issues write |
| Submit PR reviews | Pull requests write |
| Clone private repositories | Contents read |

When using a fine-grained personal access token, grant only the target repository.
