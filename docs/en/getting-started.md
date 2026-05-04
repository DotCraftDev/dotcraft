# DotCraft Getting Started

This path is for first-time DotCraft users: install Desktop, choose a project folder, configure a model provider, then run your first session. After that, move into TUI, AppServer, API, SDK, or Automations as needed.

## Quick Start

### 1. Download Desktop

Go to [GitHub Releases](https://github.com/DotHarness/dotcraft/releases) and download the desktop app:

| Platform | Recommended file |
|----------|------------------|
| Windows | `DotCraft-Desktop-win-x64-Setup.exe` |
| macOS | `DotCraft-Desktop-macos-x64.dmg` |

Desktop is the recommended first entry point because workspace selection, model configuration, sessions, diffs, plans, automation review, and runtime status live in one UI.

![DotCraft Desktop](https://github.com/DotHarness/resources/raw/master/dotcraft/desktop.png)

If you prefer building from source, install the [.NET 10 SDK](https://dotnet.microsoft.com/download), Rust toolchain, and Node.js, then run this from the repository root:

```bash
build.bat
```

On Linux / macOS, run:

```bash
bash build_linux.bat
```

### 2. Initialize a Workspace

On first launch, choose a real project folder as the workspace. DotCraft keeps that project's configuration, sessions, tasks, skills, and attachments with the project, so Desktop, terminal, and automation entry points can continue from the same context.

Start from a real project folder instead of an empty directory so the agent can read repository structure, existing docs, and build scripts.

![Workspace setup wizard](https://github.com/DotHarness/resources/raw/master/dotcraft/setup.png)

### 3. Configure a Model

DotCraft supports two common model paths:

| Path | Best for |
|------|----------|
| OpenAI-compatible API key | OpenAI API, OpenRouter, DeepSeek, and compatible providers |
| CLIProxyAPI | Reusing a local coding-agent CLI through an OpenAI-compatible proxy |

![API proxy configuration](https://github.com/DotHarness/resources/raw/master/dotcraft/api-proxy.png)

The minimal configuration usually looks like this:

```json
{
  "ApiKey": "sk-your-api-key",
  "Model": "gpt-4o-mini",
  "EndPoint": "https://api.openai.com/v1"
}
```

Put sensitive values in global configuration, and put project-specific model, tool, and entry-point settings in the current workspace configuration. If you need to edit files directly, the paths are global `~/.craft/config.json` and workspace `<workspace>/.craft/config.json`. See [Configuration & Security](./config_guide.md) for the full reference.

### 4. Run the First Session

Open the workspace in Desktop, create a session, and send a lightweight request:

```text
Read this repository's README and docs/index.md, then tell me how to start the project.
```

If you prefer a script-friendly command-line entry, run a one-shot task from the project directory:

```bash
dotcraft exec "Read this repository's README and docs/index.md, then tell me how to start the project."
```

For a richer terminal UI, continue with the [TUI Guide](./tui_guide.md).

## Understand the Entry Model

DotCraft organizes its entry points around the **Unified Session Core**: command-line runs, Desktop, IDEs, bots, and automations do not each maintain their own agent loop, but reuse the same execution engine and session model.

| Dimension | Gateway | Unified Session Core |
|-----------|---------|----------------------|
| Client customization | Hard to customize once everything is flattened into a message bus | Flexible, native client experiences |
| Approval / HITL | Cannot express platform-native approval flows | Rendered with native platform UI |
| Cross-channel resume | Not supported | Conversations can resume across channels |
| Workspace persistence | Not supported | Designed around the workspace |

![Unified entry model](https://github.com/DotHarness/resources/raw/master/dotcraft/entry.png)

DotCraft connects different entry points to the same project-scoped workspace, while the Unified Session Core handles execution, state, and orchestration.

## Configuration

First-time setup only needs a few fields:

| Field | Purpose | Recommended location |
|-------|---------|----------------------|
| `ApiKey` | Model API key | Global config |
| `Model` | Default model name | Global or workspace config |
| `EndPoint` | OpenAI-compatible API URL | Global or workspace config |
| `Language` | UI language: `Chinese` / `English` | Global config |
| `DashBoard.Enabled` | Enable web debugging and visual configuration | Workspace config |

If unsure, put the API key globally and everything else in the workspace.

## Choose the Next Step by Goal

| Goal | Next step |
|------|-----------|
| Work visually with sessions and diffs | [Desktop Guide](./desktop_guide.md) |
| Use a full terminal interface | [TUI Guide](./tui_guide.md) |
| Share a workspace across remote or multiple clients | [AppServer Mode Guide](./appserver_guide.md) |
| Expose an OpenAI-compatible HTTP API | [API Mode Guide](./api_guide.md) |
| Connect an IDE or editor | [ACP Mode Guide](./acp_guide.md) |
| Build bots or external adapters | [SDK Overview](./sdk/index.md) |
| Run local or GitHub automation tasks | [Automations Guide](./automations_guide.md) |
| Inspect traces, tool calls, and merged configuration | [Dashboard Guide](./dash_board_guide.md) |

## Explore More

### Social Channels

DotCraft integrates with Telegram, WeChat, Feishu/Lark, QQ, WeCom, and other social channels through SDK extensions. Start with the [Python SDK](./sdk/python.md) and [TypeScript SDK](./sdk/typescript.md).

| Telegram (Python SDK) | WeChat (TypeScript SDK) |
|:---:|:---:|
| ![Telegram channel example](https://github.com/DotHarness/resources/raw/master/dotcraft/telegram.jpg) | ![WeChat channel example](https://github.com/DotHarness/resources/raw/master/dotcraft/wechat.jpg) |

### Automations

Automations are for running local tasks and GitHub-driven workflows. Scheduling, review, and retry flows are covered in the [Automations Guide](./automations_guide.md).

| Desktop Automations | GitHub tracker |
|:---:|:---:|
| ![Desktop automations panel](https://github.com/DotHarness/resources/raw/master/dotcraft/desktop_github.png) | ![GitHub tracker example](https://github.com/DotHarness/resources/raw/master/dotcraft/github-tracker.png) |

### Dashboard

Dashboard is DotCraft's visual inspection and configuration surface for sessions, traces, and workspace settings. See the [Dashboard Guide](./dash_board_guide.md) for the full page overview.

| Usage overview | Session trace |
|:---:|:---:|
| ![Dashboard usage overview](https://github.com/DotHarness/resources/raw/master/dotcraft/dashboard.png) | ![Dashboard session trace](https://github.com/DotHarness/resources/raw/master/dotcraft/trace.png) |

## Advanced Topics

- Use [Hooks](./hooks_guide.md) to run scripts on lifecycle events.
- Use [sandbox and security settings](./config_guide.md#security-configuration) to constrain file, shell, and network access.
- Use the [Workspace Sample](./samples/workspace.md) to validate a complete workspace template.
- Return to the [Documentation Index](./reference.md) and choose by goal.

## Troubleshooting

### Desktop cannot find `dotcraft`

Make sure the DotCraft CLI is on `PATH`, or set the AppServer / `dotcraft` binary path in Desktop settings. Source-build users can run `build.bat` from the repository root first.

### Model requests fail

Check that `ApiKey`, `Model`, and `EndPoint` belong to the same provider. OpenAI-compatible endpoints usually end with `/v1`.

### Workspace configuration does not apply

Confirm the config is in the current workspace's `.craft/config.json`, then restart Desktop or the relevant host. Some AppServer and entry-point settings are read only at startup.
