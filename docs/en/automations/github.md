# DotCraft GitHub Automations

GitHub automations run on top of the Automations orchestrator and use GitHub Issues and Pull Requests as task sources. Issues are best for "let the Agent do work"; PRs are best for "let the Agent review work."

## Quick Start

### 1. Configure the GitHub Source

Add this to `.craft/config.json`:

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

When `IssuesWorkflowPath` is available, DotCraft dispatches Issue Agents. When `PullRequestWorkflowPath` is available, DotCraft dispatches PR review Agents. If both workflow files exist, both paths run.

### 2. Place Workflow Files

Place `WORKFLOW.md` and/or `PR_WORKFLOW.md` at the workspace root. Each file contains YAML front matter and a Liquid prompt template:

````markdown
---
tracker:
  active_states: ["Todo", "In Progress"]
  terminal_states: ["Done", "Closed", "Cancelled"]
agent:
  max_turns: 10
  max_concurrent_agents: 2
---
You are working on Issue {{ work_item.identifier }}: **{{ work_item.title }}**

{{ work_item.description }}

## Instructions

1. Complete the work described in the Issue.
2. Commit and push your changes.
3. When finished, call `CompleteIssue` with a short completion summary.
````

### 3. Set a GitHub Token

```bash
# Linux / macOS
export GITHUB_TOKEN=ghp_xxxxxxxxxxxxxxxxxxxx

# Windows PowerShell
$env:GITHUB_TOKEN = "ghp_xxxxxxxxxxxxxxxxxxxx"
```

Use a fine-grained personal access token with the narrowest repository scope and permissions that fit your workflow.

### 4. Start DotCraft

```bash
dotcraft gateway
```

## Configuration

| Field | Description | Default |
|-------|-------------|---------|
| `GitHubTracker.Enabled` | Enables the GitHub source | `false` |
| `GitHubTracker.IssuesWorkflowPath` | Issue workflow path | `WORKFLOW.md` |
| `GitHubTracker.PullRequestWorkflowPath` | PR workflow path | Empty |
| `GitHubTracker.Tracker.Repository` | GitHub repository as `owner/repo` | Empty |
| `GitHubTracker.Tracker.ApiKey` | GitHub token, supports `$ENV_VAR` | Empty |
| `GitHubTracker.Agent.MaxTurns` | Maximum GitHub Agent turns | `10` |
| `GitHubTracker.Agent.MaxConcurrentAgents` | Maximum concurrent Agents inside the GitHub source | `2` |

## Usage Examples

| Scenario | Configuration |
|----------|---------------|
| Handle Issues only | Set `IssuesWorkflowPath` |
| Review PRs only | Set `PullRequestWorkflowPath` |
| Run Issues and PRs together | Set both workflow paths |
| Keep repositories isolated | Configure one `Repository` per workspace |

## Advanced Topics

### Issue State Flow

The orchestrator uses GitHub labels to decide which Issues need work. Default mapping:

| GitHub label | State | Meaning |
|---|---|---|
| `status:todo` | Todo (active) | Waiting to be processed |
| `status:in-progress` | In Progress (active) | Being processed |
| Issue closed | Done (terminal) | Work completed |

Only active Issues are dispatched. After the Agent calls `CompleteIssue`, the Issue is closed and no longer appears in later polling.

### Pull Request Tracking

PR tracking does not require proxy Issues. When enabled, the orchestrator polls GitHub's `/pulls` API and dispatches a dedicated review Agent for matching PRs.

| GitHub condition | Derived state | Default class |
|---|---|---|
| Open PR with no reviews | `Pending Review` | Active |
| Review requested | `Review Requested` | Active |
| Latest review is `changes_requested` | `Changes Requested` | Active |
| Latest review is `approved` | `Approved` | Terminal |
| PR merged | `Merged` | Terminal |
| PR closed without merge | `Closed` | Terminal |

### Automatic PR Re-Review

The orchestrator records each PR's HEAD commit SHA. If the SHA is unchanged, it skips the PR. If the SHA changes or has never been reviewed, it dispatches an Agent. After the Agent calls `SubmitReview`, the orchestrator records the current SHA, so later pushes trigger another review.

## Troubleshooting

### GitHub issues or PRs are not picked up

Check `GitHubTracker.Enabled`, `Repository`, token permissions, workflow paths, and whether the Issue label or PR state is active.

### `CompleteIssue` fails

Confirm the token can write Issues, the target Issue is still open, and the workflow is using the current work item.

### The bot keeps re-running after submitting a PR review

Check that the workflow calls `SubmitReview` and that PR HEAD SHA state can be written to the workspace.

### git clone fails

Check the repository URL, token permissions, and Git configuration in the runtime environment. Private repositories require content read permission.
