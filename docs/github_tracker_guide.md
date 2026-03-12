# DotCraft GitHubTracker 指南

GitHubTracker 是 DotCraft 的自主 Issue 编排模块。它持续轮询 GitHub 等 Issue 跟踪器，为每个活跃 Issue 自动创建独立工作区并克隆仓库，派发 AI Agent 完成代码任务，最终通过 `CompleteIssue` 工具将 Issue 关闭并通知编排器停止重试。

灵感来源于 [OpenAI Symphony](https://github.com/openai/symphony)，核心实现依据其 [SPEC.md](https://github.com/openai/symphony/blob/main/SPEC.md) 规格说明书。

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
你负责处理 Issue {{ issue.identifier }}: **{{ issue.title }}**

{{ issue.description }}

## 指令

1. 完成 Issue 中描述的任务。
2. 提交并推送你的修改：
   ```
   git add -A && git commit -m "fix: <描述> (closes {{ issue.identifier }})" && git push
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

## Pull Request 跟踪

除了 Issue，GitHubTracker 还可以**直接跟踪 Pull Request**——无需创建代理 Issue。启用后，编排器会通过 GitHub `/pulls` API 轮询 PR，为每个符合条件的 PR 派发独立的 Agent 进行代码审查。

### 启用方式

在 `config.json` 中设置以下字段：

```json
{
  "GitHubTracker": {
    "PullRequestWorkflowPath": "PR_WORKFLOW.md",
    "Tracker": {
      "PullRequestLabelFilter": "auto-review"
    }
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

### PullRequestLabelFilter（Label 门控与自动状态流转）

设置 `PullRequestLabelFilter` 后，只有带有该 Label 的 PR 才会进入候选列表。编排器在每次 PR Agent 正常完成后会**自动移除该 Label**，使 PR 退出候选列表，与 Issue 工作流中 `CompleteIssue` 关闭 Issue 的机制对称。

**完整流程**：

1. 人工为 PR 添加指定 Label（如 `auto-review`）。
2. Bot 在下次轮询时拾取该 PR 并执行审查。
3. Agent 调用 `submit_review` 完成评审并退出。
4. 编排器自动调用 `DELETE /repos/{owner}/{repo}/issues/{number}/labels/{label}` 移除 Label。
5. 续派（约 1s 后）触发，PR 不再出现在候选列表中，Claim 释放。
6. 如需重新审查（例如开发者推送了修复），重新添加该 Label 即可。

**失败时的行为**：Label 只在 Agent 正常退出时移除；失败、超时或 Stall 时**不会**移除，这使得编排器可以重试直至 Review 成功。

> 此功能要求 GitHub Token 具有 **Issues: Read and Write** 权限（Label 移除使用与 Issue 标签相同的 REST API 端点）。

如果 `PullRequestLabelFilter` 为空，编排器会拾取所有符合活跃状态的非 draft PR，且不执行自动 Label 移除。

### 安全说明：COMMENT vs APPROVE

`submit_review` 工具支持三种 review event：`APPROVE`、`REQUEST_CHANGES`、`COMMENT`。

- **`COMMENT`**：仅发表意见，不改变 PR 的审查状态。推荐用于纯审查场景。
- **`APPROVE`**：将 PR 标记为已批准。**危险**——如果仓库开启了"需要一个 Approval 后自动合并"的分支保护规则，Bot 的 approve 可能直接触发合并。
- **`REQUEST_CHANGES`**：将 PR 标记为"需要修改"。这会阻止合并，但可能打断正常的人工审查流程。

> 建议在 `PR_WORKFLOW.md` 中明确指示 Agent 只使用 `COMMENT`，除非你有明确的自动化需求。

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
| `{{ issue.id }}` | Issue 编号（纯数字） |
| `{{ issue.identifier }}` | Issue 标识符（如 `#42`） |
| `{{ issue.title }}` | Issue 标题 |
| `{{ issue.description }}` | Issue 正文 |
| `{{ issue.state }}` | 当前状态 |
| `{{ issue.url }}` | Issue 的 GitHub URL |
| `{{ issue.labels }}` | 标签列表 |
| `{{ attempt }}` | 当前是第几次尝试（从 1 开始） |

### PR_WORKFLOW.md 参考

编排器会加载 `PullRequestWorkflowPath` 指定的文件（默认 `PR_WORKFLOW.md`）作为 PR 审查 Agent 的提示词。格式与 `WORKFLOW.md` 相同，由 YAML 前置配置和 Liquid 模板组成。

#### PR 专用 YAML 前置字段

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `tracker.pull_request_active_states` | PR 活跃状态列表 | `["Pending Review", "Review Requested", "Changes Requested"]` |
| `tracker.pull_request_terminal_states` | PR 终态列表 | `["Merged", "Closed", "Approved"]` |
| `tracker.pull_request_label_filter` | Label 门控，仅处理带此 Label 的 PR | 空（处理所有） |
| `agent.max_concurrent_pull_request_agents` | PR Agent 最大并发数 | `0`（不限制，共享 `max_concurrent_agents`） |

#### PR 专用 Liquid 模板变量

除了 Issue 通用变量外，PR 工作项额外提供以下变量：

| 变量 | 说明 |
|------|------|
| `{{ issue.kind }}` | 工作项类型：`Issue` 或 `PullRequest` |
| `{{ issue.head_branch }}` | PR 的源分支名称 |
| `{{ issue.base_branch }}` | PR 的目标分支名称 |
| `{{ issue.diff_url }}` | PR diff 的 URL |
| `{{ issue.diff }}` | PR 的完整 diff 内容（首次 turn 自动拉取并注入） |
| `{{ issue.review_state }}` | 当前审查状态：`None`、`Pending`、`Approved`、`ChangesRequested` |
| `{{ issue.is_draft }}` | PR 是否为 draft |

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
      "PullRequestTerminalStates": ["Merged", "Closed", "Approved"],
      "PullRequestLabelFilter": ""
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

**正常行为**：编排器在每次 PR Agent 正常完成后会自动移除 `PullRequestLabelFilter` 配置的 Label（如 `auto-review`），PR 随即退出候选列表，Bot 不再被派发。

**如果仍在反复运行**，请检查 DotCraft 日志是否有类似 `"Failed to remove label 'auto-review'"` 的警告：
- 最常见原因：GitHub Token 缺少 **Issues: Read and Write** 权限。
- 其次：Label 名称拼写与 `PullRequestLabelFilter` 配置不一致。

**注意**：Label 只在 Agent **正常退出**时移除。如果 Bot 因超时或失败退出，Label 保留不变，这是有意为之——保留 Label 使编排器可以重试。

### PR 没有被轮询到

**可能原因**：

1. PR 处于 **draft** 状态——编排器会自动跳过 draft PR。
2. 设置了 `Tracker.PullRequestLabelFilter`，但 PR 上没有对应的 Label。若 Label 已被自动移除，需重新添加才能触发新一轮审查。
3. 工作区根目录下不存在 `PR_WORKFLOW.md` 文件（或 `PullRequestWorkflowPath` 指向的文件不存在）。

**解决**：逐项检查上述条件，确保配置和 PR 状态匹配。
