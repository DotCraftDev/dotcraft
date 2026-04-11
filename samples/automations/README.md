# DotCraft Automations Samples

**[中文](./README_ZH.md) | English**

This folder provides ready-to-use templates for the DotCraft **Automations** pipeline. Three scenarios are included:

- [example-local-task](./example-local-task): A **local automation task** — file-based task that runs entirely on-disk without any external service.
- [github-review-bot](./github-review-bot): A **PR review bot** that automatically picks up open, non-draft pull requests, analyzes the diff, and submits a structured `COMMENT` review. Re-reviews automatically when new commits are pushed.
- [github-collab-dev-bot](./github-collab-dev-bot): A **collaborative development bot** that plans, implements, and opens a PR for a given GitHub issue, using labels to coordinate state across runs.

## Architecture

All automation sources — local tasks and GitHub work items — are managed by a single `AutomationOrchestrator`. GitHub scenarios run on top of the Automations orchestrator:

- `Automations` starts the orchestrator that polls all sources and dispatches tasks. It is enabled by default unless you explicitly turn it off.
- `GitHubTracker` registers a `GitHubAutomationSource` that feeds GitHub issues/PRs into the orchestrator.

```text
config.json
├── Automations: { Enabled: true }       ← starts the orchestrator
└── GitHubTracker: { Enabled: true }     ← registers GitHub source
         │
         └─→ AutomationOrchestrator
               ├── LocalAutomationSource    (from Automations module)
               └── GitHubAutomationSource   (from GitHubTracker module)
```

## What is inside

| Path | Purpose |
|------|---------|
| `config.template.json` | Local-only config template (Automations enabled, no GitHub) |
| `example-local-task/task.md` | Local task definition (YAML front matter + Markdown body) |
| `example-local-task/workflow.md` | Local task workflow prompts |
| `github-review-bot/config.template.json` | Config template for PR review (Automations + GitHubTracker) |
| `github-review-bot/PR_WORKFLOW.md` | PR review prompt template |
| `github-collab-dev-bot/config.template.json` | Config template for issue dev (Automations + GitHubTracker) |
| `github-collab-dev-bot/WORKFLOW.md` | Issue development prompt template |

## Prerequisites

- DotCraft installed or built on your machine
- A project directory as the DotCraft workspace (current working directory when you run `dotcraft`)
- For GitHub samples: a [Fine-grained Personal Access Token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens#creating-a-fine-grained-personal-access-token) scoped to the target repository

---

## Local Task Sample

### Quick start

1. Create the workspace config directory: `mkdir -p .craft` (Linux/macOS) or `mkdir .craft` (Windows).
2. Copy `config.template.json` to `.craft/config.json` (or merge the `Automations` block into an existing config).
3. Copy `example-local-task/` into `.craft/tasks/` and rename it to your task id:

```
<your-project>/
  .craft/
    config.json
    tasks/
      my-task-001/
        task.md
        workflow.md
```

4. Edit `task.md` so `id` matches the folder name. Set `title`, timestamps, and description.
5. Run `dotcraft`.

When the agent completes, it calls `CompleteLocalTask` to mark the task done without running until `max_rounds`.

### Configuration

| Field | Meaning |
|-------|---------|
| `Automations.Enabled` | Starts the orchestrator. Default: `true`; only set it when you want to disable Automations explicitly. |
| `Automations.LocalTasksRoot` | Task directory root. Empty = `<workspace>/.craft/tasks/`. |
| `Automations.PollingInterval` | How often sources are polled. Default: `00:00:30`. |
| `Automations.MaxConcurrentTasks` | Concurrent task limit across all sources. |

---

## GitHub Review Bot

### Quick start

```bash
mkdir -p /path/to/workspace/.craft
cp samples/automations/github-review-bot/config.template.json /path/to/workspace/.craft/config.json
cp samples/automations/github-review-bot/PR_WORKFLOW.md       /path/to/workspace/PR_WORKFLOW.md
```

Edit `.craft/config.json`:

| Field | Example | Notes |
|---|---|---|
| `GitHubTracker.Tracker.Repository` | `"your-org/your-repo"` | Format: `owner/repo` |
| `GitHubTracker.Tracker.ApiKey` | `"$GITHUB_TOKEN"` | Leave as-is to use the env var |
| `GitHubTracker.Hooks.BeforeRun` | see file | Update the email/name to your bot identity |

Set the token:

```bash
export GITHUB_TOKEN=ghp_xxxxxxxxxxxxxxxxxxxx
```

Run `dotcraft`. The bot picks up all open, non-draft PRs automatically.

### Lifecycle

```
PR opened (or new commits pushed)
  → Bot dispatched on next poll (SHA differs from last reviewed)
  → Bot reviews diff, calls SubmitReview(summaryJson, commentsJson)
  → Review posted on PR
  → Orchestrator records reviewed SHA
  → Next poll: SHA unchanged → PR skipped

New commits pushed → SHA changes → bot runs again automatically
```

The bot only submits structured `COMMENT` reviews — it never approves, requests changes, or merges.

### Required token permissions

| Permission | Level | Reason |
|---|---|---|
| Metadata | Read-only | Required by GitHub |
| Contents | Read-only | Clone and check out the PR branch |
| Pull requests | Read and Write | Read PR diff, submit review |

---

## GitHub Collab Dev Bot

### Quick start

```bash
mkdir -p /path/to/workspace/.craft
cp samples/automations/github-collab-dev-bot/config.template.json /path/to/workspace/.craft/config.json
cp samples/automations/github-collab-dev-bot/WORKFLOW.md          /path/to/workspace/WORKFLOW.md
```

Edit `.craft/config.json` with your repository and token. Then run `dotcraft`.

### Labels

| Label | Active? | Meaning |
|---|---|---|
| `status:todo` | Yes | New issue, waiting to be started |
| `status:in-progress` | Yes | Bot is implementing |
| `status:awaiting-review` | No | PR opened, waiting for human review |
| `status:blocked` | No | Bot is blocked, needs human input |

The bot manages these labels itself during its run.

### Lifecycle

```
status:todo
  ↓  (bot runs: plan + relabel)
status:in-progress
  ↓  (bot runs: implement + push + open PR + relabel)
status:awaiting-review   ← non-active, bot stops
  ↓  (human merges PR, closes issue)
closed

If blocked at any stage:
status:in-progress  →  status:blocked  ← non-active, bot stops
                        ↓  (human resolves, relabels to status:todo)
                    status:todo  →  ...
```

### Required token permissions

| Permission | Level | Reason |
|---|---|---|
| Metadata | Read-only | Required by GitHub |
| Contents | Read and Write | Clone, create branch, commit, push |
| Issues | Read and Write | Read issue, comment, relabel |
| Pull requests | Read and Write | Open PR, post PR comments |

---

## Troubleshooting

### Tasks never appear (local)

- Confirm `Automations.Enabled` was not explicitly set to `false` in the merged config.
- Confirm the task directory is under the tasks root and contains both `task.md` and `workflow.md`.

### GitHub PRs/issues not being picked up

- Confirm `GitHubTracker.Enabled` is `true`, and `Automations.Enabled` was not explicitly set to `false`.
- For PRs: confirm the PR is open and not a draft; confirm its review state is in `PullRequestActiveStates`.
- For issues: confirm the issue has a label matching an active state (e.g. `status:todo`).
- Confirm the workflow file exists at the configured path (`PR_WORKFLOW.md` or `WORKFLOW.md`).

### Review bot keeps re-running on the same commit

The orchestrator records the reviewed HEAD SHA after each successful review. If the bot re-runs on the same commit, check logs for `ReviewCompleted=true`. If the run exited before calling `SubmitReview`, no SHA is recorded and the bot retries — this is intentional. The reviewed SHA is held in memory; a service restart causes all PRs to be reviewed once.

### `gh: command not found`

Install the [GitHub CLI](https://cli.github.com/) and ensure it is on `PATH`. For the review bot, `gh` is optional — `SubmitReview` uses DotCraft's built-in GitHub API integration.

### Permission errors on `SubmitReview` or `CompleteIssue`

These tools use DotCraft's own GitHub token (`$GITHUB_TOKEN`). Ensure the token has the required permissions listed above.
