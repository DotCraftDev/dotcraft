# Automations 指南

DotCraft 原生 Automations 仅覆盖本地任务。它从当前工作区的 `.craft/tasks/` 读取任务文件，按计划或手动触发 Agent，支持线程绑定、模板、重试、活动展示和 `CompleteLocalTask` 完成路径。

## 创建本地任务

一个本地任务位于 `.craft/tasks/<task-id>/`：

```text
.craft/
  tasks/
    weekly-report/
      task.md
      workflow.md
```

`task.md` 描述任务标题、正文、调度和线程绑定；`workflow.md` 描述 Agent 要执行的工作流提示词。

## 常用能力

| 能力 | 说明 |
|------|------|
| 手动运行 | Desktop Automations 面板或 AppServer `automation/task/run` |
| 定时运行 | 在任务文件中配置 `schedule` |
| 线程绑定 | 将任务绑定到已有线程，后续运行提交到该线程 |
| 模板 | 在 `.craft/automations/templates/` 保存可复用任务模板 |
| 完成工具 | Agent 调用 `CompleteLocalTask` 写入完成摘要 |
| 删除任务 | 删除任务文件夹，并可同时删除关联线程 |

## AppServer 方法

| 方法 | 说明 |
|------|------|
| `automation/task/list` | 列出本地任务 |
| `automation/task/read` | 读取单个本地任务 |
| `automation/task/create` | 创建本地任务 |
| `automation/task/run` | 立即运行本地任务 |
| `automation/task/updateBinding` | 更新或清除线程绑定 |
| `automation/task/delete` | 删除本地任务 |
| `automation/template/list` | 列出模板 |
| `automation/template/save` | 保存用户模板 |
| `automation/template/delete` | 删除用户模板 |

更多字段见 [Automations 参考](./automations/reference.md)。
