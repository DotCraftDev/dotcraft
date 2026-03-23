# DotCraft Automations 示例

**中文 | [English](./README.md)**

本目录提供 DotCraft **Automations** 管线的即用模板，包含三种场景：

- [example-local-task](./example-local-task)：**本地自动化任务** — 基于文件的任务，完全在本地运行，不依赖外部服务。
- [github-review-bot](./github-review-bot)：**PR 审查机器人** — 自动检出所有非草稿的开放 Pull Request，分析 diff 并提交结构化 `COMMENT` 审查。推送新提交后自动重新审查。
- [github-collab-dev-bot](./github-collab-dev-bot)：**协作开发机器人** — 针对 GitHub Issue 进行规划、实现并创建 PR，通过标签协调多次运行间的状态。

## 架构

所有自动化来源 — 本地任务和 GitHub 工作项 — 统一由 `AutomationOrchestrator` 管理。GitHub 场景需要同时启用 `Automations` 和 `GitHubTracker` 两个模块：

- `Automations` 启动编排器，轮询所有来源并分发任务。
- `GitHubTracker` 注册 `GitHubAutomationSource`，将 GitHub Issue/PR 接入编排器。

```text
config.json
├── Automations: { Enabled: true }       ← 启动编排器
└── GitHubTracker: { Enabled: true }     ← 注册 GitHub 来源
         │
         └─→ AutomationOrchestrator
               ├── LocalAutomationSource    (来自 Automations 模块)
               └── GitHubAutomationSource   (来自 GitHubTracker 模块)
```

## 示例内容

| 路径 | 用途 |
|------|------|
| `config.template.json` | 仅本地任务的配置模板（仅启用 Automations，无 GitHub） |
| `example-local-task/task.md` | 本地任务定义（YAML front matter + Markdown 正文） |
| `example-local-task/workflow.md` | 本地任务工作流提示 |
| `github-review-bot/config.template.json` | PR 审查配置模板（Automations + GitHubTracker） |
| `github-review-bot/PR_WORKFLOW.md` | PR 审查提示模板 |
| `github-collab-dev-bot/config.template.json` | Issue 开发配置模板（Automations + GitHubTracker） |
| `github-collab-dev-bot/WORKFLOW.md` | Issue 开发提示模板 |

## 前置条件

- 本机已安装或编译 DotCraft
- 已选定一个项目目录作为 DotCraft workspace（运行 `dotcraft` 时的当前工作目录）
- GitHub 场景需要：一个作用域限定到目标仓库的 [Fine-grained Personal Access Token](https://docs.github.com/en/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens#creating-a-fine-grained-personal-access-token)

---

## 本地任务示例

### 快速开始

1. 创建 workspace 配置目录：`mkdir -p .craft`（Linux/macOS）或 `mkdir .craft`（Windows）。
2. 将 `config.template.json` 复制为 `.craft/config.json`（或将 `Automations` 段落合并到已有配置中）。
3. 将 `example-local-task/` 复制到 `.craft/tasks/` 并重命名为你的任务 id：

```
<你的项目>/
  .craft/
    config.json
    tasks/
      my-task-001/
        task.md
        workflow.md
```

4. 编辑 `task.md`，使 `id` 与文件夹名一致，设置 `title`、时间戳和描述。
5. 运行 `dotcraft`。

智能体完成后会调用 `CompleteLocalTask` 标记任务完成，无需跑满 `max_rounds`。

### 配置说明

| 字段 | 说明 |
|------|------|
| `Automations.Enabled` | 必须为 `true` 才能启动编排器。 |
| `Automations.LocalTasksRoot` | 任务根目录。留空 = `<workspace>/.craft/tasks/`。 |
| `Automations.PollingInterval` | 轮询间隔。默认 `00:00:30`。 |
| `Automations.MaxConcurrentTasks` | 所有来源合计的最大并发任务数。 |

---

## GitHub PR 审查机器人

### 快速开始

```bash
mkdir -p /path/to/workspace/.craft
cp samples/automations/github-review-bot/config.template.json /path/to/workspace/.craft/config.json
cp samples/automations/github-review-bot/PR_WORKFLOW.md       /path/to/workspace/PR_WORKFLOW.md
```

编辑 `.craft/config.json`：

| 字段 | 示例 | 说明 |
|---|---|---|
| `GitHubTracker.Tracker.Repository` | `"your-org/your-repo"` | 格式：`owner/repo` |
| `GitHubTracker.Tracker.ApiKey` | `"$GITHUB_TOKEN"` | 保持不变即可使用环境变量 |
| `GitHubTracker.Hooks.BeforeRun` | 见文件 | 修改为你的机器人身份信息 |

设置令牌：

```bash
export GITHUB_TOKEN=ghp_xxxxxxxxxxxxxxxxxxxx
```

运行 `dotcraft`。机器人会自动检出所有非草稿的开放 PR。

### 生命周期

```
PR 打开（或推送新提交）
  → 下一次轮询时分发机器人（SHA 与上次审查不同）
  → 机器人审查 diff，调用 SubmitReview COMMENT
  → 审查发布到 PR
  → 编排器记录已审查的 SHA
  → 下次轮询：SHA 未变 → 跳过该 PR

推送新提交 → SHA 变化 → 机器人自动再次运行
```

机器人仅提交 `COMMENT` 审查 — 不会批准、请求更改或合并。

### 所需令牌权限

| 权限 | 级别 | 原因 |
|---|---|---|
| Metadata | 只读 | GitHub 必需 |
| Contents | 只读 | 克隆仓库并检出 PR 分支 |
| Pull requests | 读写 | 读取 PR diff，提交审查 |

---

## GitHub 协作开发机器人

### 快速开始

```bash
mkdir -p /path/to/workspace/.craft
cp samples/automations/github-collab-dev-bot/config.template.json /path/to/workspace/.craft/config.json
cp samples/automations/github-collab-dev-bot/WORKFLOW.md          /path/to/workspace/WORKFLOW.md
```

编辑 `.craft/config.json` 填入你的仓库和令牌，然后运行 `dotcraft`。

### 标签

| 标签 | 是否活跃 | 含义 |
|---|---|---|
| `status:todo` | 是 | 新 Issue，等待处理 |
| `status:in-progress` | 是 | 机器人正在实现 |
| `status:awaiting-review` | 否 | PR 已创建，等待人工审查 |
| `status:blocked` | 否 | 机器人受阻，需人工介入 |

机器人在运行过程中会自行管理这些标签。

### 生命周期

```
status:todo
  ↓  （机器人运行：规划 + 改标签）
status:in-progress
  ↓  （机器人运行：实现 + 推送 + 创建 PR + 改标签）
status:awaiting-review   ← 非活跃，机器人停止
  ↓  （人工合并 PR，关闭 Issue）
closed

如在任何阶段受阻：
status:in-progress  →  status:blocked  ← 非活跃，机器人停止
                        ↓  （人工解决后改回 status:todo）
                    status:todo  →  ...
```

### 所需令牌权限

| 权限 | 级别 | 原因 |
|---|---|---|
| Metadata | 只读 | GitHub 必需 |
| Contents | 读写 | 克隆、创建分支、提交、推送 |
| Issues | 读写 | 读取 Issue、评论、改标签 |
| Pull requests | 读写 | 创建 PR、发布 PR 评论 |

---

## 常见问题

### 本地任务未出现

- 确认合并后配置中 `Automations.Enabled` 为 `true`。
- 确认任务目录位于任务根下，且同时包含 `task.md` 和 `workflow.md`。

### GitHub PR / Issue 未被检出

- 确认 `Automations.Enabled` 和 `GitHubTracker.Enabled` **同时**为 `true`。
- PR：确认 PR 为开放状态且非草稿；确认审查状态在 `PullRequestActiveStates` 中。
- Issue：确认 Issue 有匹配活跃状态的标签（如 `status:todo`）。
- 确认工作流文件存在于配置路径（`PR_WORKFLOW.md` 或 `WORKFLOW.md`）。

### 审查机器人在同一提交上反复运行

编排器在每次成功审查后记录已审查的 HEAD SHA。若机器人在同一提交上重复运行，请检查日志中是否有 `ReviewCompleted=true`。如果运行在调用 `SubmitReview` 之前退出（turn 耗尽或超时），则不记录 SHA，机器人会重试 — 这是预期行为。已审查的 SHA 仅保存在内存中；服务重启会导致所有 PR 被重新审查一次。

### `gh: command not found`

安装 [GitHub CLI](https://cli.github.com/) 并确保其在 `PATH` 中。审查机器人不强制依赖 `gh` — `SubmitReview` 使用 DotCraft 内置的 GitHub API 集成。

### `SubmitReview` 或 `CompleteIssue` 报权限错误

这些工具使用 DotCraft 的 GitHub 令牌（`$GITHUB_TOKEN`）。请确保令牌具有上方列出的对应权限。
