# DotCraft Documentation Index

Choose a document by goal. First-time users should start with [Getting Started](./getting-started.md), finish Desktop, workspace, and model configuration, then move into daily use, automation, or developer integration.

## First-Time Use

| Goal | Recommended doc |
|------|-----------------|
| Download Desktop, initialize a workspace, configure API key, and run once | [Getting Started](./getting-started.md) |
| Use the graphical desktop client | [Desktop Guide](./desktop_guide.md) |
| Use the full terminal interface | [TUI Guide](./tui_guide.md) |
| Configure API key, model, endpoint, and workspace overrides | [Configuration Guide](./config_guide.md) |

## Daily Use and Troubleshooting

| Goal | Recommended doc |
|------|-----------------|
| Inspect traces, sessions, tool calls, and visual config | [Dashboard Guide](./dash_board_guide.md) |
| Understand when settings apply and what requires restart | [Settings Lifecycle Guide](./settings-lifecycle.md) |
| Install, enable, try, or remove DotCraft plugins | [Install and Use Plugins](./plugins/install.md) |
| Let Agents create, patch, and maintain workspace skills | [Agent Skill Self-Learning](./skills/agent-self-learning.md) |
| Search local skills and install third-party skills from SkillHub / ClawHub | [Search and Install Skills](./skills/marketplace.md) |
| Limit file, shell, network, and sandbox capability | [Security Configuration](./config/security.md) |

## Automation and Workflow Extension

| Goal | Recommended doc |
|------|-----------------|
| Local tasks, Cron choice, and human review | [Automations Guide](./automations_guide.md) |
| GitHub Issue/PR automation | [GitHub Automations](./automations/github.md) |
| Automations fields, template variables, and tools | [Automations Reference](./automations/reference.md) |
| Lifecycle Hooks and shell extension | [Hooks Guide](./hooks_guide.md) |
| Hook events, payloads, exit codes, and examples | [Hooks Reference](./hooks/reference.md) |
| Prepare local plugin manifests and plugin-contained skills | [Build Plugins](./plugins/build.md) |
| Use an external coding agent CLI as a subagent | [External CLI Subagents Guide](./external_cli_subagents_guide.md) |

## Clients and Editors

| Goal | Recommended doc |
|------|-----------------|
| Connect JetBrains, Obsidian, Unity, and other editors | [ACP Mode Guide](./acp_guide.md) |
| Use the Unity editor extension and scene/resource tools | [Unity Integration Guide](./unity_guide.md) |
| Manage local workspace runtimes and visual entry points with Hub | [Hub Local Management Guide](./hub_guide.md) |
| Run Wire Protocol service and share a workspace across clients | [AppServer Mode Guide](./appserver_guide.md) |
| Implement a local Hub client that discovers and manages workspace AppServers | [Hub Protocol](./reference/hub-protocol.md) |
| Implement an AppServer JSON-RPC client | [AppServer Protocol](./reference/appserver-protocol.md) |
| Expose an OpenAI-compatible HTTP API | [API Mode Guide](./api_guide.md) |
| Connect AG-UI / CopilotKit frontends | [AG-UI Mode Guide](./agui_guide.md) |

## Bots, SDKs, and External Adapters

| Goal | Recommended doc |
|------|-----------------|
| Choose Python or TypeScript SDK | [SDK Overview](./sdk/index.md) |
| Build external channels with Python | [Python SDK](./sdk/python.md) |
| Reference the Telegram Python adapter | [Python Telegram Adapter](./sdk/python-telegram.md) |
| Build external channel modules with TypeScript | [TypeScript SDK](./sdk/typescript.md) |
| Connect QQ / WeCom / Feishu / Telegram / Weixin | [QQ](./sdk/typescript-qq.md) · [WeCom](./sdk/typescript-wecom.md) · [Feishu](./sdk/typescript-feishu.md) · [Telegram](./sdk/typescript-telegram.md) · [Weixin](./sdk/typescript-weixin.md) |

## Reference

| Goal | Recommended doc |
|------|-----------------|
| See every configuration field | [Full Configuration Reference](./reference/config.md) |
| Read the plugin architecture, manifest, and Plugin Function design | [Plugin Architecture Spec](https://github.com/DotHarness/dotcraft/blob/master/specs/plugin-architecture.md) |
| Read the Hub local management protocol | [Hub Protocol](./reference/hub-protocol.md) |
| Read the AppServer protocol developer guide | [AppServer Protocol](./reference/appserver-protocol.md) |
| See Dashboard API and trace events | [Dashboard API](./reference/dashboard-api.md) |
| Validate features with complete examples | [Samples Overview](./samples/index.md) |
| Prepare a workspace template | [Workspace Sample](./samples/workspace.md) |
| Read the complete AppServer protocol spec | [AppServer Protocol Spec](https://github.com/DotHarness/dotcraft/blob/master/specs/appserver-protocol.md) |

## Troubleshooting

### Search does not find the Desktop download path

Open [Getting Started](./getting-started.md) or [Desktop Guide](./desktop_guide.md).

### You are unsure whether config belongs globally or in the workspace

Put API keys in global `~/.craft/config.json`; put project-specific models, tools, entry points, and automation config in `<workspace>/.craft/config.json`.

### You want to contribute docs

Docs must stay bilingual: Chinese pages live under `docs/*.md`; English pages live under `docs/en/*.md`. Chinese pages use “快速开始 / 配置 / 使用示例 / 进阶 / 故障排查”; English pages use “Quick Start / Configuration / Usage Examples / Advanced Topics / Troubleshooting”.
