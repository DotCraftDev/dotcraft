# DotCraft GitHubTracker 指南

GitHubTracker 是 DotCraft 的自主工作项编排模块。它持续轮询 GitHub，为每个活跃的 **Issue** 或 **Pull Request** 自动创建独立工作区并克隆仓库，派发 AI Agent 完成任务——Issue 由 Agent 调用 `CompleteIssue` 工具标记完成，PR 则由 Agent 调用 `SubmitReview` 工具提交代码审查意见。

GitHubTracker 以 [OpenAI Symphony](https://github.com/openai/symphony) 的 [SPEC.md](https://github.com/openai/symphony/blob/main/SPEC.md) 为基础，并在其原生 Issue 跟踪能力之上**扩展了原生 PR 跟踪支持**。Issue 与 PR 作为两条并行、独立配置的任务链路，共享同一套编排、工作区和重试机制。

---

## 快速开始

### 第一步：在 `config.json` 中启用 GitHubTracker

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

### 第二步：在工作区根目录放置 `WORKFLOW.md`

```
workspace/
└── WORKFLOW.md   ← GitHubTracker 默认从这里读取 Issue Agent 提示词
```

`WORKFLOW.md` 文件由两部分组成：YAML 前置配置（`---` 之间）和 Liquid 提示词模板：

```markdown
---
tracker:
  repository: your-org/your-repo
  api_key: $GITHUB_TOKEN
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

Token 所需权限：

| 权限 | 最低要求 |
|------|---------|
| Issues | **Read and Write**（关闭 Issue 需要写权限） |
| Contents | **Read and Write**（克隆私有仓库、推送代码需要写权限） |
| Metadata | Read-only（自动授予） |

> 推荐使用 **Fine-grained Personal Access Token**，精确控制仓库范围和权限。

### 第四步：启动 DotCraft

```bash
dotcraft
```

日志中会出现：

```
[Startup] Using module: gateway
  Configuring sub-module services: github-tracker
[GitHubTracker] Orchestrator started, poll interval: 30000ms
[GitHubTracker] Fetched N candidate issues from GitHub
[GitHubTracker] Dispatching agent for #1: Hello World
```

---

## Issue 状态流转

GitHubTracker 通过 GitHub Label 判断 Issue 是否需要处理。默认映射：

| GitHub Label | GitHubTracker 状态 | 含义 |
|---|---|---|
| `status:todo` | Todo（活跃） | 等待处理 |
| `status:in-progress` | In Progress（活跃） | 处理中 |
| Issue 被关闭 | Done（终态） | 任务完成 |

只有状态为 **活跃**（`ActiveStates`）的 Issue 才会被派发。Agent 调用 `CompleteIssue` 后，GitHubTracker 会关闭该 Issue，下次轮询时该 Issue 将不再出现在候选列表中，编排器停止重试。

---

## Agent 工具

每个工作项类型对应一个专属的完成工具，由编排器在派发时自动注入 Agent 的工具集：

### `CompleteIssue`（Issue 专用）

```
CompleteIssue(reason: string)
```

- 在 GitHub 上关闭该 Issue（移除活跃状态 Label 并将 Issue 设为 closed）。
- 通知编排器该工作项已完成，停止续派。
- **务必在所有代码变更提交并推送后再调用**，调用后 Issue 将立即关闭。

在 `WORKFLOW.md` 中的典型指令：

```
完成所有代码变更并推送后，调用 `CompleteIssue` 工具，传入你做了什么的简短说明。
```

### `SubmitReview`（PR 专用）

```
SubmitReview(reviewEvent: string, body: string)
```

- 通过 GitHub Reviews API 在 PR 上提交一条 `COMMENT` 类型的 Review。
- 通知编排器 Review 已完成，编排器记录当前 HEAD SHA 并释放 Claim；下次推送新提交时会自动触发重新审查。
- `reviewEvent` 参数保留以兼容 Prompt，但始终以 `COMMENT` 提交，不受传入值影响。

> 自动化 Bot 审查始终使用 `COMMENT`，不会影响 PR 在 GitHub 上的 Approve/Reject 状态，保留人工代码审查流程。

---

## Pull Request 跟踪

除了 Issue，GitHubTracker 还可以**直接跟踪 Pull Request**——无需创建代理 Issue。启用后，编排器会通过 GitHub `/pulls` API 轮询 PR，为每个符合条件的 PR 派发独立的 Agent 进行代码审查。

### 启用方式

在 `config.json` 中设置以下字段：

```json
{
  "GitHubTracker": {
    "PullRequestWorkflowPath": "PR_WORKFLOW.md"
  }
}
```

只要工作区根目录存在 `PR_WORKFLOW.md` 文件，PR 审查就会启用；不再需要额外的布尔开关。Issue 与 PR 的启用彼此独立：

- `IssuesWorkflowPath` 可用时，派发 Issue Agent。
- `PullRequestWorkflowPath` 可用时，派发 PR Review Agent。
- 两个 workflow 都存在时，两条链路都会运行。

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

编排器通过追踪每个 PR 的 HEAD commit SHA 实现自动重审，无需人工干预：

**完整流程**：

1. 每次轮询拉取所有 open、非 draft、处于活跃状态的 PR。
2. 比较当前 `head.sha` 与上次审查时记录的 SHA。
3. 若 SHA 不变，跳过（已在此提交审查过）；若 SHA 变化（或从未审查），派发 Agent。
4. Agent 调用 `SubmitReview` 完成评审，编排器记录当前 SHA 并释放 Claim。
5. 开发者推送新提交后，下次轮询检测到 SHA 变化，自动触发新一轮审查。

SHA 记录仅保存在内存中；服务重启后所有 open PR 会在首次轮询时重新审查一次，这是预期行为。PR 进入终态（Merged/Closed/Approved）时，对应的 SHA 记录会被清除。

---

## WORKFLOW.md 参考

### YAML 前置配置字段

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `tracker.repository` | 仓库，格式 `owner/repo` | 必填 |
| `tracker.api_key` | GitHub Token，支持 `$ENV_VAR` 语法 | 必填 |
| `tracker.active_states` | 认为活跃的状态列表 | `["Todo", "In Progress"]` |
| `tracker.terminal_states` | 认为终态的状态列表 | `["Done", "Closed", "Cancelled"]` |
| `tracker.github_state_label_prefix` | Label 前缀，用于从 Label 推断状态 | `status:` |
| `tracker.assignee_filter` | 只处理分配给指定用户的 Issue | 空（处理所有） |
| `agent.max_turns` | 每次派发最多执行几轮 | `20` |
| `agent.max_concurrent_agents` | 最多并发运行多少个 Agent | `3` |
| `polling.interval_ms` | 轮询间隔（毫秒） | `30000` |

### Liquid 模板变量

| 变量 | 说明 |
|------|------|
| `{{ work_item.id }}` | Issue 编号（纯数字） |
| `{{ work_item.identifier }}` | Issue 标识符（如 `#42`） |
| `{{ work_item.title }}` | Issue 标题 |
| `{{ work_item.description }}` | Issue 正文 |
| `{{ work_item.state }}` | 当前状态 |
| `{{ work_item.url }}` | Issue 的 GitHub URL |
| `{{ work_item.labels }}` | 标签列表 |
| `{{ attempt }}` | 当前是第几次尝试（从 1 开始） |

### PR_WORKFLOW.md 参考

编排器会加载 `PullRequestWorkflowPath` 指定的文件（默认 `PR_WORKFLOW.md`）作为 PR 审查 Agent 的提示词。格式与 `WORKFLOW.md` 相同，由 YAML 前置配置和 Liquid 模板组成。

#### PR 专用 YAML 前置字段

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `tracker.pull_request_active_states` | PR 活跃状态列表 | `["Pending Review", "Review Requested", "Changes Requested"]` |
| `tracker.pull_request_terminal_states` | PR 终态列表 | `["Merged", "Closed", "Approved"]` |
| `agent.max_concurrent_pull_request_agents` | PR Agent 最大并发数 | `0`（不限制，共享 `max_concurrent_agents`） |

#### PR 专用 Liquid 模板变量

除了 Issue 通用变量外，PR 工作项额外提供以下变量：

| 变量 | 说明 |
|------|------|
| `{{ work_item.kind }}` | 工作项类型：`Issue` 或 `PullRequest` |
| `{{ work_item.head_branch }}` | PR 的源分支名称 |
| `{{ work_item.base_branch }}` | PR 的目标分支名称 |
| `{{ work_item.diff_url }}` | PR diff 的 URL |
| `{{ work_item.diff }}` | PR 的完整 diff 内容（首次 turn 自动拉取并注入） |
| `{{ work_item.review_state }}` | 当前审查状态：`None`、`Pending`、`Approved`、`ChangesRequested` |
| `{{ work_item.is_draft }}` | PR 是否为 draft |

---

## 工作区目录结构

每个 Issue 都有一个独立的工作区，GitHubTracker 在其中自动执行 `git clone`：

```
{workspace_root}/
└── {sanitized_identifier}/      ← 如 _42 (对应 #42)
    ├── .craft/                  ← Agent 的会话、记忆、配置
    ├── <git clone 内容>          ← 仓库文件
    └── ...
```

默认 `workspace_root` 为系统临时目录下的 `github_tracker_workspaces`，可通过配置覆盖：

```json
{
  "GitHubTracker": {
    "Workspace": {
      "Root": "~/git-workspaces"
    }
  }
}
```

支持 `~`（主目录）和 `$VAR`（环境变量）展开。

---

## 完整配置参考

在 `.craft/config.json`（或全局配置）中可配置所有 GitHubTracker 选项：

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

> **注意**：`IssuesWorkflowPath` 和 `PullRequestWorkflowPath` 中的相对路径以 DotCraft 工作区根目录（`workspace/`）为基准解析。默认值 `"WORKFLOW.md"` 对应 `workspace/WORKFLOW.md`，默认值 `"PR_WORKFLOW.md"` 对应 `workspace/PR_WORKFLOW.md`。
>
> `MaxConcurrentPullRequestAgents` 为 `0` 表示 PR Agent 与 Issue Agent 共享 `MaxConcurrentAgents` 并发槽，设为正整数则为 PR Agent 分配独立并发限制。

---

## Hooks 集成

GitHubTracker 的工作区生命周期与 DotCraft Hooks 系统集成，支持以下事件：

| 事件 | 触发时机 |
|------|---------|
| `after_create` | 首次创建工作区后（克隆完成后） |
| `before_run` | 每次 Agent 开始执行前 |
| `after_run` | 每次 Agent 执行结束后（无论成功或失败） |
| `before_remove` | 清理工作区前 |

示例：在每次 Agent 运行前自动安装依赖：

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

## 常见问题

### Agent 反复执行但 Issue 没有关闭

**原因**：Agent 没有调用 `CompleteIssue` 工具。

**解决**：确保 `WORKFLOW.md` 的提示词明确要求 Agent 在任务完成后调用 `CompleteIssue`。参考模板：

```
完成所有代码变更并推送后，调用 `CompleteIssue` 工具，传入你做了什么的简短说明。
```

### Issue 没有被轮询到

**原因**：Issue 没有活跃状态对应的 Label。

**解决**：给 Issue 添加 `status:todo` 或 `status:in-progress` Label（或根据你的 `GitHubStateLabelPrefix` 配置修改）。

### git clone 失败

**原因**：Token 没有 `Contents: Read` 权限，或网络不通。

**解决**：检查 Token 权限，确保 `$GITHUB_TOKEN` 环境变量已正确设置。clone 失败时 Agent 仍可执行，但工作区内没有仓库文件。

### `CompleteIssue` 调用失败

**原因**：Token 没有 `Issues: Write` 权限。

**解决**：重新生成 Token 并赋予 `Issues: Read and Write` 权限。

### Bot 提交 PR Review 后仍在反复运行

**正常行为**：编排器在每次 PR Agent 成功提交 Review 后，记录当前 HEAD SHA，PR 在下次轮询时不会被重复派发，直至开发者推送新提交。

**如果仍在反复运行**，可能是 Review 提交失败（Agent 在 turns 耗尽前未调用 `SubmitReview`），此时编排器会继续重试。检查日志中的 `ReviewSubmitted` 字段是否为 `true`。

### PR 没有被轮询到

**可能原因**：

1. PR 处于 **draft** 状态——编排器会自动跳过 draft PR。
2. PR 的当前状态不在 `PullRequestActiveStates` 列表中（例如已 `Approved`）。
3. 工作区根目录下不存在 `PR_WORKFLOW.md` 文件（或 `PullRequestWorkflowPath` 指向的文件不存在）。

**解决**：逐项检查上述条件，确保配置和 PR 状态匹配。
