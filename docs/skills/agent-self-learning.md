# Agent Skill 自学习

DotCraft 默认允许 agent 将成功经验沉淀为工作区 skill。启用后，agent 会获得一个聚合工具 `SkillManage(action, ...)`，用于创建、更新、修补和维护当前工作区的 skill 文件。

该能力默认启用。关闭时，`SkillManage` 不会注册到模型上下文，内置 `skill-authoring` 工作流 skill 也不会加载。

## 启用

在工作区配置中设置：

```json
{
  "Skills": {
    "SelfLearning": {
      "Enabled": true
    }
  }
}
```

配置修改需要启动新会话或重启运行中的宿主后生效，因为工具列表和系统提示在 agent 创建时固定。

## 配置项

| 配置 | 默认值 | 说明 |
|---|---:|---|
| `Skills.SelfLearning.Enabled` | `true` | 主开关，默认启用。关闭时不暴露 `SkillManage`，也不会加载 `skill-authoring`。 |
| `Skills.SelfLearning.MaxSkillContentChars` | `100000` | 单个 `SKILL.md` 的最大字符数。 |
| `Skills.SelfLearning.MaxSupportingFileBytes` | `1048576` | 单个 supporting file 的最大字节数。 |

## Agent 可用工具

启用后，agent 只会看到一个工具：

```text
SkillManage(action, name, content?, oldString?, newString?, filePath?, fileContent?, replaceAll?)
```

Action 速查：

| Action | 必填参数 | 用途 |
|---|---|---|
| `create` | `name`, `content` | 创建新的工作区 skill。 |
| `patch` | `name`, `oldString`, `newString` | 局部修补 `SKILL.md` 或 supporting file。 |
| `edit` | `name`, `content` | 完整替换已有工作区 skill 的 `SKILL.md`。 |
| `write_file` | `name`, `filePath`, `fileContent` | 写入 supporting file。 |
| `remove_file` | `name`, `filePath` | 删除 supporting file。 |

## 内置工作流 skill

启用自学习后，DotCraft 会在 `SkillManage` 可用时注入轻量 self-learning guidance，说明什么时候应该创建或修补 workspace skill。内置 `skill-authoring` skill 作为按需 authoring reference 出现在 skills summary 中，用于查看 `SKILL.md` frontmatter 要求、action 选择、supporting file 目录约束、常见坑和验证方法。

`SkillManage` 在执行 `create` 前会触发 DotCraft 审批（`kind: skill`），与文件/Shell 审批一致；如果发起破坏性的 `delete` 请求，也会触发审批。`edit` / `patch` / `write_file` / `remove_file` 不需要审批。

`skill-authoring` 声明了 `tools: SkillManage`，所以当自学习关闭、`SkillManage` 不存在时，它不会出现在可用 skill 列表中。

## 边界

自学习工具只写当前工作区的 skill 目录。内置 skill 和用户全局 skill 被视为只读；如果需要修改它们，agent 应创建一个新的工作区 skill 副本。

支持文件只能写在以下目录下：

- `scripts/`
- `assets/`

工具会拒绝绝对路径和 `..` 路径穿越。

## 适合保存为 skill 的情况

- 完成复杂任务后总结出可复用流程。
- 修复了一个以后可能再次遇到的棘手错误。
- 用户纠正了 agent 的做法，并形成了稳定步骤。
- 使用已有 skill 时发现其步骤过期、不完整或存在坑点。

简单的一次性回答不应保存为 skill。
