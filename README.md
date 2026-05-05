<div align="center">

# DotCraft

**[中文](./README_ZH.md) | English**

[Documentation](https://dotharness.github.io/dotcraft/en/) · [Getting Started](https://dotharness.github.io/dotcraft/en/getting-started) · [Download Release](https://github.com/DotHarness/dotcraft/releases) · [DeepWiki](https://deepwiki.com/DotHarness/dotcraft) · [License](./LICENSE)

A project-scoped agent harness for persistent AI workspaces.

DotCraft connects CLI, Desktop, IDEs, chat bots, APIs, and automations to one workspace so sessions, memory, skills, and tools stay coherent across every entry point.

![intro](https://github.com/DotHarness/resources/raw/master/dotcraft/intro.png)

</div>

## About

DotCraft is a .NET 10 / C# Agent Harness. It organizes AI workflows around a real project folder, allowing multiple entry points to share one session core, configuration, skills, tools, tasks, and observability surface.

- Project-scoped workspace: configuration, sessions, memory, skills, and tasks stay with the project.
- Unified session model: CLI, Desktop, TUI, ACP, SDKs, bots, and Automations reuse the same execution engine.
- Observability and governance: approvals, traces, Dashboard, Hooks, and sandbox settings make agent workflows easier to inspect and control.
- Extensibility and integration: AppServer, OpenAI-compatible API, SDKs, and plugins support custom entry points and business workflows.

## Get Started

For first-time use, start from Desktop:

1. Download the desktop app from [GitHub Releases](https://github.com/DotHarness/dotcraft/releases).
2. Choose a real project folder as your workspace.
3. Configure an OpenAI-compatible API key or CLIProxyAPI.
4. Create a session and send your first repository-understanding request.

See [Getting Started](https://dotharness.github.io/dotcraft/en/getting-started) for the full guided flow with screenshots.

## Documentation

| Goal | Document |
|------|----------|
| Install, configure, and run DotCraft for the first time | [Getting Started](https://dotharness.github.io/dotcraft/en/getting-started) |
| Use the graphical desktop client | [Desktop Guide](https://dotharness.github.io/dotcraft/en/desktop_guide) |
| Use the full terminal interface | [TUI Guide](https://dotharness.github.io/dotcraft/en/tui_guide) |
| Configure models, tools, approvals, and security | [Configuration Guide](https://dotharness.github.io/dotcraft/en/config_guide) |
| Inspect traces, tool calls, and merged configuration | [Dashboard Guide](https://dotharness.github.io/dotcraft/en/dash_board_guide) |
| Run local automation tasks | [Automations Guide](https://dotharness.github.io/dotcraft/en/automations_guide) |
| Connect external clients, bots, or custom adapters | [SDK Overview](https://dotharness.github.io/dotcraft/en/sdk/) |
| Find the full docs path | [Documentation Index](https://dotharness.github.io/dotcraft/en/reference) |

## Contributing

We welcome code, documentation, and integration contributions. Start with [CONTRIBUTING.md](./CONTRIBUTING.md).

## Credits

Inspired by [nanobot](https://github.com/HKUDS/nanobot) and [codex](https://github.com/openai/codex), and built on [agent-framework](https://github.com/microsoft/agent-framework).

Special thanks to:

- [HKUDS/nanobot](https://github.com/HKUDS/nanobot)
- [openai/codex](https://github.com/openai/codex)
- [microsoft/agent-framework](https://github.com/microsoft/agent-framework)
- [alibaba/OpenSandbox](https://github.com/alibaba/OpenSandbox)
- [modelcontextprotocol/csharp-sdk](https://github.com/modelcontextprotocol/csharp-sdk)
- [openai/symphony](https://github.com/openai/symphony)
- [router-for-me/CLIProxyAPI](https://github.com/router-for-me/CLIProxyAPI)

## License

Apache License 2.0
