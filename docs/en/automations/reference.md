# DotCraft Automations Reference

This page collects the configuration, task files, template variables, and tools for DotCraft native Automations. Automations now handles local workspace tasks only; GitHub Issues, Pull Requests, review rounds, and dispatch are owned by Oratorio.

## Configuration Fields

| Field | Description | Default |
|-------|-------------|---------|
| `Automations.Enabled` | Enables the Automations orchestrator | `true` |
| `Automations.LocalTasksRoot` | Local task root. Empty uses `.craft/tasks/` | Empty |
| `Automations.PollingInterval` | Polling interval | `00:00:30` |
| `Automations.MaxConcurrentTasks` | Maximum concurrent local tasks | `3` |
| `Automations.TurnTimeout` | Single-turn timeout | `00:30:00` |
| `Automations.StallTimeout` | Stall timeout without response | `00:10:00` |
| `Automations.MaxRetries` | Maximum retry count | `3` |
| `Automations.RetryInitialDelay` | Initial retry delay | `00:00:30` |
| `Automations.RetryMaxDelay` | Maximum retry delay | `00:10:00` |

## Agent Tool

### `CompleteLocalTask`

The local task completion tool. The Agent calls it after finishing the work so the task can record its summary and complete.

| Parameter | Description |
|-----------|-------------|
| `summary` | Short completion summary |

## Workflow Template Variables

| Variable | Description |
|----------|-------------|
| `task.id` | Task id |
| `task.title` | Task title |
| `task.description` | `task.md` body |
| `task.thread_id` | Bound thread id |

## Directory Layout

```text
<workspace>/
  .craft/
    tasks/
      <task-id>/
        task.md
        workflow.md
    automations/
      templates/
        <template-id>/
          template.md
```

## Hooks Integration

Automations can run Hooks during the local task lifecycle, for example to check the environment before a task starts, send a notification after completion, or write failures to an external system. See [Hooks Reference](../hooks/reference.md) for event payloads and exit-code semantics.
