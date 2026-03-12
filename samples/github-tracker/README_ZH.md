# DotCraft GitHubTracker Samples

**中文 | [English](./README.md)**

本示例为 DotCraft `GitHubTracker` 模块提供两个可直接使用的工作流模板：

- [review-bot](./review-bot)：**原生 PR Review Bot**，自动拉取带有 `auto-review` 标签的 Open PR，检出 PR 分支，分析 diff，并通过 GitHub Reviews API 提交 `COMMENT` 评审意见——仅提供反馈，不会 approve 或 block。
- [collab-dev-bot](./collab-dev-bot)：**多阶段协作开发 Bot**，对指定 Issue 进行规划、实现并提交 PR，通过 Label 在多次运行间协调状态。

## 使用方式

将模板文件复制到你自己的 DotCraft 工作区中：

```text
samples/github-tracker/<sample>/
  config.template.json   →  复制到  <your-workspace>/.craft/config.json
  WORKFLOW.md            →  复制到  <your-workspace>/WORKFLOW.md      （仅 collab-dev-bot）
  PR_WORKFLOW.md         →  复制到  <your-workspace>/PR_WORKFLOW.md   （仅 review-bot）
```

复制后，编辑 `config.json`，填入你的仓库名、Token 等信息。

## 快速开始

### 第一步：复制文件

**review-bot**：
```bash
mkdir -p /path/to/my-workspace/.craft
cp samples/github-tracker/review-bot/config.template.json /path/to/my-workspace/.craft/config.json
cp samples/github-tracker/review-bot/PR_WORKFLOW.md       /path/to/my-workspace/PR_WORKFLOW.md
```

**collab-dev-bot**：
```bash
mkdir -p /path/to/my-workspace/.craft
cp samples/github-tracker/collab-dev-bot/config.template.json /path/to/my-workspace/.craft/config.json
cp samples/github-tracker/collab-dev-bot/WORKFLOW.md          /path/to/my-workspace/WORKFLOW.md
```

### 第二步：设置环境变量

```bash
# Linux / macOS
export GITHUB_TOKEN=ghp_xxxxxxxxxxxxxxxxxxxx   # DotCraft 用于克隆仓库、调用 PR API

# Windows PowerShell
$env:GITHUB_TOKEN = "ghp_xxxxxxxxxxxxxxxxxxxx"
```

> `submit_review` 工具直接使用 DotCraft 自身的 Token（`$GITHUB_TOKEN`）调用 GitHub Reviews API，无需通过 `gh` CLI 中转。仅在 Agent 需要通过 `gh` 获取额外上下文时才需要第二个变量。

### 第三步：编辑 `config.json`

替换复制后 `config.json` 中的以下字段：

| 字段 | 示例 | 说明 |
|---|---|---|
| `Tracker.Repository` | `"your-org/your-repo"` | 格式：`owner/repo` |
| `Tracker.ApiKey` | `"$GITHUB_TOKEN"` | 保持此值以从环境变量读取 |
| `Tracker.PullRequestLabelFilter` | `"auto-review"` | 仅 Review 带此标签的 PR。Review 完成后编排器会自动摘掉标签；重新添加即可触发再次审查。 |
| `Hooks.BeforeRun` | 见文件 | 修改邮箱/用户名为你的 Bot 身份 |

### 第四步：开一个 PR

#### review-bot

Bot 会拉取配置仓库中所有 **open、非 draft** 且带有 `auto-review` 标签的 PR。提交评审后，编排器会**自动摘掉 `auto-review` 标签**，下次轮询时 PR 不再进入候选列表。

**触发 / 退出机制**：

1. 人工为 PR 添加 `auto-review` 标签。
2. 下次轮询时 Bot 被派发，审查 diff，调用 `submit_review` 提交 `COMMENT`。
3. 编排器在运行结束后自动摘掉 `auto-review` 标签。
4. 续派（1s 后）触发，PR 已不在候选列表中，Claim 释放，Bot 停止。
5. 如需重新审查（如开发者推送修复后），重新添加 `auto-review` 标签即可。

> Bot 只提交 `COMMENT` 类型的评审——不会 approve、request changes 或 merge。这避免了意外触发仓库的自动合并规则。

#### collab-dev-bot 标签约定

| 标签 | 活跃？ | 含义 |
|---|---|---|
| `status:todo` | 是 | 新 Issue，等待开始 |
| `status:in-progress` | 是 | Bot 正在实现 |
| `status:awaiting-review` | 否 | PR 已开，等待人工 Review |
| `status:blocked` | 否 | Bot 被阻塞，需要人工介入 |

Bot 在运行过程中会自行管理这些标签。

### 第五步：启动 DotCraft

```bash
dotcraft
```

---

## 示例说明

### review-bot

**用途**：原生自动化 PR 代码审查（仅提供反馈，不会 approve 或 merge）。

**工作流程**：
1. DotCraft 轮询 GitHub `/pulls` API，获取所有 open、非 draft 且带有 `auto-review` 标签的 PR。
2. PR 的 head 分支在隔离工作区中被检出。
3. PR diff 被自动拉取并注入到 Agent Prompt 中。
4. Agent 读取 diff（并可通过文件工具检查工作区中的完整文件），然后调用 `submit_review`，传入 `COMMENT`。
5. DotCraft 使用配置的 Token 通过 GitHub Reviews API 提交评审。

**生命周期**：
```
人工为 PR 添加 `auto-review` 标签
  → 下次轮询时 Bot 被派发
  → Bot 审查 diff，调用 submit_review COMMENT
  → 评审意见发布到 PR
  → 编排器自动摘掉 `auto-review` 标签
  → 续派触发（1s）：PR 已不在候选列表 → Claim 释放

如需重新审查（如开发者推送修复后）：
  → 重新添加 `auto-review` 标签 → Bot 再次运行
```

> **为什么只用 COMMENT？** 如果 Bot 提交 `APPROVE`，而仓库开启了"需要一个 Approval 后自动合并"的分支保护规则，Bot 的 approve 可能意外触发合并。使用 `COMMENT` 从根本上消除了这一风险。

**文件说明**：
- `PR_WORKFLOW.md` — PR 评审的 Prompt 模板，接收 `{{ issue.diff }}`、`{{ issue.head_branch }}`、`{{ issue.base_branch }}` 等变量。
- `config.template.json` — 仅启用 PR Review 的示例配置，只提供 `PullRequestWorkflowPath`。

---

### collab-dev-bot

**用途**：自主完成多阶段特性开发，并通过 Issue 状态协调跨轮次的工作。

**工作流程**：
1. 人工给 Issue 打上 `status:todo` 标签。
2. Bot 被派发，读取 Issue，探索代码库，以评论形式发布实现计划。
3. 将 Issue 改为 `status:in-progress`，开始实现。
4. 创建分支（`issue-<N>`），提交代码，推送，开 PR。
5. 开 PR 后将 Issue 改为 `status:awaiting-review`，编排器停止重新派发。
6. 如遇阻塞，发布阻塞评论，将 Issue 改为 `status:blocked`。
7. 人工解决阻塞后，将 Issue 改回 `status:todo` 或 `status:in-progress` 以恢复执行。

**状态流转**：
```
status:todo
  ↓  （Bot 运行：规划 + 改标签）
status:in-progress
  ↓  （Bot 运行：实现 + 推送 + 开 PR + 改标签）
status:awaiting-review   ← 非活跃，Bot 停止重试
  ↓  （人工合并 PR，关闭 Issue）
已关闭

如在任意阶段被阻塞：
status:in-progress  →  status:blocked  ← 非活跃，Bot 停止重试
                        ↓  （人工解决，改回 status:todo）
                    status:todo  →  ...
```

**为什么不在开 PR 后立即调用 `CompleteIssue`？**

`CompleteIssue` 会直接关闭 GitHub Issue。在大多数工作流中，Issue 应在 PR 被实际合并并验证后才关闭。使用 `status:awaiting-review` 可以保持 Issue 对讨论可见，同时防止 Bot 无限次重新派发。

---

## GitHub Token 权限要求

推荐使用 [Fine-grained Personal Access Token](https://docs.github.com/zh/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens#creating-a-fine-grained-personal-access-token)，并将权限限定在具体仓库范围内。

### review-bot

| 权限 | 最低要求 | 原因 |
|---|---|---|
| Metadata | Read-only | GitHub 必选权限，自动授予 |
| Contents | Read-only | 克隆仓库并检出 PR 分支 |
| Pull requests | Read and Write | 读取 PR diff、查询评审、提交评审 |
| Issues | Read and Write | Review 完成后自动摘掉 `auto-review` 标签 |

### collab-dev-bot

| 权限 | 最低要求 | 原因 |
|---|---|---|
| Metadata | Read-only | GitHub 必选权限，自动授予 |
| Contents | Read and Write | 克隆、创建分支、提交、推送 |
| Issues | Read and Write | 读取 Issue、发布评论、修改标签 |
| Pull requests | Read and Write | 开 PR、发布 PR 评论 |

---

## 常见问题

### `gh: command not found`

安装 [GitHub CLI](https://cli.github.com/)，并确保其在 DotCraft 运行的环境 `PATH` 中可用。

对于 review-bot，`gh` CLI 是可选的——`submit_review` 工具使用 DotCraft 内置的 GitHub API 集成。Agent 可能仍会调用 `gh` 获取额外上下文（如 `gh pr diff`、`gh pr checks`）。

### Bot 提交 Review 后仍在反复运行

编排器在每次 Review 成功完成后会自动摘掉 `auto-review` 标签。如果 Bot 仍在反复运行，请检查 DotCraft 日志中是否有类似 `"Failed to remove label 'auto-review'"` 的警告。通常原因是 GitHub Token 缺少 `Issues: Read and Write` 权限（标签摘除使用与 Issue 标签相同的 API 端点）。

注意：Bot 在失败或超时时**不会**摘除标签，这是有意为之——保留标签使 Bot 在下次轮询时重试。

### PR 没有被轮询到

- 确认 PR 是 **open 且非 draft** 状态。
- 如果设置了 `Tracker.PullRequestLabelFilter`，确认 PR 上有对应 Label。
- 确认工作区根目录下存在 `PR_WORKFLOW.md` 文件。

### `submit_review` 调用报权限错误

`submit_review` 工具使用 DotCraft 自身的 GitHub Token（`$GITHUB_TOKEN`）。确认该 Token 具有 `Pull requests: Read and Write` 权限。

### Issue 没有被轮询到（collab-dev-bot）

确认 Issue 上有活跃状态对应的标签，例如 `status:todo`。标签识别基于 `GitHubStateLabelPrefix` 配置（默认 `status:`）。

### `CompleteIssue` 调用报权限错误（collab-dev-bot）

`CompleteIssue` 工具使用 DotCraft 自身的 GitHub Token（`$GITHUB_TOKEN`）。确认该 Token 具有 `Issues: Read and Write` 权限。
