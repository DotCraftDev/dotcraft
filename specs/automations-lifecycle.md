# DotCraft Automations Lifecycle

DotCraft native Automations is a local-task orchestrator. It watches local task files in the current workspace, dispatches Agents for runnable tasks, preserves scheduling and thread binding, and records completion through `CompleteLocalTask`.

GitHub issue/PR orchestration, review rounds, comments, PR review drafts, GitHub writes, and long-running dispatch are owned by Oratorio. DotCraft native Automations does not read removed built-in GitHub config, workflow files, old review files, or old GitHub automation state.

## Scope

- Local task files under `.craft/tasks/` or `Automations.LocalTasksRoot`.
- Local schedule initialization and re-arm.
- Manual run.
- Thread binding.
- Local templates under `.craft/automations/templates/` or `Automations.UserTemplatesRoot`.
- Task deletion.
- Activity notifications via `automation/task/updated`.
- Completion via `CompleteLocalTask`.

## Task Identity

Tasks are addressed by `taskId` only. The AppServer automation task methods do not accept or return source identifiers.

## AppServer Surface

| Method | Params | Result |
|--------|--------|--------|
| `automation/task/list` | `{}` | `{ tasks: AutomationTaskWire[] }` |
| `automation/task/read` | `{ taskId }` | `{ task: AutomationTaskWire }` |
| `automation/task/create` | `{ title, description, workflowTemplate?, approvalPolicy?, workspaceMode?, schedule?, threadBinding?, templateId? }` | `{ task: AutomationTaskWire }` |
| `automation/task/run` | `{ taskId }` | `{ task: AutomationTaskWire }` |
| `automation/task/updateBinding` | `{ taskId, threadBinding?: AutomationThreadBindingWire | null }` | `{ task: AutomationTaskWire }` |
| `automation/task/delete` | `{ taskId }` | `{ ok: true }` |
| `automation/template/list` | `{ locale? }` | `{ templates: AutomationTemplateWire[] }` |
| `automation/template/save` | `{ id?, title, description?, icon?, category?, workflowMarkdown, defaultSchedule?, defaultWorkspaceMode?, defaultApprovalPolicy?, needsThreadBinding?, defaultTitle?, defaultDescription? }` | `{ template: AutomationTemplateWire }` |
| `automation/template/delete` | `{ id }` | `{ ok: true }` |

## `AutomationTaskWire`

```json
{
  "id": "weekly-report",
  "title": "Weekly report",
  "description": "Summarize recent work",
  "status": "pending",
  "threadId": null,
  "approvalPolicy": "workspaceScope",
  "createdAt": "2026-05-05T00:00:00Z",
  "updatedAt": "2026-05-05T00:00:00Z",
  "schedule": null,
  "threadBinding": null,
  "nextRunAt": null
}
```

`status` is one of `pending`, `running`, `completed`, or `failed`.

## Local Task Files

```text
<workspace>/
  .craft/
    tasks/
      <task-id>/
        task.md
        workflow.md
```

The local file store owns parsing and persistence. `task.md` contains task metadata and description. `workflow.md` is the Agent workflow prompt. Templates copy their workflow body into new local tasks.

## Dispatch

1. The orchestrator polls local task files.
2. Runnable tasks are keyed by `taskId`.
3. Scheduled tasks initialize `nextRunAt`; recurring schedules re-arm after a run.
4. Bound tasks submit into the bound thread when it is active and available.
5. Unbound tasks create or resume the task conversation.
6. The local task tool profile is registered with `CompleteLocalTask`.
7. Completion writes the Agent summary and emits `automation/task/updated`.

## No Migration Or Fallback

Removed GitHub automation state and config is intentionally ignored. There is no compatibility branch for old source-qualified payloads, built-in GitHub settings, GitHub workflow files, or previous review state.
