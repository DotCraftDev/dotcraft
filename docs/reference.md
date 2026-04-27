# DotCraft 文档索引

按目标选择文档。第一次使用建议先走 [快速开始](./getting-started.md)，完成 Desktop、工作区和模型配置后，再进入日常使用、自动化或开发集成。

## 我是第一次使用

| 目标 | 推荐文档 |
|------|----------|
| 下载 Desktop、初始化工作区、配置 API Key、第一次运行 | [快速开始](./getting-started.md) |
| 使用图形化桌面客户端 | [Desktop 指南](./desktop_guide.md) |
| 在终端里使用完整界面 | [TUI 指南](./tui_guide.md) |
| 配置 API Key、模型、Endpoint 和工作区覆盖 | [配置指南](./config_guide.md) |

## 我想日常使用和排查

| 目标 | 推荐文档 |
|------|----------|
| 查看 Trace、会话、工具调用和可视化配置 | [Dashboard 指南](./dash_board_guide.md) |
| 理解设置何时生效、哪些需要重启 | [设置生效层级指南](./settings-lifecycle.md) |
| 让 Agent 创建、修补和维护工作区 skill | [Agent Skill 自学习](./skills/agent-self-learning.md) |
| 限制文件、Shell、网络和沙箱能力 | [安全配置](./config/security.md) |

## 我想运行自动化或扩展工作流

| 目标 | 推荐文档 |
|------|----------|
| 本地任务、Cron 选择和人工审核 | [Automations 指南](./automations_guide.md) |
| GitHub Issue/PR 自动化 | [GitHub 自动化](./automations/github.md) |
| Automations 字段、模板变量和工具 | [Automations 参考](./automations/reference.md) |
| 生命周期事件钩子和 Shell 扩展 | [Hooks 指南](./hooks_guide.md) |
| Hook 事件、payload、退出码和示例 | [Hooks 参考](./hooks/reference.md) |
| 用外部 coding agent CLI 作为子代理 | [External CLI 子代理指南](./external_cli_subagents_guide.md) |

## 我想接入客户端或编辑器

| 目标 | 推荐文档 |
|------|----------|
| 让 JetBrains、Obsidian、Unity 等编辑器接入 DotCraft | [ACP 模式指南](./acp_guide.md) |
| 使用 Unity 编辑器扩展和场景资源工具 | [Unity 集成指南](./unity_guide.md) |
| 运行 Wire Protocol 服务、多客户端共享工作区 | [AppServer 模式指南](./appserver_guide.md) |
| 暴露 OpenAI-compatible HTTP API | [API 模式指南](./api_guide.md) |
| 接入 AG-UI / CopilotKit 前端 | [AG-UI 模式指南](./agui_guide.md) |

## 我想构建机器人、SDK 或外部适配器

| 目标 | 推荐文档 |
|------|----------|
| 选择 Python 或 TypeScript SDK | [SDK 总览](./sdk/index.md) |
| 使用 Python 构建外部渠道 | [Python SDK](./sdk/python.md) |
| 参考 Telegram Python 适配器 | [Python Telegram Adapter](./sdk/python-telegram.md) |
| 使用 TypeScript 构建外部频道模块 | [TypeScript SDK](./sdk/typescript.md) |
| 接入 QQ / 企业微信 / 飞书 / Telegram / 微信 | [QQ](./sdk/typescript-qq.md) · [WeCom](./sdk/typescript-wecom.md) · [Feishu](./sdk/typescript-feishu.md) · [Telegram](./sdk/typescript-telegram.md) · [Weixin](./sdk/typescript-weixin.md) |

## 我想查参考信息

| 目标 | 推荐文档 |
|------|----------|
| 查看完整配置字段 | [完整配置参考](./reference/config.md) |
| 查看 Dashboard API 和 Trace 事件 | [Dashboard API](./reference/dashboard-api.md) |
| 使用完整示例验证功能 | [Samples 总览](./samples/index.md) |
| 准备工作区模板 | [Workspace Sample](./samples/workspace.md) |
| 查看 AppServer 协议细节 | [AppServer Protocol Spec](https://github.com/DotHarness/dotcraft/blob/master/specs/appserver-protocol.md) |

## 故障排查

### 搜索 Desktop 没有找到下载入口

打开 [快速开始](./getting-started.md) 或 [Desktop 指南](./desktop_guide.md)。

### 不确定配置应该放全局还是工作区

API Key 放全局 `~/.craft/config.json`；项目特定模型、工具、入口和自动化配置放 `<workspace>/.craft/config.json`。

### 想贡献文档

文档要求中英文同步：中文在 `docs/*.md`，英文在 `docs/en/*.md`。中文页使用“快速开始 / 配置 / 使用示例 / 进阶 / 故障排查”，英文页使用 “Quick Start / Configuration / Usage Examples / Advanced Topics / Troubleshooting”。
