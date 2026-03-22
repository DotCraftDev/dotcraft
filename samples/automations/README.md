# DotCraft Automations sample (local tasks)

**[中文](./README_ZH.md) | English**

This folder is a **distributable reference** for running DotCraft with **local automation tasks**: a safe `config.template.json` and an **example task directory** you can copy into your own workspace.

It includes:

- `config.template.json` — workspace config with `Automations` enabled and defaults for Dashboard / optional tools
- `example-local-task/` — sample `task.md` and `workflow.md` to copy under your task root (not committed as `.craft/` in this repo)

When a **local** automation task runs, the `local-task` tool profile includes **`CompleteLocalTask`** so the agent can mark the task complete in `task.md` without running until `max_rounds`.

## What is inside

| Path | Purpose |
|------|---------|
| `config.template.json` | Example config; copy into your workspace as `.craft/config.json` (merge fields as needed) |
| `example-local-task/task.md` | Task definition (YAML front matter + Markdown body) |
| `example-local-task/workflow.md` | Workflow prompts for the agent (YAML front matter + Liquid body) |
| `.craft/config.json` | Your **live** workspace config (create locally; usually not committed) |
| `.craft/tasks/<task-id>/` | Where **local** task folders live at runtime (create by copying the example) |

The per-task `workspace/` directory under `.craft/tasks/<task-id>/` may be created by the runtime when a task runs; you do not need to copy it from this sample.

## Prerequisites

- DotCraft installed or built on your machine
- A real **project directory** you use as the DotCraft workspace (current working directory when you run `dotcraft`)
- Gateway / AppServer mode if you rely on Automations services (per your deployment)

## Quick start

### 1. Apply the config template

DotCraft reads global settings from `~/.craft/config.json` (Windows: `%USERPROFILE%\.craft\config.json`) and merges workspace overrides from `<workspace>/.craft/config.json`.

1. Create the workspace config directory if needed: `mkdir -p .craft` (Linux/macOS) or `mkdir .craft` (Windows PowerShell).
2. Copy `config.template.json` to `.craft/config.json` inside **your** project workspace (or merge the `Automations` block and any other fields you need into an existing file).
3. Keep secrets and machine-specific values in the global file when possible.

### 2. Install the example local task

Local tasks are discovered under the **tasks root** configured by Automations (`LocalTasksRoot`). When `LocalTasksRoot` is empty, the default is:

`<workspaceRoot>/.craft/tasks/`

Do **not** rely on this repository shipping `.craft/tasks/` for you. On your machine:

1. Copy the `example-local-task` folder into `.craft/tasks/`.
2. Rename the folder to your task id (for example `my-task-001`).
3. Edit `task.md` front matter so `id` matches that folder name and set `title`, timestamps, and description as needed.
4. Keep `task.md` and `workflow.md` together in the same directory.

Resulting layout:

```
<your-project>/
  .craft/
    config.json
    tasks/
      my-task-001/
        task.md
        workflow.md
        workspace/          # may appear when the task runs
```

## Configuration notes

### Automations fields (in `config.template.json`)

| Field | Meaning |
|-------|---------|
| `Automations.Enabled` | When `true`, enables the Automations module (Gateway channel). |
| `Automations.LocalTasksRoot` | Root directory for task folders. Empty string means use `<workspaceRoot>/.craft/tasks/`. Set to an absolute path to use a custom location. |
| `Automations.PollingInterval` | How often sources are polled. Default in template: 30 seconds (`00:00:30`). |
| `Automations.MaxConcurrentTasks` | Cap on concurrent dispatch across sources. |
| `Automations.WorkspaceRoot` | Root for per-task agent working directories. If omitted from JSON, the built-in default applies (under your user profile). Avoid setting this to an empty string. |

### Other template fields

`DashBoard`, `Tools.Sandbox`, and `McpServers` behave like any other workspace config: adjust host, ports, and enabled flags for your environment.

## Troubleshooting

### Tasks never appear

- Confirm `Automations.Enabled` is `true` in the merged config.
- Confirm the task directory is under the tasks root (default: `.craft/tasks/<task-id>/`) and contains `task.md` and `workflow.md`.

### Wrong tasks directory

- Set `LocalTasksRoot` to an absolute path, or leave it empty and use the default `.craft/tasks/` under your workspace.
