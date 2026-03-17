# DotCraft Bootstrap Samples

**中文 | [English](./README.md)**

Bootstrap 文件示例，适合直接复制到工作区的 `.craft/` 目录中，用来快速定制 Agent 的角色、语气和用户背景。

## 使用方式

示例文件使用 `.template.md` 后缀（如 `AGENTS.template.md`），若要使用某个示例：

1. 将该角色目录下的 `.template.md` 文件复制到你的工作区 `.craft/` 目录。
2. 将每个文件重命名，去掉 `.template` 部分，使其变为 `AGENTS.md`、`SOUL.md`、`USER.md`。

```text
# 示例：从 samples/bootstrap/senior-engineer/ 复制
<workspace>/.craft/AGENTS.md   （由 AGENTS.template.md 重命名）
<workspace>/.craft/SOUL.md     （由 SOUL.template.md 重命名）
<workspace>/.craft/USER.md     （由 USER.template.md 重命名）
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

这些文件都是普通 Markdown，没有特殊格式要求。在本仓库中它们以 `*.template.md` 形式存放；只有复制到你的工作区 `.craft/` 并重命名为 `.md` 后，DotCraft 才会加载。之后 DotCraft 会从 `.craft/` 目录中读取它们，并注入到系统提示词中。
