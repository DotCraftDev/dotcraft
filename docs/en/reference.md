# DotCraft Documentation Index

Choose docs by goal. If this is your first time, start with [Getting Started](./getting-started.md), then move into advanced entry points after Desktop, workspace setup, and model configuration work.

## I Am Using DotCraft for the First Time

| Goal | Recommended doc |
|------|-----------------|
| Download Desktop, initialize a workspace, configure an API key, and run the first session | [Getting Started](./getting-started.md) |
| Use the visual desktop client | [Desktop Guide](./desktop_guide.md) |
| Use a full terminal interface | [TUI Guide](./tui_guide.md) |
| Understand config files, API keys, security boundaries, and tool switches | [Configuration & Security](./config_guide.md) |
| Inspect traces, sessions, tool calls, and visual configuration | [Dashboard Guide](./dash_board_guide.md) |

## I Want to Connect a Client or Editor

| Goal | Recommended doc |
|------|-----------------|
| Connect JetBrains, Obsidian, Unity, and other editors | [ACP Mode Guide](./acp_guide.md) |
| Use the Unity editor extension and scene/asset tools | [Unity Integration Guide](./unity_guide.md) |
| Use an external coding-agent CLI as a subagent | [External CLI Subagents Guide](./external_cli_subagents_guide.md) |
| Understand which settings apply immediately and which require restart | [Settings Lifecycle Guide](./settings-lifecycle.md) |

## I Want DotCraft as a Service or Protocol Backend

| Goal | Recommended doc |
|------|-----------------|
| Run the Wire Protocol service and share a workspace across clients | [AppServer Mode Guide](./appserver_guide.md) |
| Expose an OpenAI-compatible HTTP API | [API Mode Guide](./api_guide.md) |
| Connect AG-UI / CopilotKit frontends | [AG-UI Mode Guide](./agui_guide.md) |
| Read the AppServer protocol details | [AppServer Protocol Spec](https://github.com/DotHarness/dotcraft/blob/master/specs/appserver-protocol.md) |

## I Want Automation or Workflow Extensions

| Goal | Recommended doc |
|------|-----------------|
| Local tasks, GitHub issue/PR orchestration, and human review | [Automations Guide](./automations_guide.md) |
| Lifecycle hooks and shell extensions | [Hooks Guide](./hooks_guide.md) |
| Validate features with complete examples | [Samples Overview](./samples/index.md) |
| Prepare a workspace template | [Workspace Sample](./samples/workspace.md) |

## I Want Bots, SDKs, or External Adapters

| Goal | Recommended doc |
|------|-----------------|
| Choose Python or TypeScript SDK | [SDK Overview](./sdk/index.md) |
| Build external channels in Python | [Python SDK](./sdk/python.md) |
| Reference the Telegram Python adapter | [Python Telegram Adapter](./sdk/python-telegram.md) |
| Build external channel modules in TypeScript | [TypeScript SDK](./sdk/typescript.md) |
| Connect QQ / WeCom / Feishu / Telegram / Weixin | [QQ](./sdk/typescript-qq.md) · [WeCom](./sdk/typescript-wecom.md) · [Feishu](./sdk/typescript-feishu.md) · [Telegram](./sdk/typescript-telegram.md) · [Weixin](./sdk/typescript-weixin.md) |

## Troubleshooting

### Searching Desktop does not find the download path

Open [Getting Started](./getting-started.md) or the [Desktop Guide](./desktop_guide.md). The docs search index should prioritize these pages.

### You are not sure whether config belongs globally or in the workspace

Put API keys in global `~/.craft/config.json`; put project-specific model, tool, entry-point, and automation settings in `<workspace>/.craft/config.json`.

### You want to contribute docs

Docs should stay bilingual: Chinese in `docs/*.md`, English in `docs/en/*.md`. Feature docs should include Quick Start, Configuration, Usage Examples, Advanced Topics, and Troubleshooting.
