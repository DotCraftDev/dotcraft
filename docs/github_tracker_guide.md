# DotCraft GitHubTracker 指南

GitHubTracker 是 DotCraft 的自主 Issue 编排模块。它持续轮询 GitHub 等 Issue 跟踪器，为每个活跃 Issue 自动创建独立工作区并克隆仓库，派发 AI Agent 完成代码任务，最终通过 `complete_issue` 工具将 Issue 关闭并通知编排器停止重试。

灵感来源于 [OpenAI Symphony](https://github.com/openai/symphony)，核心实现依据其 [SPEC.md](https://github.com/openai/symphony/blob/main/SPEC.md) 规格说明书。

---

## 快速开始

### 第一步：在 `config.json` 中启用 GitHubTracker

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

### 第二步：在工作区根目录放置 `WORKFLOW.md`

```
workspace/
└── WORKFLOW.md   ← GitHubTracker 从这里读取 Agent 提示词
```

`WORKFLOW.md` 文件由两部分组成：YAML 前置配置（`---` 之间）和 Liquid 提示词模板：

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
你负责处理 Issue {{ issue.identifier }}: **{{ issue.title }}**

{{ issue.description }}

## 指令

1. 完成 Issue 中描述的任务。
2. 提交并推送你的修改：
   ```
   git add -A && git commit -m "fix: <描述> (closes {{ issue.identifier }})" && git push
   ```
3. 完成后调用 `complete_issue` 工具，传入简短的完成说明。
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

只有状态为 **活跃**（`ActiveStates`）的 Issue 才会被派发。Agent 调用 `complete_issue` 后，GitHubTracker 会关闭该 Issue，下次轮询时该 Issue 将不再出现在候选列表中，编排器停止重试。

---

## WORKFLOW.md 参考

### YAML 前置配置字段

| 字段 | 说明 | 默认值 |
|------|------|--------|
| `tracker.kind` | 跟踪器类型，目前仅支持 `github` | `github` |
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

> **注意**：`WorkflowPath` 中的相对路径以 DotCraft 工作区根目录（`workspace/`）为基准解析，默认值 `"WORKFLOW.md"` 对应 `workspace/WORKFLOW.md`。

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

**原因**：Agent 没有调用 `complete_issue` 工具。

**解决**：确保 `WORKFLOW.md` 的提示词明确要求 Agent 在任务完成后调用 `complete_issue`。参考模板：

```
完成所有代码变更并推送后，调用 `complete_issue` 工具，传入你做了什么的简短说明。
```

### Issue 没有被轮询到

**原因**：Issue 没有活跃状态对应的 Label。

**解决**：给 Issue 添加 `status:todo` 或 `status:in-progress` Label（或根据你的 `GitHubStateLabelPrefix` 配置修改）。

### git clone 失败

**原因**：Token 没有 `Contents: Read` 权限，或网络不通。

**解决**：检查 Token 权限，确保 `$GITHUB_TOKEN` 环境变量已正确设置。clone 失败时 Agent 仍可执行，但工作区内没有仓库文件。

### `complete_issue` 调用失败

**原因**：Token 没有 `Issues: Write` 权限。

**解决**：重新生成 Token 并赋予 `Issues: Read and Write` 权限。
