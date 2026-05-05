# Automations Guide

DotCraft native Automations covers local tasks only. It reads task files from `.craft/tasks/` in the current workspace, runs Agents manually or on a schedule, and keeps thread binding, templates, retries, activity display, and the `CompleteLocalTask` completion path.

## Create a Local Task

A local task lives under `.craft/tasks/<task-id>/`:

```text
.craft/
  tasks/
    weekly-report/
      task.md
      workflow.md
```

`task.md` describes the title, body, schedule, and thread binding. `workflow.md` describes the Agent workflow prompt.

## Common Capabilities

| Capability | Description |
|------------|-------------|
| Manual run | Desktop Automations panel or AppServer `automation/task/run` |
| Scheduled run | Configure `schedule` in the task file |
| Thread binding | Bind a task to an existing thread so future runs submit there |
| Templates | Save reusable task templates under `.craft/automations/templates/` |
| Completion tool | Agent calls `CompleteLocalTask` with a summary |
| Delete task | Delete the task folder, optionally with the linked thread |

## AppServer Methods

| Method | Description |
|--------|-------------|
| `automation/task/list` | List local tasks |
| `automation/task/read` | Read one local task |
| `automation/task/create` | Create a local task |
| `automation/task/run` | Run a local task immediately |
| `automation/task/updateBinding` | Update or clear thread binding |
| `automation/task/delete` | Delete a local task |
| `automation/template/list` | List templates |
| `automation/template/save` | Save a user template |
| `automation/template/delete` | Delete a user template |

See [Automations Reference](./automations/reference.md) for field details.
