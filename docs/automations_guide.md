# DotCraft Automations 指南

Automations 是 DotCraft 的自动化任务管线。它通过统一的 `AutomationOrchestrator` 轮询所有注册的任务来源（Source），为每个待处理的任务派发 AI Agent，并在完成后进入人工审核流程。

目前支持两类任务来源：

- **本地任务**（`LocalAutomationSource`）：基于文件的任务，存放在 `.craft/tasks/` 目录下，完全在本地运行。
- **GitHub 任务**（`GitHubAutomationSource`）：轮询 GitHub Issue 和 Pull Request，为每个活跃的工作项派发 Agent。Issue 由 Agent 调用 `CompleteIssue` 标记完成，PR 由 Agent 调用 `SubmitReview` 提交审查意见。

所有任务来源共享同一个编排器、并发控制和会话服务。GitHub 任务在 Desktop 应用的 Automations 面板中与本地任务一样可见。

```
config.json
├── Automations: { Enabled: true }       ← 启动编排器
└── GitHubTracker: { Enabled: true }     ← 注册 GitHub 来源（可选）
         │
         └─→ AutomationOrchestrator
               ├── LocalAutomationSource    (来自 Automations 模块)
               └── GitHubAutomationSource   (来自 GitHubTracker 模块)
```

---

## 快速开始：本地任务

### 第一步：配置 Automations

`Automations.Enabled` 默认就是 `true`。下面这段配置主要用于你想显式调整 Automations 参数时：

这个默认值不会改变主入口：直接运行 `dotcraft` 仍然进入 CLI；如果你想使用专门承载并发 Automations / Channel 的后台宿主，请使用 `dotcraft gateway`。

在 `.craft/config.json` 中添加：

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

`LocalTasksRoot` 为空时默认使用 `<workspace>/.craft/tasks/`。

### 第二步：创建任务

在任务根目录下创建一个以任务 id 命名的文件夹，包含 `task.md` 和 `workflow.md`：

```
<workspace>/
  .craft/
    config.json
    tasks/
      my-task-001/
        task.md
        workflow.md
```

**task.md** — YAML front matter 定义任务元数据：

```markdown
---
id: "my-task-001"
title: "实现功能 X"
status: pending
created_at: "2026-03-22T10:00:00Z"
updated_at: "2026-03-22T10:00:00Z"
thread_id: null
agent_summary: null
---

在这里描述 Agent 需要完成的任务。支持 Markdown 格式。
```

**workflow.md** — YAML front matter + Liquid 提示词模板：

```markdown
---
max_rounds: 10
---
你正在执行一个本地自动化任务。

## 任务

- **ID**: {{ task.id }}
- **标题**: {{ task.title }}

## 指令

{{ task.description }}

完成后，调用 **`CompleteLocalTask`** 工具并传入简短的完成说明。
```

### 第三步：启动 DotCraft

```bash
dotcraft
```

编排器会自动发现 `pending` 状态的任务并派发 Agent。Agent 完成后调用 `CompleteLocalTask`，任务进入 `awaiting_review` 状态等待人工审核。

---

## 快速开始：GitHub 来源

GitHub 来源运行在 Automations 编排器之上。`Automations.Enabled` 默认就是 `true`；只有你显式关闭 Automations，或者没有启用 `GitHubTracker.Enabled` 时，GitHub 任务才不会被调度。

### 第一步：配置

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

- `IssuesWorkflowPath` 可用时，派发 Issue Agent。
- `PullRequestWorkflowPath` 可用时，派发 PR Review Agent。
- 两个 workflow 都存在时，两条链路同时运行。

### 第二步：放置 Workflow 文件

在工作区根目录放置 `WORKFLOW.md`（Issue）和/或 `PR_WORKFLOW.md`（PR）。文件由 YAML 前置配置和 Liquid 提示词模板组成：

```markdown
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
2. 提交并推送你的修改：
   ```
   git add -A && git commit -m "fix: <描述> (closes {{ work_item.identifier }})" && git push
   ```
3. 完成后调用 `CompleteIssue` 工具，传入简短的完成说明。
```

### 第三步：设置 GitHub Token

```bash
# Linux / macOS
export GITHUB_TOKEN=ghp_xxxxxxxxxxxxxxxxxxxx

# Windows PowerShell
$env:GITHUB_TOKEN = "ghp_xxxxxxxxxxxxxxxxxxxx"
```

推荐使用 **Fine-grained Personal Access Token**，精确控制仓库范围和权限。

### 第四步：启动 DotCraft

```bash
dotcraft
```

---

## Issue 状态流转

编排器通过 GitHub Label 判断 Issue 是否需要处理。默认映射：

| GitHub Label | 状态 | 含义 |
|---|---|---|
| `status:todo` | Todo（活跃） | 等待处理 |
| `status:in-progress` | In Progress（活跃） | 处理中 |
| Issue 被关闭 | Done（终态） | 任务完成 |

只有状态为 **活跃**（`ActiveStates`）的 Issue 才会被派发。Agent 调用 `CompleteIssue` 后，Issue 被关闭，下次轮询时不再出现在候选列表中。

---

## Pull Request 跟踪

编排器可以**直接跟踪 Pull Request**——无需创建代理 Issue。启用后通过 GitHub `/pulls` API 轮询 PR，为每个符合条件的 PR 派发独立的 Agent 进行代码审查。

### 启用方式

在 `config.json` 中设置 `PullRequestWorkflowPath`，并在工作区根目录放置对应文件：

```json
{
  "GitHubTracker": {
    "PullRequestWorkflowPath": "PR_WORKFLOW.md"
  }
}
```

### PR 状态推导

编排器通过 GitHub Reviews API 自动推导 PR 的逻辑状态（**不依赖 Label**）：

| GitHub 条件 | 推导状态 | 默认分类 |
|---|---|---|
| Open PR，无任何评审 | `Pending Review` | 活跃 |
| 存在 review requested | `Review Requested` | 活跃 |
| 最新评审为 `changes_requested` | `Changes Requested` | 活跃 |
| 最新评审为 `approved` | `Approved` | 终态 |
| PR 已合并 | `Merged` | 终态 |
| PR 已关闭（未合并） | `Closed` | 终态 |

> PR 的活跃 / 终态划分通过 `PullRequestActiveStates` 和 `PullRequestTerminalStates` 配置。

### PR 自动重审（HEAD SHA 追踪）

编排器通过追踪每个 PR 的 HEAD commit SHA 实现自动重审：

1. 每次轮询拉取所有 open、非 draft、处于活跃状态的 PR。
2. 比较当前 `head.sha` 与上次审查时记录的 SHA。
3. 若 SHA 不变，跳过；若 SHA 变化（或从未审查），派发 Agent。
4. Agent 调用 `SubmitReview` 完成评审，编排器记录当前 SHA。
5. 开发者推送新提交后，下次轮询检测到 SHA 变化，自动触发新一轮审查。

SHA 记录仅保存在内存中；服务重启后所有 open PR 会在首次轮询时重新审查一次。PR 进入终态时 SHA 记录被清除。

---

## Agent 工具

每种任务来源对应不同的完成工具，由编排器在派发时自动注入 Agent 的工具集。

### `CompleteLocalTask`（本地任务专用）

```
CompleteLocalTask(summary: string)
```

- 将 `task.md` 的状态设为 `agent_completed`，编排器检测后停止工作流。
- 任务随后进入 `awaiting_review` 状态，等待人工审核。

### `CompleteIssue`（GitHub Issue 专用）

```
CompleteIssue(reason: string)
```

- 在 GitHub 上关闭该 Issue（移除活跃状态 Label 并设为 closed）。
- 通知编排器该工作项已完成，停止续派。
- **务必在所有代码变更提交并推送后再调用**。

### `SubmitReview`（GitHub PR 专用）

```
SubmitReview(summaryJson: string, commentsJson: string)
```

- 通过 GitHub Reviews API 在 PR 上提交结构化 `COMMENT` 审查。
- `summaryJson` 包含审查摘要（如 major/minor/suggestion 计数和 body）。
- `commentsJson` 包含锚定到 PR diff 行的 inline comments（可选 suggestionReplacement）。
- 当 summary 成功提交后通知编排器审查完成并记录当前 HEAD SHA；下次推送新提交时自动触发重新审查。

> 自动化 Bot 审查始终使用 `COMMENT`，不会影响 PR 在 GitHub 上的 Approve/Reject 状态。

---

## Workflow 参考

### 本地任务 Liquid 模板变量

| 变量 | 说明 |
|------|------|
| `{{ task.id }}` | 任务 ID |
| `{{ task.title }}` | 任务标题 |
| `{{ task.description }}` | 任务描述（task.md 的 Markdown 正文） |
| `{{ task.workspace_path }}` | Agent 工作目录路径 |

### GitHub 工作流 YAML 前置字段

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `tracker.repository` | 仓库，格式 `owner/repo` | 必填 |
| `tracker.api_key` | GitHub Token，支持 `$ENV_VAR` 语法 | 必填 |
| `tracker.active_states` | Issue 活跃状态列表 | `["Todo", "In Progress"]` |
| `tracker.terminal_states` | Issue 终态列表 | `["Done", "Closed", "Cancelled"]` |
| `tracker.github_state_label_prefix` | Label 前缀 | `status:` |
| `tracker.assignee_filter` | 只处理指定用户的 Issue | 空 |
| `tracker.pull_request_active_states` | PR 活跃状态列表 | `["Pending Review", "Review Requested", "Changes Requested"]` |
| `tracker.pull_request_terminal_states` | PR 终态列表 | `["Merged", "Closed", "Approved"]` |
| `agent.max_turns` | 每次派发最多执行几轮 | `20` |
| `agent.max_concurrent_agents` | 最多并发 Agent 数 | `3` |
| `agent.max_concurrent_pull_request_agents` | PR Agent 最大并发数 | `0`（共享） |
| `polling.interval_ms` | 轮询间隔（毫秒） | `30000` |

### GitHub Liquid 模板变量

GitHub 工作流同时支持 `work_item.*` 和 `task.*` 两组别名，指向相同数据。

| 变量 | 说明 |
|------|------|
| `{{ work_item.id }}` / `{{ task.id }}` | Issue/PR 编号 |
| `{{ work_item.identifier }}` / `{{ task.identifier }}` | 标识符（如 `#42`） |
| `{{ work_item.title }}` / `{{ task.title }}` | 标题 |
| `{{ work_item.description }}` / `{{ task.description }}` | 正文 |
| `{{ work_item.state }}` / `{{ task.state }}` | 当前状态 |
| `{{ work_item.url }}` / `{{ task.url }}` | GitHub URL |
| `{{ work_item.labels }}` / `{{ task.labels }}` | 标签列表 |
| `{{ attempt }}` | 当前尝试次数 |

#### PR 专用变量

| 变量 | 说明 |
|------|------|
| `{{ work_item.kind }}` | 工作项类型：`Issue` 或 `PullRequest` |
| `{{ work_item.head_branch }}` | PR 源分支 |
| `{{ work_item.base_branch }}` | PR 目标分支 |
| `{{ work_item.diff }}` | PR 完整 diff 内容 |
| `{{ work_item.diff_url }}` | PR diff URL |
| `{{ work_item.review_state }}` | 审查状态 |
| `{{ work_item.is_draft }}` | 是否为 draft |

---

## 工作区目录结构

### 本地任务

```
<workspace>/
  .craft/
    tasks/
      my-task-001/
        task.md              ← 任务定义
        workflow.md           ← 工作流提示词
        workspace/            ← Agent 工作目录（运行时创建）
```

### GitHub 工作项

每个 Issue/PR 有独立的工作区，编排器自动执行 `git clone`：

```
{workspace_root}/
└── github/
    └── {task_id}/           ← 如 42（对应 #42）
        ├── .craft/          ← Agent 会话、记忆、配置
        ├── <git clone 内容>  ← 仓库文件
        └── ...
```

GitHub 工作区的 `workspace_root` 通过 `Automations.WorkspaceRoot` 配置。`GitHubTracker.Workspace.Root` 用于旧版克隆工作区。

---

## 完整配置参考

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

> `Automations.Enabled` 用于启动编排器，且默认值为 `true`。`GitHubTracker.Enabled` 用于注册 GitHub 来源，且默认值仍为 `false`。直接运行 `dotcraft` 仍然启动 CLI；如需专门的并发宿主，请使用 `dotcraft gateway`。
>
> `IssuesWorkflowPath` 和 `PullRequestWorkflowPath` 的相对路径以工作区根目录为基准解析。
>
> `MaxConcurrentPullRequestAgents` 为 `0` 表示 PR Agent 与 Issue Agent 共享 `MaxConcurrentAgents` 并发槽。

---

## Hooks 集成

GitHubTracker 的工作区生命周期支持以下 Hook 事件：

| 事件 | 触发时机 |
|------|---------|
| `after_create` | 首次创建工作区后（克隆完成后） |
| `before_run` | 每次 Agent 开始执行前 |
| `after_run` | 每次 Agent 执行结束后 |
| `before_remove` | 清理工作区前 |

示例：

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

## GitHub Token 权限

| 场景 | 权限 | 级别 |
|------|------|------|
| PR 审查 | Metadata | Read-only |
| PR 审查 | Contents | Read-only |
| PR 审查 | Pull requests | Read and Write |
| Issue 开发 | Metadata | Read-only |
| Issue 开发 | Contents | Read and Write |
| Issue 开发 | Issues | Read and Write |
| Issue 开发 + PR | Pull requests | Read and Write |

推荐使用 [Fine-grained Personal Access Token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens#creating-a-fine-grained-personal-access-token)，精确控制仓库范围。

---

## 常见问题

### 本地任务未出现

- 确认 `Automations.Enabled` 没有被显式设置为 `false`。
- 确认任务目录位于任务根下（默认 `.craft/tasks/<task-id>/`），且包含 `task.md` 和 `workflow.md`。
- 确认 `task.md` 的 `status` 为 `pending`。

### GitHub Issue/PR 没有被轮询到

- 确认 `GitHubTracker.Enabled` 为 `true`，并且 `Automations.Enabled` 没有被显式设置为 `false`。
- Issue：确认 Issue 有匹配活跃状态的 Label（如 `status:todo`）。
- PR：确认 PR 为 open 且非 draft，审查状态在 `PullRequestActiveStates` 中。
- 确认工作流文件存在于配置路径。

### Agent 反复执行但 Issue 没有关闭

确保 `WORKFLOW.md` 明确要求 Agent 调用 `CompleteIssue`。

### `CompleteIssue` 调用失败

Token 缺少 `Issues: Write` 权限，重新生成并赋予 `Issues: Read and Write`。

### Bot 提交 PR Review 后仍在反复运行

检查日志中 `ReviewCompleted` 是否为 `true`。如果 Agent 在 turns 耗尽前未调用 `SubmitReview`，编排器会继续重试。

### git clone 失败

检查 Token 权限（需要 `Contents: Read`）和网络连接。

---

## 示例模板

完整的配置模板和工作流文件见 [samples/automations/](../samples/automations/)，包含：

- `example-local-task/` — 本地任务示例
- `github-review-bot/` — PR 审查机器人
- `github-collab-dev-bot/` — 协作开发机器人
