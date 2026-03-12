# DotCraft GitHubTracker Samples

**[中文](./README_ZH.md) | English**

This sample provides two ready-to-use workflow templates for the DotCraft `GitHubTracker` module:

- [review-bot](./review-bot): A **native PR review bot** that automatically picks up open pull requests carrying the `auto-review` label, checks out the PR branch, analyzes the diff, and submits a structured `COMMENT` review — providing feedback only, without approving or blocking.
- [collab-dev-bot](./collab-dev-bot): A **multi-stage collaborative development bot** that plans, implements, and opens a PR for a given issue, using labels to coordinate state across runs.

## How to Use a Sample

Copy the template files into your own DotCraft workspace:

```text
samples/github-tracker/<sample>/
  config.template.json   →  copy to  <your-workspace>/.craft/config.json
  WORKFLOW.md            →  copy to  <your-workspace>/WORKFLOW.md      (collab-dev-bot only)
  PR_WORKFLOW.md         →  copy to  <your-workspace>/PR_WORKFLOW.md   (review-bot only)
```

Then edit `config.json` to fill in your repository, token, and any other settings.

## Quick Start

### 1. Copy Files

**review-bot**:
```bash
mkdir -p /path/to/my-workspace/.craft
cp samples/github-tracker/review-bot/config.template.json /path/to/my-workspace/.craft/config.json
cp samples/github-tracker/review-bot/PR_WORKFLOW.md       /path/to/my-workspace/PR_WORKFLOW.md
```

**collab-dev-bot**:
```bash
mkdir -p /path/to/my-workspace/.craft
cp samples/github-tracker/collab-dev-bot/config.template.json /path/to/my-workspace/.craft/config.json
cp samples/github-tracker/collab-dev-bot/WORKFLOW.md          /path/to/my-workspace/WORKFLOW.md
```

### 2. Set Environment Variables

```bash
# Linux / macOS
export GITHUB_TOKEN=ghp_xxxxxxxxxxxxxxxxxxxx   # used by DotCraft for cloning, PR API calls

# Windows PowerShell
$env:GITHUB_TOKEN = "ghp_xxxxxxxxxxxxxxxxxxxx"
```

> The `submit_review` tool calls the GitHub Review API directly using DotCraft's own token (`$GITHUB_TOKEN`). You only need a second variable if the agent uses `gh` CLI for additional context.

### 3. Edit `config.json`

Replace these values in the copied `config.json`:

| Field | Example | Notes |
|---|---|---|
| `Tracker.Repository` | `"your-org/your-repo"` | Format: `owner/repo` |
| `Tracker.ApiKey` | `"$GITHUB_TOKEN"` | Leave as-is to use the env var |
| `Tracker.PullRequestLabelFilter` | `"auto-review"` | Only review PRs carrying this label. The orchestrator removes it automatically after each review; re-add to trigger another review. |
| `Hooks.BeforeRun` | see file | Update the email/name to your bot identity |

### 4. Open a Pull Request

#### review-bot

The bot picks up any **open, non-draft** pull request that carries the `auto-review` label. After submitting its review, the orchestrator **automatically removes the `auto-review` label**, so the PR is not re-dispatched on the next poll.

**Trigger / exit mechanism**:

1. A human adds the `auto-review` label to a PR.
2. The bot is dispatched on the next poll, reviews the diff, and calls `submit_review` with `COMMENT`.
3. The orchestrator removes the `auto-review` label automatically after the run completes.
4. On the next poll, the PR no longer matches the label filter — claim is released, bot stops.
5. To trigger a re-review (e.g. after pushing fixes), re-add the `auto-review` label.

> The bot only submits `COMMENT` reviews — it never approves, requests changes, or merges. This avoids accidentally triggering auto-merge rules on the repository.

#### collab-dev-bot labels

| Label | Active? | Meaning |
|---|---|---|
| `status:todo` | Yes | New issue, waiting to be started |
| `status:in-progress` | Yes | Bot is implementing |
| `status:awaiting-review` | No | PR opened, waiting for human review |
| `status:blocked` | No | Bot is blocked, needs human input |

The bot manages these labels itself during its run.

### 5. Start DotCraft

```bash
dotcraft
```

---

## Sample Details

### review-bot

**Purpose**: Automated native PR code review (feedback only, no approval or merge).

**Workflow**:
1. DotCraft polls the GitHub `/pulls` API for open, non-draft PRs carrying the `auto-review` label.
2. The PR's head branch is checked out in an isolated workspace.
3. The PR diff is fetched and injected directly into the agent prompt.
4. The agent reads the diff (and optionally inspects files in the workspace), then calls `submit_review` with `COMMENT`.
5. DotCraft submits the review to GitHub via the Reviews API using the configured token.

**Lifecycle**:
```
Human adds `auto-review` label to PR
  → Bot dispatched on next poll
  → Bot reviews diff, calls submit_review COMMENT
  → Review posted on PR
  → Orchestrator auto-removes `auto-review` label
  → Continuation retry fires (1s): PR no longer in candidates → claim released

To trigger a re-review (e.g. after pushing fixes):
  → Re-add `auto-review` label → bot runs again
```

> **Why COMMENT only?** If the bot were to submit `APPROVE` and the repository has auto-merge enabled with branch protection requiring one approval, the bot's approval could inadvertently trigger a merge. Using `COMMENT` eliminates this risk.

**Files**:
- `PR_WORKFLOW.md` — prompt template for PR reviews; receives `{{ issue.diff }}`, `{{ issue.head_branch }}`, `{{ issue.base_branch }}`, etc.
- `config.template.json` — PR-only sample config. It enables PR review by providing `PullRequestWorkflowPath` only.

---

### collab-dev-bot

**Purpose**: Autonomous multi-stage feature development with issue-state coordination.

**Workflow**:
1. A human labels an issue with `status:todo`.
2. The bot is dispatched. It reads the issue, explores the codebase, and posts an implementation plan as a comment.
3. It relabels the issue to `status:in-progress` and begins implementation.
4. It creates a branch (`issue-<N>`), commits changes, pushes, and opens a PR.
5. After opening the PR it relabels the issue to `status:awaiting-review`. The orchestrator stops redispatching.
6. If blocked, it posts a blocker comment and relabels to `status:blocked`.
7. A human resolves the blocker and relabels back to `status:todo` or `status:in-progress` to resume.

**State flow**:
```
status:todo
  ↓  (bot runs: plan + relabel)
status:in-progress
  ↓  (bot runs: implement + push + open PR + relabel)
status:awaiting-review   ← non-active, bot stops retrying
  ↓  (human merges PR, closes issue)
closed

If blocked at any stage:
status:in-progress  →  status:blocked  ← non-active, bot stops retrying
                        ↓  (human resolves, relabels to status:todo)
                    status:todo  →  ...
```

**Why not call `CompleteIssue` immediately after opening the PR?**

`CompleteIssue` closes the GitHub issue. In most workflows, the issue should stay open until the PR is actually merged and verified. Using `status:awaiting-review` keeps the issue visible for discussion while preventing the bot from repeatedly redispatching.

---

## Required GitHub Token Permissions

Use a [Fine-grained Personal Access Token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens#creating-a-fine-grained-personal-access-token) scoped to the specific repository.

### review-bot

| Permission | Level | Reason |
|---|---|---|
| Metadata | Read-only | Required by GitHub, auto-granted |
| Contents | Read-only | Clone the repository and check out the PR branch |
| Pull requests | Read and Write | Read PR diff, list reviews, submit review |
| Issues | Read and Write | Remove the `auto-review` label after each review |

### collab-dev-bot

| Permission | Level | Reason |
|---|---|---|
| Metadata | Read-only | Required by GitHub, auto-granted |
| Contents | Read and Write | Clone, create branch, commit, push |
| Issues | Read and Write | Read issue, comment, relabel |
| Pull requests | Read and Write | Open PR, post PR comments |

---

## Troubleshooting

### `gh: command not found`

Install the [GitHub CLI](https://cli.github.com/) and ensure it is on `PATH` in the environment where DotCraft runs.

For the review-bot, `gh` CLI is optional — `submit_review` uses DotCraft's built-in GitHub API integration. The agent may still invoke `gh` for additional context (e.g. `gh pr diff`, `gh pr checks`).

### Bot keeps re-running after submitting a review

The orchestrator automatically removes the `auto-review` label after a successful review run. If the bot is re-running unexpectedly, check the DotCraft logs for a warning like `"Failed to remove label 'auto-review'"`. This usually means the GitHub token is missing `Issues: Read and Write` permission (label removal uses the same API endpoint as issue label mutations).

The label is **not** removed on failure or timeout, so the bot will retry — this is intentional.

### PR is not being picked up

- Confirm the PR is **open and not a draft**.
- If `Tracker.PullRequestLabelFilter` is set, confirm the PR has that label.
- Check that `PR_WORKFLOW.md` exists in the workspace root.

### `submit_review` reports a permission error

The `submit_review` tool uses DotCraft's own GitHub token (`$GITHUB_TOKEN`). Ensure the token has `Pull requests: Read and Write` permission.

### Issue is not being picked up (collab-dev-bot)

Ensure the issue has a label matching an active state, e.g. `status:todo`. Labels are derived from the `GitHubStateLabelPrefix` setting (`status:` by default).

### Token errors on `CompleteIssue` (collab-dev-bot)

The `CompleteIssue` tool uses DotCraft's own GitHub token (`$GITHUB_TOKEN`). Make sure the token has `Issues: Read and Write` permission.
