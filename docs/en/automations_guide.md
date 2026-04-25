# DotCraft Automations Guide

Automations is DotCraft's task automation pipeline. A unified `AutomationOrchestrator` polls all registered task sources, dispatches an AI Agent for each pending task, and transitions completed tasks into a human review flow.

Two source types are currently supported:

- **Local Tasks** (`LocalAutomationSource`): file-based tasks stored under `.craft/tasks/`, running entirely on-disk without external services.
- **GitHub Tasks** (`GitHubAutomationSource`): polls GitHub Issues and Pull Requests, dispatching an agent for each active work item. Issues are completed when the agent calls `CompleteIssue`; PRs are completed when the agent calls `SubmitReview`.

All sources share the same orchestrator, concurrency control, and session service. GitHub tasks are visible alongside local tasks in the Desktop Automations panel.

```
config.json
├── Automations: { Enabled: true }       ← starts the orchestrator
└── GitHubTracker: { Enabled: true }     ← registers GitHub source (optional)
         │
         └─→ AutomationOrchestrator
               ├── LocalAutomationSource    (from Automations module)
               └── GitHubAutomationSource   (from GitHubTracker module)
```

---

## Cron vs Automation Task

Both Cron and Automation Task fire an agent on a schedule, but their positioning is fundamentally different — don't conflate them:

| Aspect | Cron | Automation Task |
|--------|------|------------------|
| Entry point | Agent tool call `Cron add/list/remove` during a conversation | Desktop `Automations` panel / template gallery / drag-and-drop |
| Granularity | A single message + schedule, fire-and-forget | A full editable `workflow.md` (multi-step prompt / `max_rounds` / hooks) |
| Thread | Each job owns its own `cron:<id>` thread automatically | Isolated thread by default; **can be bound to any existing thread** and run inside it on schedule |
| Lifecycle | Runs once, posts one message | `pending → running → (awaiting_review → approved/rejected) → pending` (auto-rearmed by schedule), with optional human review |
| Visibility | Automations panel · Cron tab, disable/delete only | Automations panel · Tasks tab, full CRUD + review |
| Typical use | "Remind me to check email every day at 9 AM", "Send todo digest every hour" | "Scan recent commits for bugs daily", "Bind a Feishu thread so the agent auto-responds when a new reply lands" |

**How to choose**: reach for Cron for one-liner reminders/notifications. Use an Automation Task when you need a multi-turn agent workflow, periodic deliverables, a human-review gate, or to keep an agent "on call" inside an existing conversation (Feishu / WeCom / QQ / desktop thread).

---

## Quick Start: Local Tasks

### Step 1: Configure Automations

`Automations.Enabled` defaults to `true`. Add this block when you want to tune Automations settings explicitly:

This default does not change the main entry point: bare `dotcraft` still starts the CLI. Use `dotcraft gateway` when you want a dedicated background host for concurrent automations and channels.

Add to `.craft/config.json`:

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

When `LocalTasksRoot` is empty, the default is `<workspace>/.craft/tasks/`.

### Step 2: Create a task

Create a folder named after the task id under the tasks root, containing `task.md` and `workflow.md`:

```
<workspace>/
  .craft/
    config.json
    tasks/
      my-task-001/
        task.md
        workflow.md
```

**task.md** — YAML front matter defines task metadata:

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

Describe what the agent should do. Free-form Markdown.
````

**workflow.md** — YAML front matter + Liquid prompt template:

```markdown
---
max_rounds: 10
---
You are running a local automation workflow.

## Task

- **ID**: {{ task.id }}
- **Title**: {{ task.title }}

## Instructions

{{ task.description }}

When finished, call the **`CompleteLocalTask`** tool with a short summary.
```

### Step 3: Start DotCraft

```bash
dotcraft
```

The orchestrator discovers `pending` tasks and dispatches agents. When the agent calls `CompleteLocalTask`, the task transitions to `awaiting_review` for human review.

---

## Quick Start: GitHub Source

GitHub source runs through the Automations orchestrator. `Automations.Enabled` is `true` by default; GitHub dispatch only stops if you explicitly turn Automations off or leave `GitHubTracker.Enabled` disabled.

### Step 1: Configure

Add to `.craft/config.json`:

```json
{
  "Automations": {
    "Enabled": true,
    "PollingInterval": "00:00:30",
    "MaxConcurrentTasks": 3
  },
  "GitHubTracker": {
    "Enabled": true,
    "IssuesWorkflowPath": "WORKFLOW.md",
    "PullRequestWorkflowPath": "PR_WORKFLOW.md",
    "Tracker": {
      "Repository": "your-org/your-repo",
      "ApiKey": "$GITHUB_TOKEN"
    },
    "Agent": {
      "MaxTurns": 10,
      "MaxConcurrentAgents": 2
    }
  }
}
```

- When `IssuesWorkflowPath` is available, issue agents are dispatched.
- When `PullRequestWorkflowPath` is available, PR review agents are dispatched.
- When both workflow files exist, both pipelines run concurrently.

### Step 2: Place workflow files

Place `WORKFLOW.md` (issues) and/or `PR_WORKFLOW.md` (PRs) in the workspace root. Each file has YAML front matter and a Liquid template:

````markdown
---
tracker:
  active_states: ["Todo", "In Progress"]
  terminal_states: ["Done", "Closed", "Cancelled"]
agent:
  max_turns: 10
  max_concurrent_agents: 2
---
You are assigned to issue {{ work_item.identifier }}: **{{ work_item.title }}**

{{ work_item.description }}

## Instructions

1. Complete the task described in the issue.
2. Commit and push your changes:
   ```
   git add -A && git commit -m "fix: <description> (closes {{ work_item.identifier }})" && git push
   ```
3. When done, call the `CompleteIssue` tool with a brief summary.
````

### Step 3: Set up a GitHub Token

```bash
# Linux / macOS
export GITHUB_TOKEN=ghp_xxxxxxxxxxxxxxxxxxxx

# Windows PowerShell
$env:GITHUB_TOKEN = "ghp_xxxxxxxxxxxxxxxxxxxx"
```

A **Fine-grained Personal Access Token** is recommended for precise scope control.

### Step 4: Start DotCraft

```bash
dotcraft
```

---

## Issue State Flow

The orchestrator determines issue eligibility via GitHub Labels. Default mapping:

| GitHub Label | State | Meaning |
|---|---|---|
| `status:todo` | Todo (active) | Waiting to be processed |
| `status:in-progress` | In Progress (active) | Currently being worked on |
| Issue is closed | Done (terminal) | Task complete |

Only issues in **active** states (`ActiveStates`) are dispatched. After the agent calls `CompleteIssue`, the issue is closed and no longer appears in the candidate list.

---

## Pull Request Tracking

The orchestrator can **track Pull Requests directly** — no proxy issue required. When enabled, it polls the GitHub `/pulls` API and dispatches an agent for each qualifying PR.

### Enabling PR Tracking

Set `PullRequestWorkflowPath` in `config.json` and place the file in the workspace root:

```json
{
  "GitHubTracker": {
    "PullRequestWorkflowPath": "PR_WORKFLOW.md"
  }
}
```

### PR State Derivation

The orchestrator derives logical PR state from the GitHub Reviews API — **no labels required**:

| GitHub Condition | Derived State | Default Classification |
|---|---|---|
| Open PR, no reviews yet | `Pending Review` | Active |
| Review has been requested | `Review Requested` | Active |
| Latest review is `changes_requested` | `Changes Requested` | Active |
| Latest review is `approved` | `Approved` | Terminal |
| PR has been merged | `Merged` | Terminal |
| PR closed without merging | `Closed` | Terminal |

> Active/terminal classification is controlled by `PullRequestActiveStates` and `PullRequestTerminalStates`.

### Automatic Re-Review (HEAD SHA Tracking)

The orchestrator tracks each PR's HEAD commit SHA to trigger re-reviews automatically:

1. On each poll, all open, non-draft PRs in active states are fetched.
2. The current `head.sha` is compared to the last reviewed SHA.
3. If SHA matches → skip. If SHA differs or is new → dispatch agent.
4. The agent calls `SubmitReview`. The orchestrator records the reviewed SHA.
5. When the author pushes new commits, the SHA changes and the bot re-reviews on the next poll.

The reviewed SHA is held in memory only. After a service restart, all open PRs are reviewed once on the first poll. When a PR reaches a terminal state, the SHA record is removed.

---

## Agent Tools

Each source type has dedicated completion tools injected automatically at dispatch time.

### `CompleteLocalTask` (Local tasks only)

```
CompleteLocalTask(summary: string)
```

- Sets `task.md` status to `agent_completed`; the orchestrator detects this and stops the workflow.
- The task then transitions to `awaiting_review` for human review.

### `CompleteIssue` (GitHub Issues only)

```
CompleteIssue(reason: string)
```

- Closes the GitHub Issue (removes active-state labels and sets it to closed).
- Signals the orchestrator that the work item is done.
- **Call only after all code changes have been committed and pushed.**

### `SubmitReview` (GitHub PRs only)

```
SubmitReview(summaryJson: string, commentsJson: string)
```

- Submits a structured `COMMENT` review on the PR via the GitHub Reviews API.
- `summaryJson` includes review summary fields such as major/minor/suggestion counts and body.
- `commentsJson` includes inline review comments (with optional suggestion replacements) anchored to PR diff lines.
- Signals the orchestrator that the review is complete when the summary is posted; reviewed HEAD SHA is then recorded.

> Automated bot reviews always use `COMMENT` to avoid affecting the PR's approval status on GitHub.

---

## Workflow Reference

### Local Task Liquid Template Variables

| Variable | Description |
|----------|-------------|
| `{{ task.id }}` | Task ID |
| `{{ task.title }}` | Task title |
| `{{ task.description }}` | Task description (Markdown body of task.md) |
| `{{ task.workspace_path }}` | Agent working directory path |

### GitHub Workflow YAML Front Matter Fields

| Field | Description | Default |
|-------|-------------|---------|
| `tracker.repository` | Repository in `owner/repo` format | Required |
| `tracker.api_key` | GitHub Token; supports `$ENV_VAR` | Required |
| `tracker.active_states` | Issue active states | `["Todo", "In Progress"]` |
| `tracker.terminal_states` | Issue terminal states | `["Done", "Closed", "Cancelled"]` |
| `tracker.github_state_label_prefix` | Label prefix for state inference | `status:` |
| `tracker.assignee_filter` | Only process issues assigned to this user | Empty |
| `tracker.pull_request_active_states` | PR active states | `["Pending Review", "Review Requested", "Changes Requested"]` |
| `tracker.pull_request_terminal_states` | PR terminal states | `["Merged", "Closed", "Approved"]` |
| `agent.max_turns` | Max turns per dispatch | `20` |
| `agent.max_concurrent_agents` | Max concurrent agents | `3` |
| `agent.max_concurrent_pull_request_agents` | Max concurrent PR agents | `0` (shared) |
| `polling.interval_ms` | Polling interval in milliseconds | `30000` |

### GitHub Liquid Template Variables

GitHub workflows support both `work_item.*` and `task.*` aliases pointing to the same data.

| Variable | Description |
|----------|-------------|
| `{{ work_item.id }}` / `{{ task.id }}` | Issue/PR number |
| `{{ work_item.identifier }}` / `{{ task.identifier }}` | Identifier (e.g. `#42`) |
| `{{ work_item.title }}` / `{{ task.title }}` | Title |
| `{{ work_item.description }}` / `{{ task.description }}` | Body |
| `{{ work_item.state }}` / `{{ task.state }}` | Current state |
| `{{ work_item.url }}` / `{{ task.url }}` | GitHub URL |
| `{{ work_item.labels }}` / `{{ task.labels }}` | Label list |
| `{{ attempt }}` | Current attempt number |

#### PR-specific Variables

| Variable | Description |
|----------|-------------|
| `{{ work_item.kind }}` | Work item type: `Issue` or `PullRequest` |
| `{{ work_item.head_branch }}` | PR source branch |
| `{{ work_item.base_branch }}` | PR target branch |
| `{{ work_item.diff }}` | Full PR diff content |
| `{{ work_item.diff_url }}` | PR diff URL |
| `{{ work_item.review_state }}` | Review state |
| `{{ work_item.is_draft }}` | Whether the PR is a draft |

---

## Workspace Directory Structure

### Local Tasks

```
<workspace>/
  .craft/
    tasks/
      my-task-001/
        task.md              ← Task definition
        workflow.md           ← Workflow prompt
        workspace/            ← Agent working directory (created at runtime)
```

### GitHub Work Items

Each issue/PR gets its own workspace with automatic `git clone`:

```
{workspace_root}/
└── github/
    └── {task_id}/           ← e.g. 42 (for #42)
        ├── .craft/          ← Agent sessions, memory, config
        ├── <git clone>      ← Repository files
        └── ...
```

The GitHub workspace root is configured via `Automations.WorkspaceRoot`. `GitHubTracker.Workspace.Root` configures the tracker clone workspace.

---

## Full Configuration Reference

```json
{
  "Automations": {
    "Enabled": true,
    "LocalTasksRoot": "",
    "PollingInterval": "00:00:30",
    "MaxConcurrentTasks": 3
  },
  "GitHubTracker": {
    "Enabled": true,
    "IssuesWorkflowPath": "WORKFLOW.md",
    "PullRequestWorkflowPath": "PR_WORKFLOW.md",
    "Tracker": {
      "Repository": "your-org/your-repo",
      "ApiKey": "$GITHUB_TOKEN",
      "ActiveStates": ["Todo", "In Progress"],
      "TerminalStates": ["Done", "Closed", "Cancelled"],
      "GitHubStateLabelPrefix": "status:",
      "AssigneeFilter": "",
      "PullRequestActiveStates": ["Pending Review", "Review Requested", "Changes Requested"],
      "PullRequestTerminalStates": ["Merged", "Closed", "Approved"]
    },
    "Polling": {
      "IntervalMs": 30000
    },
    "Workspace": {
      "Root": ""
    },
    "Agent": {
      "MaxConcurrentAgents": 3,
      "MaxConcurrentPullRequestAgents": 0,
      "MaxTurns": 20,
      "MaxRetryBackoffMs": 300000,
      "TurnTimeoutMs": 3600000,
      "StallTimeoutMs": 300000
    },
    "Hooks": {
      "AfterCreate": "",
      "BeforeRun": "",
      "AfterRun": "",
      "BeforeRemove": "",
      "TimeoutMs": 60000
    }
  }
}
```

> `Automations.Enabled` starts the orchestrator and defaults to `true`. `GitHubTracker.Enabled` registers the GitHub source and remains `false` by default. Bare `dotcraft` still launches the CLI; use `dotcraft gateway` for a dedicated concurrent host.
>
> Relative paths in `IssuesWorkflowPath` and `PullRequestWorkflowPath` are resolved relative to the workspace root.
>
> `MaxConcurrentPullRequestAgents` set to `0` means PR agents share the global `MaxConcurrentAgents` pool.

---

## Hooks Integration

GitHubTracker workspace lifecycle supports the following hook events:

| Event | When It Fires |
|-------|--------------|
| `after_create` | After the workspace is first created (after clone) |
| `before_run` | Before each agent execution |
| `after_run` | After each agent execution (success or failure) |
| `before_remove` | Before the workspace is cleaned up |

Example:

```json
{
  "GitHubTracker": {
    "Hooks": {
      "BeforeRun": "npm install --silent"
    }
  }
}
```

---

## GitHub Token Permissions

| Scenario | Permission | Level |
|----------|-----------|-------|
| PR review | Metadata | Read-only |
| PR review | Contents | Read-only |
| PR review | Pull requests | Read and Write |
| Issue development | Metadata | Read-only |
| Issue development | Contents | Read and Write |
| Issue development | Issues | Read and Write |
| Issue development + PR | Pull requests | Read and Write |

A [Fine-grained Personal Access Token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens#creating-a-fine-grained-personal-access-token) scoped to the target repository is recommended.

---

## Troubleshooting

### Local tasks never appear

- Confirm `Automations.Enabled` has not been explicitly set to `false`.
- Confirm the task directory is under the tasks root (default `.craft/tasks/<task-id>/`) and contains both `task.md` and `workflow.md`.
- Confirm `task.md` has `status: pending`.

### GitHub issues/PRs not being picked up

- Confirm `GitHubTracker.Enabled` is `true`, and `Automations.Enabled` has not been explicitly set to `false`.
- Issues: confirm the issue has a label matching an active state (e.g. `status:todo`).
- PRs: confirm the PR is open and not a draft, and its review state is in `PullRequestActiveStates`.
- Confirm the workflow file exists at the configured path.

### Agent keeps running but the issue is never closed

Make sure `WORKFLOW.md` explicitly instructs the agent to call `CompleteIssue`.

### `CompleteIssue` call fails

The token lacks `Issues: Write` permission. Regenerate with `Issues: Read and Write`.

### Bot keeps re-running after submitting a PR review

Check logs for `ReviewCompleted=true`. If the agent exited before calling `SubmitReview` (turns exhausted, timeout), no SHA is recorded and the orchestrator retries intentionally.

### git clone fails

Check token permissions (`Contents: Read` required) and network connectivity.

---

## Sample Templates

Complete config templates and workflow files are available in [Automations Samples](./samples/automations.md):

- `example-local-task/` — local task example
- `github-review-bot/` — PR review bot
- `github-collab-dev-bot/` — collaborative development bot
