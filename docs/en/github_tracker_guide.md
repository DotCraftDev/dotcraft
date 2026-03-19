# DotCraft GitHubTracker Guide

GitHubTracker is DotCraft's autonomous work-item orchestration module. It continuously polls GitHub, automatically creates an isolated workspace and clones the repository for each active **Issue** or **Pull Request**, and dispatches an AI Agent to do the work — Issues are completed when the agent calls `CompleteIssue`, while PRs are completed when the agent calls `SubmitReview` to post a code review.

GitHubTracker is built on the [OpenAI Symphony](https://github.com/openai/symphony) [SPEC.md](https://github.com/openai/symphony/blob/main/SPEC.md) design, and **extends Symphony's original issue-only pipeline with native Pull Request tracking**. Issues and PRs run as two independent, parallel dispatch pipelines that share the same orchestration, workspace, and retry infrastructure.

---

## Quick Start

### Step 1: Enable GitHubTracker in `config.json`

```json
{
  "GitHubTracker": {
    "Enabled": true,
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

### Step 2: Place `WORKFLOW.md` in the workspace root

```
workspace/
└── WORKFLOW.md   ← GitHubTracker reads the issue agent prompt template from here
```

`WORKFLOW.md` consists of two parts: a YAML front matter block (between `---`) and a Liquid prompt template:

```markdown
---
tracker:
  repository: your-org/your-repo
  api_key: $GITHUB_TOKEN
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

## Agent Tools

Each work item type has a dedicated completion tool that the orchestrator injects into the agent's tool set at dispatch time.

### `CompleteIssue` (Issues only)

```
CompleteIssue(reason: string)
```

- Closes the GitHub Issue (removes active-state labels and sets the issue to closed).
- Signals the orchestrator that the work item is done and stops re-dispatch.
- **Call this only after all code changes have been committed and pushed.**

Typical wording in `WORKFLOW.md`:

```
After committing and pushing all code changes, call the `CompleteIssue` tool
with a brief description of what was done.
```

### `SubmitReview` (Pull Requests only)

```
SubmitReview(reviewEvent: string, body: string)
```

- Submits a `COMMENT` review on the PR via the GitHub Reviews API.
- Signals the orchestrator that the review is complete; the orchestrator records the current HEAD SHA so the PR is not re-dispatched until new commits are pushed.
- The `reviewEvent` parameter is accepted for prompt compatibility but is always normalized to `COMMENT`.

> Automated bot reviews always use `COMMENT` to avoid affecting the PR's approval status on GitHub and preventing accidental auto-merge triggers.

> Explicitly instruct the agent to use only `COMMENT` in `PR_WORKFLOW.md` unless you have a clear automated approval/rejection requirement.

---

## Pull Request Tracking

In addition to Issues, GitHubTracker can **track Pull Requests directly** — no proxy issue required. When enabled, the orchestrator polls the GitHub `/pulls` API and dispatches an independent agent to each qualifying PR.

### Enabling PR Tracking

Add the following to `config.json`:

```json
{
  "GitHubTracker": {
    "PullRequestWorkflowPath": "PR_WORKFLOW.md"
  }
}
```

PR tracking activates as soon as the file referenced by `PullRequestWorkflowPath` exists in the workspace root — no additional boolean flag is needed. Issue and PR activation are independent:

- `IssuesWorkflowPath` enables issue dispatch when its workflow file exists.
- `PullRequestWorkflowPath` enables PR review dispatch when its workflow file exists.
- When both workflow files exist, both pipelines run concurrently.

### PR State Derivation

The orchestrator automatically derives a logical PR state from the GitHub Reviews API — **no labels required**:

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

The orchestrator tracks each PR's HEAD commit SHA to automatically trigger re-reviews when new commits are pushed — no labels or manual triggers required.

**End-to-end flow:**

1. On each poll, all open, non-draft PRs in active states are fetched.
2. The orchestrator compares each PR's current `head.sha` to the last reviewed SHA.
3. If SHA matches (already reviewed at this commit) → skip. If SHA differs or is new → dispatch agent.
4. The agent calls `SubmitReview`. The orchestrator records the reviewed SHA and releases the claim.
5. When the author pushes new commits, the SHA changes. On the next poll, the bot automatically re-reviews.
6. When a PR reaches a terminal state (Merged/Closed/Approved), the SHA record is removed.

**Failure behavior:** If the agent exits before calling `SubmitReview` (turns exhausted, timeout), no SHA is recorded and the orchestrator retries — this is intentional.

The reviewed SHA is held in memory only. After a service restart, all open PRs are reviewed once on the first poll.

---

## WORKFLOW.md Reference

### YAML Front Matter Fields

| Field | Description | Default |
|-------|-------------|---------|
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
| `{{ work_item.id }}` | Issue number (numeric) |
| `{{ work_item.identifier }}` | Issue identifier (e.g. `#42`) |
| `{{ work_item.title }}` | Issue title |
| `{{ work_item.description }}` | Issue body |
| `{{ work_item.state }}` | Current state |
| `{{ work_item.url }}` | GitHub URL of the issue |
| `{{ work_item.labels }}` | List of labels |
| `{{ attempt }}` | Current attempt number (starts at 1) |

### PR_WORKFLOW.md Reference

The orchestrator loads the file referenced by `PullRequestWorkflowPath` (default `PR_WORKFLOW.md`) as the prompt template for PR review agents. The format is identical to `WORKFLOW.md` — a YAML front matter block followed by a Liquid template.

#### PR-specific YAML Front Matter Fields

| Field | Description | Default |
|-------|-------------|---------|
| `tracker.pull_request_active_states` | PR states considered active | `["Pending Review", "Review Requested", "Changes Requested"]` |
| `tracker.pull_request_terminal_states` | PR states considered terminal | `["Merged", "Closed", "Approved"]` |
| `agent.max_concurrent_pull_request_agents` | Max concurrent PR agents | `0` (no dedicated limit, shares `max_concurrent_agents`) |

#### PR-specific Liquid Template Variables

In addition to the issue variables above, PR work items expose the following extra variables:

| Variable | Description |
|----------|-------------|
| `{{ work_item.kind }}` | Work item type: `Issue` or `PullRequest` |
| `{{ work_item.head_branch }}` | Source branch of the PR (e.g. `feature/my-branch`) |
| `{{ work_item.base_branch }}` | Target branch of the PR (e.g. `main`) |
| `{{ work_item.diff_url }}` | URL to the raw PR diff |
| `{{ work_item.diff }}` | Full diff content of the PR (automatically fetched and injected on the first turn) |
| `{{ work_item.review_state }}` | Aggregated review decision: `None`, `Pending`, `Approved`, `ChangesRequested` |
| `{{ work_item.is_draft }}` | Whether the PR is in draft mode |

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

> **Note:** Relative paths in `IssuesWorkflowPath` and `PullRequestWorkflowPath` are resolved relative to the DotCraft workspace root (`workspace/`). The defaults map to `workspace/WORKFLOW.md` and `workspace/PR_WORKFLOW.md`.
>
> `MaxConcurrentPullRequestAgents` set to `0` means PR agents share the global `MaxConcurrentAgents` pool. Set it to a positive integer to give PR agents their own dedicated concurrency limit.

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

### Bot keeps re-running after submitting a PR review

**Normal behavior:** After each PR agent successfully calls `SubmitReview`, the orchestrator records the reviewed HEAD SHA. The PR is skipped on subsequent polls until new commits are pushed.

**If it keeps re-running**, confirm that logs show `ReviewSubmitted=true`. If the agent exited before calling `SubmitReview` (turns exhausted, timeout), no SHA is recorded and the orchestrator intentionally retries.

The reviewed SHA is in memory only — a service restart causes all open PRs to be reviewed once on the first poll.

### PR is not being picked up by the poller

**Possible causes:**

1. The PR is in **draft** state — the orchestrator skips all draft PRs automatically.
2. The PR's review state is not in `PullRequestActiveStates` (e.g. it is already `Approved`).
3. The file referenced by `PullRequestWorkflowPath` does not exist in the workspace root (default: `PR_WORKFLOW.md`).

**Fix:** Check each condition above and ensure both the config and the PR state match your expectations.
