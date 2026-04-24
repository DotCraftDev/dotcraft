<div align="center">

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/DotHarness/dotcraft)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![Docs Site](https://img.shields.io/badge/docs-site-4A7FA5.svg)](https://dotharness.github.io/dotcraft/)

**中文 | [English](./README_EN.md)**

面向项目的 Agent Harness，打造持久的 AI 工作空间。

*围绕你的项目，打造一个持久的 AI 工作空间。*

由 .NET 10 与 Unified Session Core 驱动，DotCraft 在终端、桌面应用、IDE 编辑器与即时社交软件之间提供统一且可观测的 AI 编排体验。

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png)

</div>

## ✨ 亮点

<table>
<tr>
<td width="25%" align="center"><b>📁 项目级工作空间</b><br/>Agent 可以不受具体应用形态限制真正了解你的项目</td>
<td width="25%" align="center"><b>⚡ 统一会话模型</b><br/>跨越社交软件、终端、桌面应用、编辑器和 Agent 对话</td>
<td width="25%" align="center"><b>🛡️ 可观测与治理</b><br/>Agent 安全可靠，出问题随时定位溯源</td>
<td width="25%" align="center"><b>🔗 扩展与集成</b><br/>高度可拓展性，快速集成业务功能</td>
</tr>
</table>

## 🚀 快速开始

使用 DotCraft 最推荐的 Desktop 桌面应用最为第一入口。

![Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/desktop.png)

### 安装部署

#### 方式一：直接下载 Release 包

前往 [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) 下载桌面应用：

| 平台 | 文件 |
|------|------|
| Windows | `DotCraft-Desktop-win-x64-Setup.exe` |
| macOS   | `DotCraft-Desktop-macos-x64.dmg` |

#### 方式二：从源码构建

1. 安装 [.NET 10 SDK](https://dotnet.microsoft.com/download)

2. 安装 Rust 套件
3. 安装 NodeJS
4. 运行 `build.bat`
5. 运行 `build/release/DotCraft-Desktop-Setup.exe`

### 配置工作区

首次进入需要选择文件夹作为工作区，请跟随配置向导完成初始化。

![setup](https://github.com/DotHarness/resources/raw/master/dotcraft/setup.png)

### 配置 API Key

DotCraft 支持以下两种配置方式：

- OpenAI 兼容格式的 API Key （例如 官方 API， OpenRouter 等提供商）
- Coding Agent CLI 反向代理 （基于 [CLIProxyAPI](https://github.com/router-for-me/CLIProxyAPI)）

![apiproxy](https://github.com/DotHarness/resources/raw/master/dotcraft/api-proxy.png)

### 高级配置

如果你需要查看完整配置项，请阅读 [配置指南](https://dotharness.github.io/dotcraft/config_guide)。

推荐通过内置 Dashboard 完成可视化配置，详情请阅读 [Dashboard 指南](https://dotharness.github.io/dotcraft/dash_board_guide)。

## 🔌 入口与工作流

DotCraft 围绕 **统一会话核心（Unified Session Core）** 组织不同入口：CLI、Desktop、IDE、机器人与自动化并不是各自维护一套 agent 流程，而是复用同一个执行引擎与会话模型。

先看它与传统 Gateway 风格架构的核心差异：

| 维度 | Gateway| Unified Session Core |
|------|-----------------------------------|----------|
| 客户端定制 | 消息总线丢失难以定制 | 灵活自由的客户端 |
| 审批 / HITL | 无法表达平台原生的审批交互 | 以平台原生 UI 呈现 |
| 跨渠道恢复 | 不支持 | 会话可跨渠道恢复 |
| 工作区持久化 | 不支持 | 围绕工作区设计 |

![entry](https://github.com/DotHarness/resources/raw/master/dotcraft/entry.png)

<div align="center">dotcraft 将不同入口连接同一个项目级工作空间，由统一会话核心负责承接执行、状态与编排。</div>


你可以按自己的使用场景选择最合适的入口：

| 如果你想... | 从这里开始 |
|---|---|
| 在本地终端中使用 | [CLI](#cli) |
| 使用终端富文本界面 | [TUI](#tui) |
| 以无头服务器方式运行 | [AppServer](#appserver) |
| 使用图形化桌面客户端 | [Desktop 桌面应用](#desktop-桌面应用) |
| 在编辑器或 IDE 中使用 | [编辑器与 ACP](#编辑器与-acp) |
| 接入聊天机器人 | [社交渠道](#社交渠道) |
| 运行自动化任务（Local / GitHub） | [Automations](#automations) |

### CLI

CLI 是最直接的入口，适合在本地项目目录中与 DotCraft 协作。它也是理解整套工作流的默认起点：先在仓库里启动，再根据需要延伸到 AppServer、Desktop 或自动化场景。

### TUI

TUI 适合希望在终端里获得更丰富交互体验的用户。它基于 Ratatui 构建，通过 Wire Protocol 连接 AppServer，并复用同一套会话能力。

### AppServer

AppServer 是 DotCraft 对外暴露能力的统一后端边界，通过 stdio 或 WebSocket 提供基于 JSON-RPC 的 Wire Protocol。它适合远程 CLI、多客户端接入，以及任意语言的自定义集成。详见 [AppServer 模式指南](https://dotharness.github.io/dotcraft/appserver_guide)。

### Desktop 桌面应用

Desktop 适合希望以图形化方式管理会话、Diff、计划与自动化审核的用户。它作为 AppServer 的图形化客户端工作，通过 Wire Protocol 消费同一套会话、审批与自动化能力。详见 [Desktop Client README](./desktop/README_ZH.md)。

### 编辑器与 ACP

编辑器与 ACP 适合希望把 DotCraft 直接嵌入开发环境的用户，包括 Unity、Obsidian 与 JetBrains IDE。这里的关键不是另起一套编辑器 Agent，而是通过 ACP 桥接层把编辑器接入同一个 AppServer 会话运行时。先看 [ACP 模式指南](https://dotharness.github.io/dotcraft/acp_guide)；如果主要在 Unity 中使用，再看 [Unity 集成指南](https://dotharness.github.io/dotcraft/unity_guide) 与 [Unity Client README](./src/DotCraft.UnityClient/Packages/com.dotcraft.unityclient/README.md)。

### 社交渠道

QQ / 企业微信是 DotCraft 的原生社交渠道，无需额外依赖，配置详情可查看 [QQ 机器人指南](https://dotharness.github.io/dotcraft/qq_bot_guide) 和 [企业微信指南](https://dotharness.github.io/dotcraft/wecom_guide)。

对于更多的社交渠道，DotCraft 使用 SDK 拓展的方式集成，详见 [Python SDK](https://dotharness.github.io/dotcraft/sdk/python) 和 [TypeScript SDK](https://dotharness.github.io/dotcraft/sdk/typescript)。

目前集成了 Telegram、微信、飞书社交渠道。

| Telegram（Python SDK） | 微信（TypeScript SDK） |
|:---:|:---:|
| ![telegram](https://github.com/DotHarness/resources/raw/master/dotcraft/telegram.jpg) | ![wechat](https://github.com/DotHarness/resources/raw/master/dotcraft/wechat.jpg) |

### Automations

Automations 适合运行本地任务与 GitHub 驱动的工作流，详见 [Automations 指南](https://dotharness.github.io/dotcraft/automations_guide)。

| Desktop 自动化面板 | GitHub 追踪 |
|:---:|:---:|
| ![desktop-github](https://github.com/DotHarness/resources/raw/master/dotcraft/desktop_github.png) | ![github-tracker](https://github.com/DotHarness/resources/raw/master/dotcraft/github-tracker.png) |
| 桌面应用查看自动化任务。 | PR 自动 Review。 |

## 🛡️ 观测与治理

### Dashboard

Dashboard 是 DotCraft 的可视化观察与配置入口，用于查看会话、追踪调用和编辑工作区设置，详见 [Dashboard 指南](https://dotharness.github.io/dotcraft/dash_board_guide)。

| 用量与会话概览 | 会话追踪 |
|:---:|:---:|
| ![dashboard](https://github.com/DotHarness/resources/raw/master/dotcraft/dashboard.png) | ![trace](https://github.com/DotHarness/resources/raw/master/dotcraft/trace.png) |
| 用量、会话统计，按渠道汇总。 | 完整记录工具调用、会话历史。 |

### 沙箱隔离

沙箱隔离用于把 Shell 与文件工具放到受控环境中执行，适合对安全边界和宿主隔离有更高要求的场景。安装、配置和安全细节请参阅 [配置指南](https://dotharness.github.io/dotcraft/config_guide)。

## 📚 文档导航

完整文档请访问 [官方文档](https://dotharness.github.io/dotcraft/)。

**我想直接在仓库里使用 DotCraft**

- [配置指南](https://dotharness.github.io/dotcraft/config_guide)：配置项、工具、安全、审批、MCP、沙箱、启动方式、Gateway
- [Dashboard 指南](https://dotharness.github.io/dotcraft/dash_board_guide)：Dashboard 页面、调试能力与可视化配置
- [Automations 指南](https://dotharness.github.io/dotcraft/automations_guide)：本地任务与 GitHub Issue/PR 编排、Agent 派发与人工审核流程
- [Rust TUI 指南](./tui/README_ZH.md)：构建方式、启动模式、快捷键、斜杠命令和主题配置

**我想把 DotCraft 接入编辑器或客户端**

- [Desktop Client 指南](./desktop/README_ZH.md)：Electron 桌面应用，构建、启动与功能概览
- [ACP 模式指南](https://dotharness.github.io/dotcraft/acp_guide)：编辑器/IDE 集成（JetBrains、Obsidian 等）
- [Unity 集成指南](https://dotharness.github.io/dotcraft/unity_guide)：Unity 编辑器扩展与 AI 驱动的场景和资源工具

**我想把 DotCraft 作为服务端或后端**

- [AppServer 模式指南](https://dotharness.github.io/dotcraft/appserver_guide)：Wire Protocol 服务器、WebSocket 传输、远程 CLI 连接

**我想构建机器人、适配器或扩展**

- [QQ 机器人指南](https://dotharness.github.io/dotcraft/qq_bot_guide)：NapCat、权限与审批
- [企业微信指南](https://dotharness.github.io/dotcraft/wecom_guide)：企业微信推送与机器人模式
- [Python SDK](https://dotharness.github.io/dotcraft/sdk/python)：使用 `dotcraft-wire` 与 Telegram 参考示例构建外部适配器
- [TypeScript SDK](https://dotharness.github.io/dotcraft/sdk/typescript)：使用 `dotcraft-wire` 构建微信、飞书等外部适配器
- [Hooks 指南](https://dotharness.github.io/dotcraft/hooks_guide)：生命周期事件钩子、Shell 命令扩展、安全防护

## 🤝 贡献指南

欢迎提交代码、文档与集成相关贡献。开始前请阅读 [CONTRIBUTING.md](./CONTRIBUTING.md)。

## 🙏 致谢

本项目受 [nanobot](https://github.com/HKUDS/nanobot) 与 [codex](https://github.com/openai/codex) 启发，并构建在 [Agent Framework](https://github.com/microsoft/agent-framework) 之上。

- [HKUDS/nanobot](https://github.com/HKUDS/nanobot)
- [openai/codex](https://github.com/openai/codex)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework)
- [alibaba/OpenSandbox](https://github.com/alibaba/OpenSandbox)
- [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)
- [openai/symphony](https://github.com/openai/symphony)
- [router-for-me/CLIProxyAPI](https://github.com/router-for-me/CLIProxyAPI)

## 📄 许可证

Apache License 2.0
