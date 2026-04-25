# DotCraft 文档索引

按“我想做什么”选择文档。第一次使用建议先走 [快速开始](./getting-started.md)，跑通 Desktop、工作区和模型配置后，再进入进阶入口。

## 我是第一次使用

| 目标 | 推荐文档 |
|------|----------|
| 下载 Desktop、初始化工作区、配置 API Key、第一次运行 | [快速开始](./getting-started.md) |
| 使用图形化桌面客户端 | [Desktop 指南](./desktop_guide.md) |
| 在终端里使用完整界面 | [TUI 指南](./tui_guide.md) |
| 理解配置文件、API Key、安全边界和工具开关 | [配置与安全](./config_guide.md) |
| 查看 Trace、会话、工具调用和可视化配置 | [Dashboard 指南](./dash_board_guide.md) |

## 我想接入客户端或编辑器

| 目标 | 推荐文档 |
|------|----------|
| 让 JetBrains、Obsidian、Unity 等编辑器接入 DotCraft | [ACP 模式指南](./acp_guide.md) |
| 使用 Unity 编辑器扩展和场景资源工具 | [Unity 集成指南](./unity_guide.md) |
| 用外部 coding agent CLI 作为子代理 | [External CLI 子代理指南](./external_cli_subagents_guide.md) |
| 理解设置何时生效、哪些需要重启 | [设置生效层级指南](./settings-lifecycle.md) |

## 我想把 DotCraft 作为服务或协议后端

| 目标 | 推荐文档 |
|------|----------|
| 运行 Wire Protocol 服务、多客户端共享工作区 | [AppServer 模式指南](./appserver_guide.md) |
| 暴露 OpenAI-compatible HTTP API | [API 模式指南](./api_guide.md) |
| 接入 AG-UI / CopilotKit 前端 | [AG-UI 模式指南](./agui_guide.md) |
| 查看 AppServer 协议细节 | [AppServer Protocol Spec](https://github.com/DotHarness/dotcraft/blob/master/specs/appserver-protocol.md) |

## 我想运行自动化或扩展工作流

| 目标 | 推荐文档 |
|------|----------|
| 本地任务、GitHub Issue/PR 编排和人工审核 | [Automations 指南](./automations_guide.md) |
| 生命周期事件钩子和 Shell 扩展 | [Hooks 指南](./hooks_guide.md) |
| 使用完整示例验证功能 | [Samples 总览](./samples/index.md) |
| 准备工作区模板 | [Workspace Sample](./samples/workspace.md) |

## 我想构建机器人、SDK 或外部适配器

| 目标 | 推荐文档 |
|------|----------|
| 选择 Python 或 TypeScript SDK | [SDK 总览](./sdk/index.md) |
| 使用 Python 构建外部渠道 | [Python SDK](./sdk/python.md) |
| 参考 Telegram Python 适配器 | [Python Telegram Adapter](./sdk/python-telegram.md) |
| 使用 TypeScript 构建外部频道模块 | [TypeScript SDK](./sdk/typescript.md) |
| 接入 QQ / 企业微信 / 飞书 / Telegram / 微信 | [QQ](./sdk/typescript-qq.md) · [WeCom](./sdk/typescript-wecom.md) · [Feishu](./sdk/typescript-feishu.md) · [Telegram](./sdk/typescript-telegram.md) · [Weixin](./sdk/typescript-weixin.md) |

## Troubleshooting

### 搜索 Desktop 没有找到下载入口

请直接打开 [快速开始](./getting-started.md) 或 [Desktop 指南](./desktop_guide.md)。文档站搜索索引会优先收录这些页面。

### 不确定配置应该放全局还是工作区

API Key 放全局 `~/.craft/config.json`；项目特定模型、工具、入口和自动化配置放 `<workspace>/.craft/config.json`。

### 想贡献文档

文档要求中英文同步：中文在 `docs/*.md`，英文在 `docs/en/*.md`。特性文档建议包含 Quick Start、Configuration、Usage Examples、Advanced Topics、Troubleshooting。
