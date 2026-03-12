# DotCraft GitHubTracker Samples

**中文 | [English](./README.md)**

本示例为 DotCraft `GitHubTracker` 模块提供两个可直接使用的 `WORKFLOW.md` 模板：

- [review-bot](./review-bot)：**Issue 驱动的 PR Review Bot**，读取 Review 请求 Issue，拉取对应 PR 的 diff，发布结构化代码审查意见，最后关闭请求 Issue。
- [collab-dev-bot](./collab-dev-bot)：**多阶段协作开发 Bot**，对指定 Issue 进行规划、实现并提交 PR，通过 Label 在多次运行间协调状态。

## 使用方式

将模板文件复制到你自己的 DotCraft 工作区中：

```text
samples/github-tracker/<sample>/
  config.template.json   →  复制到  <your-workspace>/.craft/config.json
  WORKFLOW.md            →  复制到  <your-workspace>/WORKFLOW.md
```

复制后，编辑 `config.json`，填入你的仓库名、Token 等信息。

## 快速开始

### 第一步：复制文件

**review-bot**：
```bash
mkdir -p /path/to/my-workspace/.craft
cp samples/github-tracker/review-bot/config.template.json /path/to/my-workspace/.craft/config.json
cp samples/github-tracker/review-bot/WORKFLOW.md          /path/to/my-workspace/WORKFLOW.md
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
export GITHUB_TOKEN=ghp_xxxxxxxxxxxxxxxxxxxx   # DotCraft 用于克隆仓库、操作 Issue
export GH_TOKEN=$GITHUB_TOKEN                  # gh CLI 在 shell 命令中使用

# Windows PowerShell
$env:GITHUB_TOKEN = "ghp_xxxxxxxxxxxxxxxxxxxx"
$env:GH_TOKEN     = $env:GITHUB_TOKEN
```

> **为什么需要两个变量？**
> DotCraft Tracker 读取 `$GITHUB_TOKEN` 用于自身的 GitHub API 调用（克隆、Issue 状态查询、关闭 Issue）。Agent 运行的 shell 命令中，`gh` CLI 读取 `$GH_TOKEN`。两者通常指向同一个 Token。

### 第三步：编辑 `config.json`

替换复制后 `config.json` 中的以下字段：

| 字段 | 示例 | 说明 |
|---|---|---|
| `Tracker.Repository` | `"your-org/your-repo"` | 格式：`owner/repo` |
| `Tracker.ApiKey` | `"$GITHUB_TOKEN"` | 保持此值以从环境变量读取 |
| `Hooks.BeforeRun` | 见文件 | 修改邮箱/用户名为你的 Bot 身份 |

### 第四步：给 Issue 打标签

#### review-bot 标签约定

| 标签 | 含义 |
|---|---|
| `status:todo` | 新的 Review 请求，将被派发 |

Issue 正文必须包含待 Review 的 PR 编号和 Review 范围。

**Issue 正文示例**：
```
请 Review PR #42。

重点关注错误处理和新增的认证中间件。
```

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

**用途**：自动化 PR 代码审查。

**工作流程**：
1. 人工在 GitHub 上开一个带 `status:todo` 标签的 Issue，Issue 正文写明 PR 编号和 Review 范围。
2. Bot 被派发到该 Issue。
3. 使用 `gh pr diff` 拉取 PR diff。
4. 分析变更，通过 `gh pr comment` 或 `gh api` 在 PR 上发布结构化 Review 评论。
5. 调用 `complete_issue` 关闭 Review 请求 Issue。

**能力限制说明**：当前 `GitHubTracker` 仅对 GitHub _Issues_ 进行派发，PR 本身（带有 `pull_request` 字段的条目）会在候选阶段被过滤掉。review-bot 通过一个代理 Issue 作为触发点来绕过这一限制。

**状态流转**：
```
status:todo  →  （Bot 运行，发布 Review）  →  Issue 关闭
```

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

**为什么不在开 PR 后立即调用 `complete_issue`？**

`complete_issue` 会直接关闭 GitHub Issue。在大多数工作流中，Issue 应在 PR 被实际合并并验证后才关闭。使用 `status:awaiting-review` 可以保持 Issue 对讨论可见，同时防止 Bot 无限次重新派发。

---

## GitHub Token 权限要求

推荐使用 [Fine-grained Personal Access Token](https://docs.github.com/zh/authentication/keeping-your-account-and-data-secure/managing-your-personal-access-tokens#creating-a-fine-grained-personal-access-token)，并将权限限定在具体仓库范围内。

### review-bot

| 权限 | 最低要求 | 原因 |
|---|---|---|
| Metadata | Read-only | GitHub 必选权限，自动授予 |
| Contents | Read-only | 克隆仓库，读取文件 |
| Issues | Read and Write | 读取 Issue 正文、发布评论、关闭 Issue |
| Pull requests | Read and Write | 读取 PR diff、发布 Review 评论 |

### collab-dev-bot

| 权限 | 最低要求 | 原因 |
|---|---|---|
| Metadata | Read-only | GitHub 必选权限，自动授予 |
| Contents | Read and Write | 克隆、创建分支、提交、推送 |
| Issues | Read and Write | 读取 Issue、发布评论、修改标签 |
| Pull requests | Read and Write | 开 PR、发布 PR 评论 |

> `before_run` Hook 中执行 `gh auth login` 用于认证 `gh` CLI。这要求在启动 DotCraft 前已将 `$GH_TOKEN` 设置到环境变量中。

---

## 常见问题

### `gh: command not found`

安装 [GitHub CLI](https://cli.github.com/)，并确保其在 DotCraft 运行的环境 `PATH` 中可用。

### `before_run` Hook 中 `gh auth login` 失败

Hook 使用了 `gh auth login --with-token <<< "$GH_TOKEN"`，这是 Bash 的 Here String 语法。在 Windows 上请改用以下方式：

```powershell
echo $env:GH_TOKEN | gh auth login --with-token
```

相应更新 `config.json` 中的 `Hooks.BeforeRun` 字段。

### Bot 发布 Review / 开 PR 后仍在反复运行

这是因为 Issue 标签没有被成功改为非活跃状态。编排器在 Issue 处于 `active_states` 标签时会持续重试。确认工作流中的 `gh issue edit` 步骤是否已成功执行。

### Issue 没有被轮询到

确认 Issue 上有活跃状态对应的标签，例如 `status:todo`。标签识别基于 `GitHubStateLabelPrefix` 配置（默认 `status:`）。

### `complete_issue` 调用报权限错误

`complete_issue` 工具使用 DotCraft 自身的 GitHub Token（`$GITHUB_TOKEN`）。确认该 Token 具有 `Issues: Read and Write` 权限。
