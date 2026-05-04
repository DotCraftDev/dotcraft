# SubAgent 配置指南

SubAgent 用于把一段独立任务委派给子 Agent。配置时要区分两个概念：

- `agentRole` 决定子 Agent 的行为、工具边界和提示词约束。
- `profile` 决定子 Agent 使用哪个运行时，例如 DotCraft 原生运行时或外部 CLI。

如果只想让 DotCraft 安全地做一级委派，通常不需要改配置。默认设置已经允许根 Agent 创建一级 SubAgent，并阻止 SubAgent 再继续创建 SubAgent。

## 快速开始

默认配置等价于：

```json
{
  "SubAgent": {
    "MaxDepth": 1
  }
}
```

此时：

- 根 Agent 可以调用 `SpawnAgent` 创建一级 SubAgent。
- 一级 SubAgent 默认超过深度限制，不能再创建新的 SubAgent。
- `agentRole` 未指定时使用 `default`。
- 原生 SubAgent 默认使用轻量提示词 profile：`subagent-light`。

## 内置角色

| 角色 | 适合场景 | 工具策略 |
|------|----------|----------|
| `default` | 普通一级协作、总结、局部分析 | 禁用 AgentTools，使用保守工具集 |
| `worker` | 实现、验证、文件修改任务 | 允许读写、Shell、Web；AgentTools 仍受 `MaxDepth` 限制 |
| `explorer` | 只读代码探索、资料调研 | 只保留只读探索和 Web 工具，禁用写入、Shell、Plan/Todo、SkillManage、AgentTools |

`worker` 具备递归委派的能力模型，但默认 `SubAgent.MaxDepth = 1` 会阻止一级 SubAgent 再调用 `SpawnAgent`。这让递归 SubAgent 成为显式开启的高级选项。

## 配置

SubAgent 配置可以放在全局 `~/.craft/config.json` 或工作区 `<workspace>/.craft/config.json`。团队项目建议放在工作区配置中。

### 开启二级 worker 委派

```json
{
  "SubAgent": {
    "MaxDepth": 2
  }
}
```

开启后：

- 根 Agent 创建的第一级 `worker` SubAgent 可以看到 `SpawnAgent`。
- 第二级 SubAgent 到达深度上限，不能继续创建第三级 SubAgent。
- `default` 和 `explorer` 仍按各自 role 策略禁用 AgentTools。

### 覆盖内置 worker 说明

同名 role 会覆盖内置 role。下面示例保留 `worker` 的轻量提示词 profile，并为团队工作流添加说明：

```json
{
  "SubAgent": {
    "Roles": [
      {
        "Name": "worker",
        "Description": "Team worker role for bounded implementation tasks.",
        "AgentControlToolAccess": "Full",
        "PromptProfile": "subagent-light",
        "ToolDenyList": ["UpdateTodos", "TodoWrite", "CreatePlan"],
        "Instructions": "Complete the assigned task within the requested files. Summarize changed paths and validation results."
      }
    ]
  }
}
```

### 添加只读探索角色

```json
{
  "SubAgent": {
    "Roles": [
      {
        "Name": "docs-explorer",
        "Description": "Read-only documentation and code explorer.",
        "ToolAllowList": ["ReadFile", "GrepFiles", "FindFiles", "WebSearch", "WebFetch", "SkillView"],
        "AgentControlToolAccess": "Disabled",
        "PromptProfile": "subagent-light",
        "Instructions": "Inspect files and web sources only. Do not edit files, execute shell commands, manage skills, or spawn agents."
      }
    ]
  }
}
```

主 Agent 调用 `SpawnAgent` 时用 `agentPrompt` 传入任务内容，并可把 `agentRole` 设为 `docs-explorer`。

### 使用完整提示词

原生 SubAgent 默认使用 `subagent-light`。如果某个 role 需要完整上下文，可以改为 `full`：

```json
{
  "SubAgent": {
    "Roles": [
      {
        "Name": "full-context-worker",
        "Description": "Worker role with the full root prompt context.",
        "AgentControlToolAccess": "Disabled",
        "PromptProfile": "full",
        "Instructions": "Use the full project context to complete the assigned task."
      }
    ]
  }
}
```

## 轻量提示词

`subagent-light` 是原生 session-backed SubAgent 的默认提示词 profile。它保留：

- DotCraft 基础身份
- 当前 workspace 和环境信息
- role instructions
- 工具能力和限制
- `.craft/AGENTS.md`
- 必要的文件引用和工作风格

它默认跳过：

- Memory 全量上下文
- Skill self-learning 长说明
- custom commands 摘要
- deferred MCP discovery 长说明
- Plan/Todo 注入

这能让短任务 SubAgent 更快启动，也避免把主线程的长期上下文全部复制给子线程。

## Profile 与 external CLI

`profile` 选择运行时。常见值包括：

| Profile | 说明 |
|---------|------|
| `native` | DotCraft 原生 SubAgent，支持 role-resolved 工具过滤 |
| `codex-cli` | 使用 Codex CLI 作为一次性外部 SubAgent |
| `cursor-cli` | 使用 Cursor CLI 作为一次性外部 SubAgent |

外部 CLI profile 的配置见 [External CLI 子代理指南](./external_cli_subagents_guide.md)。

DotCraft 会把 role instructions 传给外部 CLI，但不能强制过滤外部 CLI 自带工具。需要强隔离时，优先使用 `native` profile，并结合 role allow/deny list 与 [安全配置](./config/security.md)。

## 字段参考

完整字段表见 [完整配置参考](./reference/config.md#subagent)。
