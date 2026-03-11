# DotCraft GitHubTracker Samples

**[中文](./README_ZH.md) | English**

This sample provides two ready-to-use `WORKFLOW.md` templates for the DotCraft `GitHubTracker` module:

- [review-bot](./review-bot): An **issue-driven PR review bot** that reads a review request issue, fetches the referenced PR diff, posts structured code review feedback, and closes the request issue.
- [collab-dev-bot](./collab-dev-bot): A **multi-stage collaborative development bot** that plans, implements, and opens a PR for a given issue, using labels to coordinate state across runs.

## How to Use a Sample

Copy the template files into your own DotCraft workspace:

```text
samples/github-tracker/<sample>/
  config.template.json   →  copy to  <your-workspace>/.craft/config.json
  WORKFLOW.md            →  copy to  <your-workspace>/WORKFLOW.md
```

Then edit `config.json` to fill in your repository, token, and any other settings.

## Quick Start

### 1. Copy Files

**review-bot**:
```bash
mkdir -p /path/to/my-workspace/.craft
cp samples/github-tracker/review-bot/config.template.json /path/to/my-workspace/.craft/config.json
cp samples/github-tracker/review-bot/WORKFLOW.md          /path/to/my-workspace/WORKFLOW.md
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
export GITHUB_TOKEN=ghp_xxxxxxxxxxxxxxxxxxxx   # used by DotCraft for cloning and issue ops
export GH_TOKEN=$GITHUB_TOKEN                  # used by gh CLI in shell commands

# Windows PowerShell
$env:GITHUB_TOKEN = "ghp_xxxxxxxxxxxxxxxxxxxx"
$env:GH_TOKEN     = $env:GITHUB_TOKEN
```

> **Why two variables?**
> DotCraft's tracker reads `$GITHUB_TOKEN` for its own GitHub API calls (clone, issue state, close). The `gh` CLI that the agent runs in shell commands reads `$GH_TOKEN`. They usually point to the same token.

### 3. Edit `config.json`

Replace these values in the copied `config.json`:

| Field | Example | Notes |
|---|---|---|
| `Tracker.Repository` | `"your-org/your-repo"` | Format: `owner/repo` |
| `Tracker.ApiKey` | `"$GITHUB_TOKEN"` | Leave as-is to use the env var |
| `Hooks.BeforeRun` | see file | Update the email/name to match your bot identity |

### 4. Label Your Issues

#### review-bot labels

| Label | Meaning |
|---|---|
| `status:todo` | New review request, will be dispatched |

The issue body must contain the PR number to review and the review scope.

**Example issue body**:
```
Please review PR #42.

Focus on error handling and the new authentication middleware.
```

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

**Purpose**: Automates code review for pull requests.

**Workflow**:
1. A human opens a GitHub issue with label `status:todo`, writing the PR number and review scope in the issue body.
2. The bot is dispatched to the issue.
3. It fetches the PR diff using `gh pr diff`.
4. It analyzes the changes and posts a structured review comment on the PR via `gh pr comment` or `gh api`.
5. It calls `complete_issue` to close the review-request issue.

**Limitation**: `GitHubTracker` currently dispatches only GitHub _issues_, not PRs directly. PRs with the `pull_request` field set are skipped at the discovery stage. The review-bot works around this by using a proxy issue as the trigger. If a future version of DotCraft adds PR-dispatch support, the workflow file can be adapted to remove the proxy issue step.

**State flow**:
```
status:todo  →  (bot runs, posts review)  →  issue closed
```

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

**Why not call `complete_issue` immediately after opening the PR?**

`complete_issue` closes the GitHub issue. In most workflows, the issue should stay open until the PR is actually merged and verified. Using `status:awaiting-review` keeps the issue visible for discussion while preventing the bot from repeatedly redispatching.

---

## Required GitHub Token Permissions

Use a [Fine-grained Personal Access Token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens#creating-a-fine-grained-personal-access-token) scoped to the specific repository.

### review-bot

| Permission | Level | Reason |
|---|---|---|
| Metadata | Read-only | Required by GitHub, auto-granted |
| Contents | Read-only | Clone the repository and read files |
| Issues | Read and Write | Read issue body, post comments, close issue |
| Pull requests | Read and Write | Read PR diff, post review comments |

### collab-dev-bot

| Permission | Level | Reason |
|---|---|---|
| Metadata | Read-only | Required by GitHub, auto-granted |
| Contents | Read and Write | Clone, create branch, commit, push |
| Issues | Read and Write | Read issue, comment, relabel |
| Pull requests | Read and Write | Open PR, post PR comments |

> `gh auth login` inside the `before_run` hook authenticates `gh` CLI. This requires that `$GH_TOKEN` is set in the environment before starting DotCraft.

---

## Troubleshooting

### `gh: command not found`

Install the [GitHub CLI](https://cli.github.com/) and ensure it is on `PATH` in the environment where DotCraft runs.

### `gh auth login` fails in the hook

The `before_run` hook uses `gh auth login --with-token <<< "$GH_TOKEN"`. On Windows, here-string syntax differs. Use an alternative:

```powershell
echo $env:GH_TOKEN | gh auth login --with-token
```

Update the `Hooks.BeforeRun` field in your `config.json` accordingly.

### Bot keeps re-running after posting the review or opening the PR

This happens when the issue label was not changed to a non-active state. The orchestrator retries while the issue is in an `active_states` label. Ensure the workflow's relabeling step (`gh issue edit`) executed successfully.

### Issue is not being picked up

Ensure the issue has a label matching an active state, e.g. `status:todo`. Labels are derived from the `GitHubStateLabelPrefix` setting (`status:` by default).

### Token errors on `complete_issue`

The `complete_issue` tool uses DotCraft's own GitHub token (`$GITHUB_TOKEN`). Make sure the token has `Issues: Read and Write` permission.
