# DotCraft GitHubTracker Guide

GitHubTracker is DotCraft's autonomous issue orchestration module. It continuously polls issue trackers like GitHub, automatically creates an isolated workspace and clones the repository for each active issue, dispatches an AI Agent to complete the coding task, and finally calls the `CompleteIssue` tool to close the issue and signal the orchestrator to stop retrying.

Inspired by [OpenAI Symphony](https://github.com/openai/symphony). The core implementation follows its [SPEC.md](https://github.com/openai/symphony/blob/main/SPEC.md).

---

## Quick Start

### Step 1: Enable GitHubTracker in `config.json`

```json
{
  "GitHubTracker": {
    "Enabled": true,
    "Tracker": {
      "Kind": "github",
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

### Step 2: Place `WORKFLOW.md` in the workspace root

```
workspace/
└── WORKFLOW.md   ← GitHubTracker reads the agent prompt template from here
```

`WORKFLOW.md` consists of two parts: a YAML front matter block (between `---`) and a Liquid prompt template:

```markdown
---
tracker:
  kind: github
  repository: your-org/your-repo
  api_key: $GITHUB_TOKEN
agent:
  max_turns: 10
  max_concurrent_agents: 2
---
You are assigned to issue {{ issue.identifier }}: **{{ issue.title }}**

{{ issue.description }}

## Instructions

1. Complete the task described in the issue.
2. Commit and push your changes:
   ```
   git add -A && git commit -m "fix: <description> (closes {{ issue.identifier }})" && git push
   ```
3. When done, call the `CompleteIssue` tool with a brief summary of what you did.
```

### Step 3: Set up a GitHub Token

```bash
# Linux / macOS
export GITHUB_TOKEN=ghp_xxxxxxxxxxxxxxxxxxxx

# Windows PowerShell
$env:GITHUB_TOKEN = "ghp_xxxxxxxxxxxxxxxxxxxx"
```

Required token permissions:

| Permission | Minimum Required |
|------------|-----------------|
| Issues | **Read and Write** (write is required to close issues) |
| Contents | **Read and Write** (required for cloning private repos and pushing code) |
| Metadata | Read-only (granted automatically) |

> A **Fine-grained Personal Access Token** is recommended for precise control over repository scope and permissions.

### Step 4: Start DotCraft

```bash
dotcraft
```

You should see logs like:

```
[Startup] Using module: gateway
  Configuring sub-module services: github-tracker
[GitHubTracker] Orchestrator started, poll interval: 30000ms
[GitHubTracker] Fetched N candidate issues from GitHub
[GitHubTracker] Dispatching agent for #1: Hello World
```

---

## Issue State Flow

GitHubTracker determines whether an issue needs processing via GitHub Labels. Default mapping:

| GitHub Label | GitHubTracker State | Meaning |
|---|---|---|
| `status:todo` | Todo (active) | Waiting to be processed |
| `status:in-progress` | In Progress (active) | Currently being worked on |
| Issue is closed | Done (terminal) | Task complete |

Only issues in **active** states (`ActiveStates`) are dispatched. After the agent calls `CompleteIssue`, GitHubTracker closes the issue. On the next poll tick, the issue no longer appears in the candidate list and the orchestrator stops retrying.

---

## WORKFLOW.md Reference

### YAML Front Matter Fields

| Field | Description | Default |
|-------|-------------|---------|
| `tracker.kind` | Tracker type — currently only `github` is supported | `github` |
| `tracker.repository` | Repository in `owner/repo` format | Required |
| `tracker.api_key` | GitHub Token; supports `$ENV_VAR` syntax | Required |
| `tracker.active_states` | States considered active | `["Todo", "In Progress"]` |
| `tracker.terminal_states` | States considered terminal | `["Done", "Closed", "Cancelled"]` |
| `tracker.github_state_label_prefix` | Label prefix used to infer state from labels | `status:` |
| `tracker.assignee_filter` | Only process issues assigned to this user | Empty (all issues) |
| `agent.max_turns` | Maximum turns per dispatch | `20` |
| `agent.max_concurrent_agents` | Maximum concurrent agents | `3` |
| `polling.interval_ms` | Polling interval in milliseconds | `30000` |

### Liquid Template Variables

| Variable | Description |
|----------|-------------|
| `{{ issue.id }}` | Issue number (numeric) |
| `{{ issue.identifier }}` | Issue identifier (e.g. `#42`) |
| `{{ issue.title }}` | Issue title |
| `{{ issue.description }}` | Issue body |
| `{{ issue.state }}` | Current state |
| `{{ issue.url }}` | GitHub URL of the issue |
| `{{ issue.labels }}` | List of labels |
| `{{ attempt }}` | Current attempt number (starts at 1) |

---

## Workspace Directory Structure

Each issue gets its own workspace. GitHubTracker automatically runs `git clone` inside it:

```
{workspace_root}/
└── {sanitized_identifier}/      ← e.g. _42 (for #42)
    ├── .craft/                  ← Agent sessions, memory, config
    ├── <git clone contents>     ← Repository files
    └── ...
```

The default `workspace_root` is `github_tracker_workspaces` under the system temp directory. Override it in config:

```json
{
  "GitHubTracker": {
    "Workspace": {
      "Root": "~/git-workspaces"
    }
  }
}
```

Supports `~` (home directory) and `$VAR` (environment variable) expansion.

---

## Full Configuration Reference

All GitHubTracker options can be set in `.craft/config.json` (or global config):

```json
{
  "GitHubTracker": {
    "Enabled": true,
    "WorkflowPath": "WORKFLOW.md",
    "Tracker": {
      "Kind": "github",
      "Repository": "your-org/your-repo",
      "ApiKey": "$GITHUB_TOKEN",
      "ActiveStates": ["Todo", "In Progress"],
      "TerminalStates": ["Done", "Closed", "Cancelled"],
      "GitHubStateLabelPrefix": "status:",
      "AssigneeFilter": ""
    },
    "Polling": {
      "IntervalMs": 30000
    },
    "Workspace": {
      "Root": ""
    },
    "Agent": {
      "MaxConcurrentAgents": 3,
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

> **Note:** Relative paths in `WorkflowPath` are resolved relative to the DotCraft workspace root (`workspace/`). The default value `"WORKFLOW.md"` resolves to `workspace/WORKFLOW.md`.

---

## Hooks Integration

GitHubTracker's workspace lifecycle is integrated with the DotCraft Hooks system and supports the following events:

| Event | When It Fires |
|-------|--------------|
| `after_create` | After the workspace is first created (after clone completes) |
| `before_run` | Before each agent execution begins |
| `after_run` | After each agent execution ends (success or failure) |
| `before_remove` | Before the workspace is cleaned up |

Example — automatically install dependencies before each agent run:

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

## Troubleshooting

### Agent keeps running but the issue is never closed

**Cause:** The agent is not calling the `CompleteIssue` tool.

**Fix:** Make sure the `WORKFLOW.md` prompt explicitly instructs the agent to call `CompleteIssue` after all work is done. Example wording:

```
After committing and pushing all code changes, call the `CompleteIssue` tool
with a brief description of what was done.
```

### Issues are not being picked up by the poller

**Cause:** The issues do not have a label matching the active states.

**Fix:** Add the `status:todo` or `status:in-progress` label to the issue (or adjust to match your `GitHubStateLabelPrefix` configuration).

### git clone fails

**Cause:** The token lacks `Contents: Read` permission, or there is a network issue.

**Fix:** Check token permissions and ensure `$GITHUB_TOKEN` is set correctly. When a clone fails, the agent still runs but the workspace will be empty.

### `CompleteIssue` call fails

**Cause:** The token lacks `Issues: Write` permission.

**Fix:** Regenerate the token with `Issues: Read and Write` permission.
