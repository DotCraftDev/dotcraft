# DotCraft Automations 指南

Automations 是 DotCraft 的自动化任务管线。它轮询任务来源，为待处理任务派发 Agent，并在完成后进入可审核的状态流。第一次使用建议先跑通本地任务，再接入 [GitHub 自动化](./automations/github.md)。

DotCraft 支持两类任务来源：

- **本地任务**：基于文件的任务，存放在 `.craft/tasks/` 目录下，完全在本地运行。
- **GitHub 任务**：轮询 GitHub Issue 和 Pull Request，为活跃工作项派发 Agent。

所有任务来源共享编排器、并发控制、会话服务和 Desktop Automations 面板。

## 快速开始

### 1. 配置 Automations

`Automations.Enabled` 默认启用。下面的配置适合显式调整轮询和并发参数：

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

`LocalTasksRoot` 为空时使用 `<workspace>/.craft/tasks/`。

### 2. 创建本地任务

在任务根目录下创建一个以任务 id 命名的文件夹，包含 `task.md` 和 `workflow.md`：

```text
<workspace>/
  .craft/
    tasks/
      my-task-001/
        task.md
        workflow.md
```

`task.md` 描述任务本身：

````markdown
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
````

`workflow.md` 描述 Agent 执行方式：

```markdown
---
max_rounds: 10
---
你正在执行一个本地自动化任务。

## 任务

- ID: {{ task.id }}
- 标题: {{ task.title }}

## 指令

{{ task.description }}

完成后，调用 `CompleteLocalTask` 工具并传入简短的完成说明。
```

### 3. 启动 DotCraft

```bash
dotcraft gateway
```

编排器会发现 `pending` 状态的任务并派发 Agent。Agent 完成后调用 `CompleteLocalTask`，任务进入 `awaiting_review` 状态等待人工审核。

### 命令行一次性任务

需要在脚本或 CI 中直接调用 Agent 时，使用 `dotcraft exec`：

```bash
dotcraft exec "检查最近一次提交并总结风险"
```

也可以从 stdin 读取输入，适合管道组合：

```bash
git diff --stat | dotcraft exec -
```

连接远程 AppServer 时，把连接参数放在 `exec` 后：

```bash
dotcraft exec --remote ws://server:9100/ws --token my-secret "汇总当前任务状态"
```

`dotcraft exec` 只运行一个输入并退出。stdout 只输出最终回复，连接信息、进度和错误写入 stderr；执行成功返回 `0`，配置错误、模型错误、审批请求被拒绝或运行取消时返回非 `0`。

## 配置

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Automations.Enabled` | 是否启用 Automations 编排器 | `true` |
| `Automations.LocalTasksRoot` | 本地任务根目录，留空使用 `.craft/tasks/` | 空 |
| `Automations.PollingInterval` | 轮询间隔 | `00:00:30` |
| `Automations.MaxConcurrentTasks` | 所有来源合计最大并发任务数 | `3` |

完整字段、模板变量和 Agent 工具见 [Automations 参考](./automations/reference.md)。

## 使用示例

| 场景 | 选择 |
|------|------|
| 每天 9 点提醒我查看邮件 | Cron |
| 每小时发送一次待办统计 | Cron |
| 每天扫描最近提交找 bug | Automation Task |
| 把 Agent 挂到已有聊天线程中持续响应 | Automation Task |
| GitHub Issue 自动处理或 PR 审查 | [GitHub 自动化](./automations/github.md) |

## 进阶

### Cron vs Automation Task

| 维度 | Cron | Automation Task |
|------|------|------------------|
| 入口 | Agent 在对话里调用 `Cron add/list/remove` | Desktop Automations 面板、模板或任务文件 |
| 粒度 | 单条消息 + 时间表 | 可编辑的 `workflow.md` |
| Thread | 每个 Job 独占 `cron:<id>` 线程 | 默认独立线程，也可绑定已有 thread |
| 生命周期 | 跑一次发一条消息 | `pending -> running -> awaiting_review -> approved/rejected` |
| 典型场景 | 提醒、通知、轻量统计 | 多轮工作流、周期性产出、人工审批 |

### 工作区目录

```text
<workspace>/
  .craft/
    tasks/
      <task-id>/
        task.md
        workflow.md
```

任务状态、执行摘要和审核结果会保存在任务文件中，Desktop Automations 面板会读取同一份状态。

## 故障排查

### 本地任务未出现

确认 `Automations.Enabled = true`，任务位于 `LocalTasksRoot` 下，并且 `task.md` 的 `status` 是 `pending`。

### Agent 完成后仍在等待

确认 workflow 明确要求调用 `CompleteLocalTask`。没有完成工具调用时，任务不会进入审核状态。

### 想接入 GitHub Issue 或 PR

继续阅读 [GitHub 自动化](./automations/github.md)。
