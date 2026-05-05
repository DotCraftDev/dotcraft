# DotCraft Automations 参考

本页汇总 DotCraft 原生 Automations 的配置、任务文件、模板变量和工具。Automations 现在只负责工作区内的本地任务；GitHub Issue、Pull Request、审查轮次和派发由 Oratorio 接管。

## 配置字段

| 配置项 | 说明 | 默认值 |
|--------|------|--------|
| `Automations.Enabled` | 是否启用 Automations 编排器 | `true` |
| `Automations.LocalTasksRoot` | 本地任务根目录，留空使用 `.craft/tasks/` | 空 |
| `Automations.PollingInterval` | 轮询间隔 | `00:00:30` |
| `Automations.MaxConcurrentTasks` | 本地任务最大并发数 | `3` |
| `Automations.TurnTimeout` | 单轮对话超时时间 | `00:30:00` |
| `Automations.StallTimeout` | 停顿超时时间 | `00:10:00` |
| `Automations.MaxRetries` | 最大重试次数 | `3` |
| `Automations.RetryInitialDelay` | 重试初始延迟 | `00:00:30` |
| `Automations.RetryMaxDelay` | 重试最大延迟 | `00:10:00` |

## Agent 工具

### `CompleteLocalTask`

本地任务完成工具。Agent 完成工作后调用，任务记录完成摘要并进入完成路径。

| 参数 | 说明 |
|------|------|
| `summary` | 简短完成说明 |

## Workflow 模板变量

| 变量 | 说明 |
|------|------|
| `task.id` | 任务 id |
| `task.title` | 任务标题 |
| `task.description` | `task.md` 正文 |
| `task.thread_id` | 绑定线程 id |

## 目录结构

```text
<workspace>/
  .craft/
    tasks/
      <task-id>/
        task.md
        workflow.md
    automations/
      templates/
        <template-id>/
          template.md
```

## Hooks 集成

Automations 可以通过 Hooks 在本地任务生命周期中运行脚本，例如任务开始前检查环境、完成后发送通知，或失败后写入外部系统。Hooks 的事件、输入和退出码语义见 [Hooks 参考](../hooks/reference.md)。
