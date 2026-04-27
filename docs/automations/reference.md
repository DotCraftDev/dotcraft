# DotCraft Automations 参考

本页汇总 Automations 的字段、模板变量、Agent 工具、目录结构和 Hooks 集成。入门流程见 [Automations 指南](../automations_guide.md)，GitHub 流程见 [GitHub 自动化](./github.md)。

## 配置字段

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Automations.Enabled` | 是否启用 Automations 编排器 | `true` |
| `Automations.LocalTasksRoot` | 本地任务根目录，留空使用 `.craft/tasks/` | 空 |
| `Automations.WorkspaceRoot` | 任务工作区根目录，留空使用系统临时目录 | 空 |
| `Automations.PollingInterval` | 轮询间隔 | `00:00:30` |
| `Automations.MaxConcurrentTasks` | 所有来源合计最大并发任务数 | `3` |
| `Automations.TurnTimeout` | 单轮对话超时时间 | `00:30:00` |
| `Automations.StallTimeout` | 停顿超时时间 | `00:10:00` |
| `Automations.MaxRetries` | 最大重试次数 | `3` |
| `Automations.RetryInitialDelay` | 重试初始延迟 | `00:00:30` |
| `Automations.RetryMaxDelay` | 重试最大延迟 | `00:10:00` |

## Agent 工具

### `CompleteLocalTask`

本地任务完成工具。Agent 完成工作后调用，任务进入 `awaiting_review`。

| 参数 | 说明 |
|------|------|
| `summary` | 简短完成说明 |

### `CompleteIssue`

GitHub Issue 完成工具。Agent 完成工作后调用，编排器关闭对应 Issue 并记录摘要。

| 参数 | 说明 |
|------|------|
| `summary` | 简短完成说明 |

### `SubmitReview`

GitHub PR 审查工具。Agent 完成审查后调用，编排器向 PR 提交 review 并记录当前 HEAD SHA。

| 参数 | 说明 |
|------|------|
| `body` | Review 正文 |
| `event` | Review 事件，例如 `COMMENT`、`APPROVE`、`REQUEST_CHANGES` |

## Workflow 模板

### 本地任务变量

| 变量 | 说明 |
|------|------|
| `task.id` | 任务 id |
| `task.title` | 任务标题 |
| `task.description` | `task.md` 正文 |
| `task.thread_id` | 绑定线程 id |

### GitHub 变量

| 变量 | 说明 |
|------|------|
| `work_item.identifier` | Issue 或 PR 标识 |
| `work_item.title` | 标题 |
| `work_item.description` | 正文描述 |
| `work_item.url` | GitHub URL |
| `work_item.repository` | 仓库名 |

### GitHub YAML 前置字段

| 字段 | 说明 |
|------|------|
| `tracker.active_states` | 会被派发的状态列表 |
| `tracker.terminal_states` | 终态列表 |
| `agent.max_turns` | 单个 Agent 最大轮数 |
| `agent.max_concurrent_agents` | GitHub 来源内部最大并发 Agent 数 |

## 目录结构

### 本地任务

```text
<workspace>/
  .craft/
    tasks/
      <task-id>/
        task.md
        workflow.md
```

### GitHub 工作项

GitHub 工作项状态保存在工作区 `.craft/` 下，由 GitHubTracker 维护。用户通常只需要维护 `WORKFLOW.md`、`PR_WORKFLOW.md` 和 `.craft/config.json`。

## Hooks 集成

Automations 可以通过 Hooks 在任务生命周期中运行脚本，例如任务开始前检查环境、完成后发送通知，或失败后写入外部系统。Hooks 的事件、输入和退出码语义见 [Hooks 参考](../hooks/reference.md)。

## GitHub Token 权限

| 能力 | 所需权限 |
|------|----------|
| 读取 Issue / PR | Repository contents 和 issues / pull requests read |
| 关闭 Issue | Issues write |
| 提交 PR Review | Pull requests write |
| Clone 私有仓库 | Contents read |

使用 Fine-grained Personal Access Token 时，建议只授权目标仓库。
