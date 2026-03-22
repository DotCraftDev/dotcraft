<div align="center">

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/DotCraftDev/DotCraft)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)

**中文 | [English](./README.md)**

# DotCraft

**Craft around your project.**

一款 Agent Harness，围绕你的项目，打造一个持久的 AI 工作空间。

无论您使用桌面应用、命令行界面 (CLI)、编辑器（IDE）、聊天机器人还是 API，它都能满足您的需求。

![banner](https://github.com/DotCraftDev/resources/raw/master/dotcraft/banner.png)

</div>

> **注意**：目前项目处于早期开发阶段，可能存在 Breaking Changes。

## ✨ 亮点

<table>
<tr>
<td width="33%" align="center"><b>📁 项目为先</b><br/>会话、记忆、技能与配置保存在 <code>.craft/</code> 下，跟着项目走</td>
<td width="33%" align="center"><b>⚡ 统一会话核心</b><br/>桌面应用、CLI、编辑器、机器人与工作流共享同一套会话模型，跨入口恢复，平台交互原生保留</td>
<td width="33%" align="center"><b>🛡️ 可观察可治理</b><br/>审批、追踪、Dashboard 与可选沙箱隔离内建</td>
</tr>
</table>

![intro](https://github.com/DotCraftDev/resources/raw/master/dotcraft/intro.png)

- ⚡ **统一会话核心**：统一所有服务端管理渠道的执行路径，支持跨入口恢复与原生平台交互，是 DotCraft "crafting around your project" 理念的核心抽象
- 🛠️ 文件、Shell、Web 与 SubAgent 工具，面向真实工作流
- 🔗 支持 MCP、ACP、AG-UI 与 OpenAI 兼容 API
- 🌐 支持 External Channel Adapter，可用 Python 或任意支持 JSON-RPC 的语言构建自定义渠道接入
- 🖥️ 原生编辑器集成：Unity、JetBrains 系列 IDE、Obsidian
- 👥 基于 GitHubTracker 的 Issue 与 PR 编排
- 🧩 Skills、Hooks、斜杠命令与工作区定制
- ⚗️ MCP 工具延迟加载，大规模工具场景更高效

## 🚀 快速开始

**环境要求**：

- 支持的 LLM API Key（OpenAI 兼容格式）

**方式一 — 直接下载预构建包**（无需安装 .NET SDK）：

前往 [GitHub Releases](https://github.com/DotCraftDev/DotCraft/releases) 下载对应平台的压缩包：

| 平台 | 文件 |
|------|------|
| Windows | `DotCraft-win-x64.zip` |
| Linux   | `DotCraft-linux-x64.tar.gz` |
| macOS   | `DotCraft-macos-x64.tar.gz` |

解压后即可运行，可选将目录添加到 PATH：

```bash
# Windows — 解压 DotCraft-win-x64.zip，可选加入 PATH
powershell -File install_to_path.ps1

# Linux / macOS — 解压后可选移动至 $PATH 目录
tar -xzf DotCraft-linux-x64.tar.gz   # 或 DotCraft-macos-x64.tar.gz
```

**方式二 — 从源码构建**：

需要提前安装 [.NET 10 SDK](https://dotnet.microsoft.com/download)。

```bash
# Windows
build.bat

# Linux / macOS
bash build-linux.bat

# 配置路径到环境变量（可选，Windows）
cd Release/DotCraft
powershell -File install_to_path.ps1
```

**首次启动**：

```bash
cd my-project
dotcraft
```

第一次运行时，DotCraft 会初始化当前工作区下的 `.craft/`；如果缺少可用的 `ApiKey`，会自动打开本地 Dashboard 引导首次配置。保存后重新运行 `dotcraft` 即可进入 CLI。

**示例会话**：

```
You > 总结一下这个仓库最近的变更

DotCraft is thinking...

我已经查看了最近的 Git 历史。以下是近一周的变更摘要：...
```

如果你希望手动编辑配置或了解更完整的配置项，请阅读 [配置指南](./docs/config_guide.md)。

## ⚙️ 配置说明

首次使用时，推荐通过内置 Dashboard 完成可视化配置。后续如果需要调整工作区设置，也可以继续使用 Dashboard 的 Settings 页面。

如果你需要查看完整配置项、配置层级或手动编辑方式，请阅读 [配置指南](./docs/config_guide.md)。

## 🔌 入口与工作流

所有入口共享同一个执行引擎——**统一会话核心（Unified Session Core）**。下表说明它与传统 Gateway 风格架构（如 nanobot / OpenClaw）的核心差异：

| 维度 | Gateway 风格（nanobot / OpenClaw） | DotCraft |
|------|-----------------------------------|----------|
| 会话模型 | 扁平化 `MessageBus`（`InboundMessage` / `OutboundMessage`） | 统一会话核心 |
| 渠道接入方式 | Gateway 将事件路由至通用消息总线 | 每个适配器都是完整的双向 Wire Protocol Client |
| 平台原生交互 | 压平为消息总线后丢失平台特性 | 保留——每个适配器独立负责自身平台的渲染逻辑 |
| 审批 / HITL | 无法表达平台原生的审批交互 | 双向：服务端下发审批请求，适配器以平台原生 UI 呈现（Telegram Inline Keyboard、QQ 消息回复等） |
| 跨渠道恢复 | 不支持 | 服务端管理的 Thread 可跨渠道恢复 |
| 工作区持久化 | 框架层不定义 | `.craft/` 统一管理会话、记忆、技能与配置，随项目走 |

![entry](https://github.com/DotCraftDev/resources/raw/master/dotcraft/entry.png)

```mermaid
flowchart LR
    Cli["CLI"]
    Desktop["Desktop"]
    AppSrv["AppServer"]
    Ide["ACP / IDE"]
    Bots["QQ / WeCom / ..."]
    ExtCh["External Channels (Telegram, ...)"]
    Workflow["GitHub Workflow"]
    Api["API / AG-UI"]

    subgraph Workspace [".craft/"]
        Core["**Unified Session Core**"]
    end

    Dashboard["Dashboard"]

    Cli --> AppSrv
    Desktop --> AppSrv
    AppSrv --> Workspace
    Ide --> Workspace
    Bots --> Workspace
    ExtCh -->|"SDK / JSON-RPC"| AppSrv
    Workflow --> Workspace
    Api --> Workspace
    Workspace --> Dashboard

    style Core fill:#0969da,color:#ffffff,stroke:#0550ae
    style Workspace fill:#ddf4ff,stroke:#54aeff,color:#0550ae
    style Dashboard fill:#e5a50a,color:#ffffff,stroke:#bf8700
    style Cli fill:#57606a,color:#ffffff,stroke:#424a53
    style Desktop fill:#57606a,color:#ffffff,stroke:#424a53
    style AppSrv fill:#57606a,color:#ffffff,stroke:#424a53
    style Ide fill:#57606a,color:#ffffff,stroke:#424a53
    style Bots fill:#57606a,color:#ffffff,stroke:#424a53
    style ExtCh fill:#57606a,color:#ffffff,stroke:#424a53
    style Workflow fill:#57606a,color:#ffffff,stroke:#424a53
    style Api fill:#57606a,color:#ffffff,stroke:#424a53
```

| 如果你想... | 从这里开始 |
|---|---|
| 在本地终端中使用 | [CLI](#本地-cli) |
| 以无头服务器方式运行 | [AppServer](#appserver) |
| 使用图形化桌面客户端 | [Desktop 桌面应用](#desktop-桌面应用) |
| 在编辑器或 IDE 中使用 | [编辑器与 ACP](#编辑器与-acp) |
| 把 DotCraft 作为服务接入 | [API / AG-UI](#api--ag-ui) |
| 接入聊天机器人 | [QQ / 企业微信](#qq--企业微信) |
| 自定义渠道适配器 | [External Channels](#external-channels外部渠道适配器) |
| 自动化 GitHub Issue 与 PR | [GitHub 工作流](#github-工作流自动化) |

### 本地 CLI

CLI 是最直接的起点，适合在本地项目目录中直接与 DotCraft 协作。

![repl](https://github.com/DotCraftDev/resources/raw/master/dotcraft/repl.gif)

### AppServer

AppServer 将 DotCraft 的 Agent 能力以 wire protocol（JSON-RPC）方式通过 stdio 或 WebSocket 暴露，支持远程 CLI 连接、多客户端接入和任意语言的自定义集成。详见 [AppServer 模式指南](./docs/appserver_guide.md)。

### Desktop 桌面应用

DotCraft Desktop 是一个基于 Electron + React 的桌面应用，为 DotCraft Agent Harness 提供图形化界面。它通过 Wire Protocol 连接 AppServer，提供三栏式工作区，支持多会话管理、实时流式对话、内联 Diff 查看与一键回滚、审批流程、计划追踪和定时任务管理。支持 Windows、macOS 和 Linux。

详见 [Desktop Client README](./desktop/README_ZH.md)。

### 编辑器与 ACP

DotCraft 支持 ACP 兼容编辑器，包括 Unity、Obsidian 和 JetBrains 系列 IDE。你可以先查看 [ACP 模式指南](./docs/acp_guide.md)；如果你主要在 Unity 中使用，再查看 [Unity 集成指南](./docs/unity_guide.md) 与 [Unity Client README](./src/DotCraft.UnityClient/Packages/com.dotcraft.unityclient/README.md)。

![unity](https://github.com/DotCraftDev/resources/raw/master/dotcraft/unity.gif)

### API / AG-UI

把 DotCraft 作为服务接入其他应用，或对接前端交互体验。可查看 [API 模式指南](./docs/api_guide.md) 和 [AG-UI 模式指南](./docs/agui_guide.md)。

![agui](https://github.com/DotCraftDev/resources/raw/master/dotcraft/agui.gif)

### QQ / 企业微信

把同一个工作区接入聊天机器人入口。可查看 [QQ 机器人指南](./docs/qq_bot_guide.md) 和 [企业微信指南](./docs/wecom_guide.md)。

![qqbot](https://github.com/DotCraftDev/resources/raw/master/dotcraft/qqbot.gif)

### External Channels（外部渠道适配器）

除了内建渠道外，DotCraft 还可以通过 AppServer Wire Protocol 接入外部渠道，因此你可以将 Telegram、Discord、Slack，或企业内部 IM 等平台接入同一个工作区，而不需要把适配器嵌入主进程。

Python SDK（`DotCraftClient`、`ChannelAdapter`）便于接入外部渠道。各平台适配器可用原生 UI 呈现审批流程，整体适配也更灵活。

仓库中已经包含一个 Telegram 参考适配器，演示了 long polling、Inline Keyboard 审批以及与 DotCraft 会话模型的完整闭环。可进一步查看 [Python SDK](./sdk/python/README.md)。

![telegram](https://github.com/DotCraftDev/resources/raw/master/dotcraft/telegram.jpg)

### GitHub 工作流自动化

DotCraft 可以自动轮询 GitHub 的 Issue 和 Pull Request、创建隔离工作区、派发开发或 Review Agent，并在多轮运行中完成交接。详见 [GitHubTracker 指南](./docs/github_tracker_guide.md)。

![github-tracker](https://github.com/DotCraftDev/resources/raw/master/dotcraft/github-tracker.png)

## 🛡️ 运行与治理

### Dashboard

DotCraft 内置 Dashboard，可用于查看会话、追踪调用和编辑配置。首次缺少 `ApiKey` 时，它也会以 setup-only 模式承担初始配置入口。详见 [Dashboard 指南](./docs/dash_board_guide.md)。

![dashboard](https://github.com/DotCraftDev/resources/raw/master/dotcraft/dashboard.png)

<div align="center">
用量、会话统计，按渠道汇总。
</div>

![trace](https://github.com/DotCraftDev/resources/raw/master/dotcraft/trace.png)

<div align="center">
完整记录工具调用、会话历史。
</div>

### 沙箱隔离

如果你希望把 Shell 和文件工具放到隔离环境中执行，DotCraft 支持 [OpenSandbox](https://github.com/alibaba/OpenSandbox)。安装、配置和安全细节请参阅 [配置指南](./docs/config_guide.md)。

### MCP 工具延迟加载

当接入的 MCP 服务器较多时，将所有工具定义一次性注入上下文会带来显著的 Token 开销，并可能降低模型的工具选择精度。延迟加载让 Agent 通过 `SearchTools` 按需发现并激活 MCP 工具，而非在会话开始时全量注入。工具激活后立即可用，且在会话内单调递增，确保 Prompt Cache 可以稳定复用。

配置详情和推荐的 Skill 引导模式请参阅 [配置指南](./docs/config_guide.md#mcp-工具延迟加载)。

### 工作区定制

你可以通过 `.craft/AGENTS.md`、`.craft/USER.md`、`.craft/SOUL.md`、`.craft/TOOLS.md`、`.craft/IDENTITY.md` 等文件定制 Agent 行为，也可以通过 `.craft/commands/` 添加自定义命令。具体用法建议参考对应文档和示例。

## 📚 文档导航

**配置与运行**

- [配置指南](./docs/config_guide.md)：配置项、工具、安全、审批、MCP、沙箱、Gateway
- [Dashboard 指南](./docs/dash_board_guide.md)：Dashboard 页面、调试能力与可视化配置
- [GitHubTracker 指南](./docs/github_tracker_guide.md)：Issue 与 PR 编排、隔离工作区、Agent 派发与交接机制

**入口能力**

- [AppServer 模式指南](./docs/appserver_guide.md)：Wire Protocol 服务器、WebSocket 传输、远程 CLI 连接
- [Desktop Client 指南](./desktop/README_ZH.md)：Electron 桌面应用，构建、启动与功能概览
- [API 模式指南](./docs/api_guide.md)：OpenAI 兼容 API、工具过滤、SDK 示例
- [AG-UI 模式指南](./docs/agui_guide.md)：AG-UI 协议 SSE 服务端、CopilotKit 集成
- [QQ 机器人指南](./docs/qq_bot_guide.md)：NapCat、权限与审批
- [企业微信指南](./docs/wecom_guide.md)：企业微信推送与机器人模式
- [ACP 模式指南](./docs/acp_guide.md)：编辑器/IDE 集成（JetBrains、Obsidian 等）
- [外部渠道适配器规范](./specs/external-channel-adapter.md)：面向进程外渠道适配器的 Wire Protocol 契约
- [Python SDK](./sdk/python/README.md)：使用 `dotcraft-wire` 与 Telegram 参考示例构建外部适配器

**编辑器与扩展**

- [Unity 集成指南](./docs/unity_guide.md)：Unity 编辑器扩展与 AI 驱动的场景和资源工具
- [Hooks 指南](./docs/hooks_guide.md)：生命周期事件钩子、Shell 命令扩展、安全防护
- [文档索引](./docs/index.md)：完整文档导航

**TUI**

- [Rust TUI 指南](./tui/README_ZH.md)：构建方式、启动模式、快捷键、斜杠命令和主题配置

## 🤝 贡献指南

我们欢迎各种形式的贡献！无论是修复 Bug、添加新功能还是改进文档，我们都非常感谢。

**开始贡献**：请参阅 [CONTRIBUTING.md](./CONTRIBUTING.md) 了解开发规范。

你可以选择使用 AI 辅助或手动开发——规范同时支持两种方式。

## 🙏 致谢

本项目受 nanobot 和 Codex 启发，基于微软 Agent Framework 打造。

感谢 [Devin AI](https://devin.ai/) 提供了免费的 ACU 额度为开发提供便捷。

- [HKUDS/nanobot](https://github.com/HKUDS/nanobot)
- [openai/codex](https://github.com/openai/codex)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework)
- [alibaba/OpenSandbox](https://github.com/alibaba/OpenSandbox)
- [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)
- [agentclientprotocol/agent-client-protocol](https://github.com/agentclientprotocol/agent-client-protocol)
- [ag-ui-protocol/ag-ui](https://github.com/ag-ui-protocol/ag-ui)
- [openai/symphony](https://github.com/openai/symphony)

## 📄 许可证

Apache License 2.0
