# DotCraft Bootstrap Samples

**中文 | [English](./README.md)**

Bootstrap 文件示例，适合直接复制到工作区的 `.craft/` 目录中，用来快速定制 Agent 的角色、语气和用户背景。

## 使用方式

将某个角色目录中的文件复制到你的工作区：

```text
<workspace>/.craft/AGENTS.md
<workspace>/.craft/SOUL.md
<workspace>/.craft/USER.md
```

你也可以混合使用不同目录中的文件，再根据自己的场景做调整。

## 文件说明

| 文件 | 作用 |
|------|------|
| `AGENTS.md` | 角色职责、行为边界、回答规则 |
| `SOUL.md` | 个性、语气、表达风格 |
| `USER.md` | 用户画像、受众背景、沟通偏好 |

## 示例列表

| 目录 | 说明 |
|------|------|
| [qq-group-assistant](./qq-group-assistant) | 中文 QQ 群助手模板，适合群聊答疑、排障和项目支持 |
| [senior-engineer](./senior-engineer) | English senior engineer template for architecture, review, and debugging |

## 说明

这些文件都是普通 Markdown，没有特殊格式要求。DotCraft 会从 `.craft/` 目录中读取它们，并注入到系统提示词中。
