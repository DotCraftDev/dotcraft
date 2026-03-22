# DotCraft Automations 示例（本地任务）

**中文 | [English](./README.md)**

本目录提供一份**可随仓库分发的参考**，用于在启用 **本地自动化任务** 时配置 DotCraft：包含安全的 `config.template.json`，以及可复制到你真实工作区的**示例任务目录**。

包含内容：

- `config.template.json`：带 `Automations` 的 workspace 配置示例（含 Dashboard / 可选工具等默认值）
- `example-local-task/`：示例 `task.md` 与 `workflow.md`，需复制到本机任务根目录下（本仓库不随附 `.craft/` 运行数据）

**本地**自动化任务运行时，`local-task` 工具配置会提供 **`CompleteLocalTask`**，智能体可用其将 `task.md` 标为完成，而不必跑满 `max_rounds`。

## 示例内容

| 路径 | 用途 |
|------|------|
| `config.template.json` | 示例配置；复制到你自己的 workspace 的 `.craft/config.json`（按需合并字段） |
| `example-local-task/task.md` | 任务定义（YAML front matter + Markdown 正文） |
| `example-local-task/workflow.md` | 智能体工作流提示（YAML front matter + Liquid 正文） |
| `.craft/config.json` | **本机**实际使用的 workspace 配置（本地创建，通常不提交） |
| `.craft/tasks/<task-id>/` | 运行时**本地任务**所在目录（通过复制示例创建） |

每个任务下的 `workspace/` 目录由运行时按需创建，无需从本示例手动拷贝。

## 前置条件

- 本机已安装或已编译 DotCraft
- 已选定一个真实的**项目目录**作为 DotCraft workspace（运行 `dotcraft` 时的当前工作目录）
- 若使用 Automations 相关能力，需按你的部署方式启用 Gateway / AppServer

## 快速开始

### 1. 使用配置模板

DotCraft 会先读取全局配置 `~/.craft/config.json`（Windows：`%USERPROFILE%\.craft\config.json`），再与 workspace 下的 `<workspace>/.craft/config.json` 合并。

1. 若尚未存在，请创建 workspace 配置目录：`mkdir -p .craft`（Linux/macOS）或 `mkdir .craft`（Windows PowerShell）。
2. 将 `config.template.json` 复制为**你项目 workspace** 下的 `.craft/config.json`（或把其中的 `Automations` 等段落合并进已有文件）。
3. 密钥与机器相关配置尽量放在全局配置中。

### 2. 安装示例本地任务

本地任务在 Automations 配置的 **`LocalTasksRoot`** 下扫描。当 `LocalTasksRoot` 为空字符串时，默认使用：

`<workspaceRoot>/.craft/tasks/`

请不要假设仓库会自带 `.craft/tasks/`。在你本机上：

1. 将 `example-local-task` 目录复制到 `.craft/tasks/` 下。
2. 将文件夹重命名为你的任务 id（例如 `my-task-001`）。
3. 编辑 `task.md` 的 front matter，使 `id` 与文件夹名一致，并设置 `title`、时间戳与任务描述。
4. `task.md` 与 `workflow.md` 必须放在同一任务目录中。

示例目录结构：

```
<你的项目>/
  .craft/
    config.json
    tasks/
      my-task-001/
        task.md
        workflow.md
        workspace/          # 任务运行后可能出现
```

## 配置说明

### Automations 相关字段（见 `config.template.json`）

| 字段 | 说明 |
|------|------|
| `Automations.Enabled` | 为 `true` 时启用 Automations 模块（Gateway 通道）。 |
| `Automations.LocalTasksRoot` | 任务根目录。空字符串表示使用 `<workspaceRoot>/.craft/tasks/`。可填绝对路径。 |
| `Automations.PollingInterval` | 轮询间隔。模板中为 30 秒（`00:00:30`）。 |
| `Automations.MaxConcurrentTasks` | 各来源合计的最大并发调度任务数。 |
| `Automations.WorkspaceRoot` | 每个任务智能体工作目录的根。若 JSON 中省略该键，将使用内置默认值（位于用户目录下）。请勿将空字符串写入该字段。 |

### 模板中其它字段

`DashBoard`、`Tools.Sandbox`、`McpServers` 等与其它 workspace 配置相同，请按本机地址、端口与实际启用情况调整。

## 常见问题

### 看不到任务

- 确认合并后的配置里 `Automations.Enabled` 为 `true`。
- 确认任务目录位于任务根下（默认：`.craft/tasks/<task-id>/`），且包含 `task.md` 与 `workflow.md`。

### 任务目录位置不对

- 将 `LocalTasksRoot` 设为绝对路径，或留空使用默认的 `.craft/tasks/`。
