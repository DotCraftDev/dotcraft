<div align="center">

[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/DotHarness/dotcraft)
[![License](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](LICENSE)
[![Docs Site](https://img.shields.io/badge/docs-site-4A7FA5.svg)](https://dotharness.github.io/dotcraft/en/)

**[中文](./README.md) | English**

A project-scoped agent harness for persistent AI workspaces.

*Craft a persistent AI workspace around your project.*

Powered by .NET 10 and a Unified Session Core, DotCraft delivers unified, observable AI orchestration across terminals, desktop apps, IDEs, and instant messaging platforms.

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png)

</div>

## ✨ Highlights

<table>
<tr>
<td width="25%" align="center"><b>📁 Project-Scoped Workspace</b><br/>Agents can truly understand your project without being constrained by a specific client surface</td>
<td width="25%" align="center"><b>⚡ Unified Session Model</b><br/>Span conversations across IM platforms, terminals, desktop apps, editors, and agent workflows</td>
<td width="25%" align="center"><b>🛡️ Observability and Governance</b><br/>Keep agents safe and reliable, with issues easy to inspect and trace</td>
<td width="25%" align="center"><b>🔗 Extensibility and Integration</b><br/>Highly extensible, with fast paths for integrating business workflows</td>
</tr>
</table>

## 🚀 Quick Start

Use DotCraft's most recommended Desktop application as the primary entry point.

![Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/desktop.png)

### Installation

#### Option 1: Download a release build

Download the desktop app from [GitHub Releases](https://github.com/DotHarness/dotcraft/releases):

| Platform | File |
|----------|------|
| Windows  | `DotCraft-Desktop-win-x64-Setup.exe` |
| macOS    | `DotCraft-Desktop-macos-x64.dmg` |

#### Option 2: Build from source

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download)
2. Install the Rust toolchain
3. Install Node.js
4. Run `build.bat`
5. Run `build/release/DotCraft-Desktop-Setup.exe`

### Configure the workspace

On first launch, choose a folder as your workspace and follow the setup wizard to initialize it.

![setup](https://github.com/DotHarness/resources/raw/master/dotcraft/setup.png)

### Configure an API key

DotCraft currently supports two setup paths:

- An OpenAI-compatible API key, such as the official API or providers like OpenRouter
- A Coding Agent CLI reverse proxy based on [CLIProxyAPI](https://github.com/router-for-me/CLIProxyAPI)

![apiproxy](https://github.com/DotHarness/resources/raw/master/dotcraft/api-proxy.png)

### Advanced configuration

For the full configuration surface, see the [Configuration Guide](https://dotharness.github.io/dotcraft/en/config_guide).

For the recommended visual setup flow in the built-in Dashboard, see the [Dashboard Guide](https://dotharness.github.io/dotcraft/en/dash_board_guide).

## 🔌 Entry Points

DotCraft organizes its entry points around the **Unified Session Core**: CLI, Desktop, IDEs, bots, and automations do not each maintain their own agent loop, but reuse the same execution engine and session model.

Here is how that differs from a traditional gateway-style architecture:

| Dimension | Gateway | Unified Session Core |
|-----------|-----------------------------------|----------|
| Client customization | Hard to customize once everything is flattened into a message bus | Flexible, native client experiences |
| Approval / HITL | Cannot express platform-native approval flows | Rendered with native platform UI |
| Cross-channel resume | Not supported | Conversations can resume across channels |
| Workspace persistence | Not supported | Designed around the workspace |

![entry](https://github.com/DotHarness/resources/raw/master/dotcraft/entry.png)

<div align="center">DotCraft connects different entry points to the same project-scoped workspace, while the Unified Session Core handles execution, state, and orchestration.</div>

You can choose the entry point that best fits your workflow:

| If you want to... | Start here |
|---|---|
| Work in a local terminal | [CLI](#cli) |
| Use a rich terminal UI | [TUI](#tui) |
| Run DotCraft as a headless server | [AppServer](#appserver) |
| Use a graphical desktop client | [Desktop](#desktop) |
| Use DotCraft in an editor or IDE | [Editors and ACP](#editors-and-acp) |
| Connect a chat bot | [Social Channels](#social-channels) |
| Run automations (Local / GitHub) | [Automations](#automations) |

### CLI

CLI is the most direct entry point for working with DotCraft in a local project directory. It is also the default starting point for understanding the overall workflow before expanding into AppServer, Desktop, or automation scenarios.

### TUI

TUI is for users who want a richer terminal experience. It is built on Ratatui, connects to AppServer over the Wire Protocol, and reuses the same session capabilities.

### AppServer

AppServer is DotCraft's unified backend boundary for exposing capabilities over a JSON-RPC Wire Protocol via stdio or WebSocket. It is the right entry point for remote CLI, multi-client access, and custom integrations in any language. See the [AppServer Guide](https://dotharness.github.io/dotcraft/en/appserver_guide).

### Desktop

Desktop is for users who want a graphical workspace for conversations, diffs, plans, and automation review. It acts as a graphical AppServer client and consumes the same session, approval, and automation capabilities over the Wire Protocol. See the [Desktop Client README](./desktop/README.md) for details.

### Editors and ACP

Editors and ACP are for users who want DotCraft embedded directly into development tools, including Unity, Obsidian, and JetBrains IDEs. The key idea is not a separate editor-only agent, but an ACP bridge that connects the editor to the same AppServer runtime. Start with the [ACP Mode Guide](https://dotharness.github.io/dotcraft/en/acp_guide); for Unity specifically, see the [Unity Integration Guide](https://dotharness.github.io/dotcraft/en/unity_guide) and the [Unity Client README](./src/DotCraft.UnityClient/Packages/com.dotcraft.unityclient/README.md).

### Social Channels

QQ / WeCom are DotCraft's native social channels and require no extra dependencies. For setup details, see the [QQ Bot Guide](https://dotharness.github.io/dotcraft/en/qq_bot_guide) and [WeCom Guide](https://dotharness.github.io/dotcraft/en/wecom_guide).

For more social channels, DotCraft integrates through SDK-based extensions. See the [Python SDK](./sdk/python/README.md) and [TypeScript SDK](./sdk/typescript/README.md).

DotCraft currently includes integrations for Telegram, WeChat, and Feishu/Lark.

| Telegram (Python SDK) | WeChat (TypeScript SDK) |
|:---:|:---:|
| ![telegram](https://github.com/DotHarness/resources/raw/master/dotcraft/telegram.jpg) | ![wechat](https://github.com/DotHarness/resources/raw/master/dotcraft/wechat.jpg) |

### Automations

Automations are for running local tasks and GitHub-driven workflows. See the [Automations Guide](https://dotharness.github.io/dotcraft/en/automations_guide).

| Desktop Automations | GitHub tracker |
|:---:|:---:|
| ![desktop-github](https://github.com/DotHarness/resources/raw/master/dotcraft/desktop_github.png) | ![github-tracker](https://github.com/DotHarness/resources/raw/master/dotcraft/github-tracker.png) |
| View automated tasks in the desktop application. | Automatic PR reviews. |

## 🛡️ Observability and Governance

### Dashboard

Dashboard is DotCraft's visual inspection and configuration surface for sessions, traces, and workspace settings. See the [Dashboard Guide](https://dotharness.github.io/dotcraft/en/dash_board_guide) for details.

| Usage overview | Session trace |
|:---:|:---:|
| ![dashboard](https://github.com/DotHarness/resources/raw/master/dotcraft/dashboard.png) | ![trace](https://github.com/DotHarness/resources/raw/master/dotcraft/trace.png) |
| Usage and session statistics, aggregated by channel. | Complete record of tool calls and session history. |

### Sandbox Isolation

Sandbox Isolation is for scenarios where Shell and File tools should run inside a controlled execution boundary with stronger host isolation. Installation, configuration, and security details are covered in the [Configuration Guide](https://dotharness.github.io/dotcraft/en/config_guide).

## 📚 Documentation

For the full docs, visit the [Official Documentation](https://dotharness.github.io/dotcraft/en/).

**I want to use DotCraft directly in a repo**

- [Configuration Guide](https://dotharness.github.io/dotcraft/en/config_guide): configuration, tools, security, approvals, MCP, sandbox, startup modes, Gateway
- [Dashboard Guide](https://dotharness.github.io/dotcraft/en/dash_board_guide): Dashboard pages, debugging, and visual configuration
- [Automations Guide](https://dotharness.github.io/dotcraft/en/automations_guide): local tasks and GitHub issue/PR orchestration, agent dispatch, and human review flow
- [Rust TUI Guide](./tui/README.md): build, launch modes, key bindings, slash commands, and theme configuration

**I want to connect DotCraft to an editor or client**

- [Desktop Client Guide](./desktop/README.md): Electron desktop application, build, launch, and feature overview
- [ACP Mode Guide](https://dotharness.github.io/dotcraft/en/acp_guide): editor/IDE integration (JetBrains, Obsidian, and more)
- [Unity Integration Guide](https://dotharness.github.io/dotcraft/en/unity_guide): Unity Editor extension and AI-powered scene and asset tools

**I want to use DotCraft as a server or backend**

- [AppServer Guide](https://dotharness.github.io/dotcraft/en/appserver_guide): wire protocol server, WebSocket transport, remote CLI

**I want to build bots, adapters, or extensions**

- [QQ Bot Guide](https://dotharness.github.io/dotcraft/en/qq_bot_guide): NapCat, permissions, and approvals
- [WeCom Guide](https://dotharness.github.io/dotcraft/en/wecom_guide): WeCom push notifications and bot mode
- [Python SDK](./sdk/python/README.md): build external adapters with `dotcraft-wire` and the Telegram reference example
- [TypeScript SDK](./sdk/typescript/README.md): build external adapters with `dotcraft-wire` for WeChat, Feishu, and similar channels
- [Hooks Guide](https://dotharness.github.io/dotcraft/en/hooks_guide): lifecycle hooks, shell extensions, and security guards

## 🤝 Contributing

We welcome code, documentation, and integration contributions. Start with [CONTRIBUTING.md](./CONTRIBUTING.md).

## 🙏 Credits

Inspired by [nanobot](https://github.com/HKUDS/nanobot) and [codex](https://github.com/openai/codex), and built on [Agent Framework](https://github.com/microsoft/agent-framework).

- [HKUDS/nanobot](https://github.com/HKUDS/nanobot)
- [openai/codex](https://github.com/openai/codex)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework)
- [alibaba/OpenSandbox](https://github.com/alibaba/OpenSandbox)
- [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)
- [openai/symphony](https://github.com/openai/symphony)
- [router-for-me/CLIProxyAPI](https://github.com/router-for-me/CLIProxyAPI)

## 📄 License

Apache License 2.0
