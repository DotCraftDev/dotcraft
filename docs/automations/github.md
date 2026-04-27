# DotCraft GitHub 自动化

GitHub 自动化运行在 Automations 编排器之上，用 GitHub Issue 和 Pull Request 作为任务来源。Issue 适合“让 Agent 完成工作”，PR 适合“让 Agent 审查工作”。

## 快速开始

### 1. 配置 GitHub 来源

在 `.craft/config.json` 中添加：

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

`IssuesWorkflowPath` 可用时派发 Issue Agent；`PullRequestWorkflowPath` 可用时派发 PR Review Agent。两个 workflow 都存在时，两条链路同时运行。

### 2. 放置 Workflow 文件

在工作区根目录放置 `WORKFLOW.md` 和/或 `PR_WORKFLOW.md`。文件由 YAML 前置配置和 Liquid 提示词模板组成：

````markdown
---
tracker:
  active_states: ["Todo", "In Progress"]
  terminal_states: ["Done", "Closed", "Cancelled"]
agent:
  max_turns: 10
  max_concurrent_agents: 2
---
你负责处理 Issue {{ work_item.identifier }}: **{{ work_item.title }}**

{{ work_item.description }}

## 指令

1. 完成 Issue 中描述的任务。
2. 提交并推送你的修改。
3. 完成后调用 `CompleteIssue` 工具，传入简短的完成说明。
````

### 3. 设置 GitHub Token

```bash
# Linux / macOS
export GITHUB_TOKEN=ghp_xxxxxxxxxxxxxxxxxxxx

# Windows PowerShell
$env:GITHUB_TOKEN = "ghp_xxxxxxxxxxxxxxxxxxxx"
```

推荐使用 Fine-grained Personal Access Token，并精确控制仓库范围和权限。

### 4. 启动 DotCraft

```bash
dotcraft
```

## 配置

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `GitHubTracker.Enabled` | 是否启用 GitHub 来源 | `false` |
| `GitHubTracker.IssuesWorkflowPath` | Issue workflow 路径 | `WORKFLOW.md` |
| `GitHubTracker.PullRequestWorkflowPath` | PR workflow 路径 | 空 |
| `GitHubTracker.Tracker.Repository` | GitHub 仓库，格式 `owner/repo` | 空 |
| `GitHubTracker.Tracker.ApiKey` | GitHub Token，支持 `$ENV_VAR` | 空 |
| `GitHubTracker.Agent.MaxTurns` | GitHub Agent 最大轮数 | `10` |
| `GitHubTracker.Agent.MaxConcurrentAgents` | GitHub 来源内部最大并发 Agent 数 | `2` |

## 使用示例

| 场景 | 配置 |
|------|------|
| 只处理 Issue | 设置 `IssuesWorkflowPath` |
| 只审查 PR | 设置 `PullRequestWorkflowPath` |
| Issue 与 PR 同时运行 | 两个 workflow 路径都设置 |
| 多仓库隔离 | 每个工作区配置一个 `Repository` |

## 进阶

### Issue 状态流转

编排器通过 GitHub Label 判断 Issue 是否需要处理。默认映射：

| GitHub Label | 状态 | 含义 |
|---|---|---|
| `status:todo` | Todo（活跃） | 等待处理 |
| `status:in-progress` | In Progress（活跃） | 处理中 |
| Issue 被关闭 | Done（终态） | 任务完成 |

只有状态为活跃的 Issue 会被派发。Agent 调用 `CompleteIssue` 后，Issue 被关闭，下次轮询时不再进入候选列表。

### Pull Request 跟踪

PR 跟踪不需要创建代理 Issue。启用后，编排器通过 GitHub `/pulls` API 轮询 PR，为符合条件的 PR 派发独立的审查 Agent。

| GitHub 条件 | 推导状态 | 默认分类 |
|---|---|---|
| Open PR，无任何评审 | `Pending Review` | 活跃 |
| 存在 review requested | `Review Requested` | 活跃 |
| 最新评审为 `changes_requested` | `Changes Requested` | 活跃 |
| 最新评审为 `approved` | `Approved` | 终态 |
| PR 已合并 | `Merged` | 终态 |
| PR 已关闭（未合并） | `Closed` | 终态 |

### PR 自动重审

编排器记录每个 PR 的 HEAD commit SHA。SHA 不变时跳过；SHA 变化或从未审查时派发 Agent。Agent 调用 `SubmitReview` 后，编排器记录当前 SHA，后续推送会触发新一轮审查。

## 故障排查

### GitHub Issue/PR 没有被轮询到

检查 `GitHubTracker.Enabled`、`Repository`、Token 权限、workflow 路径，以及 Issue Label 或 PR 状态是否属于活跃状态。

### `CompleteIssue` 调用失败

确认 Token 拥有 Issue 写权限，目标 Issue 仍然打开，并且 workflow 中传入的是当前工作项。

### Bot 提交 PR Review 后仍在反复运行

检查 workflow 是否调用 `SubmitReview`，并确认 PR HEAD SHA 记录能够写入工作区状态。

### git clone 失败

确认仓库 URL、Token 权限和运行环境的 Git 配置。私有仓库需要 Token 具备读取内容权限。
