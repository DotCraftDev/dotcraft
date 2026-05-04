# DotCraft Configuration Guide

This page is for the first configuration pass: where config files live, how they merge, and which fields matter most. For every field, see [Full Configuration Reference](./reference/config.md). For file, shell, sandbox, and network boundaries, see [Security Configuration](./config/security.md).

## Quick Start

A minimal working configuration only needs model provider details:

```json
{
  "ApiKey": "sk-your-api-key",
  "Model": "gpt-4o-mini",
  "EndPoint": "https://api.openai.com/v1"
}
```

Recommended placement:

| Setting | Location | Why |
|---------|----------|-----|
| API key | `~/.craft/config.json` | Keeps secrets out of project repositories |
| Project model overrides | `<workspace>/.craft/config.json` | Lets each project choose its own model and tools |
| Dashboard / Automations / Gateway | `<workspace>/.craft/config.json` | These usually belong to the project workflow |

If you are unsure: put the API key globally and project-specific behavior in the workspace.

## Configuration

DotCraft reads two configuration layers:

| Config file | Path | Purpose |
|-------------|------|---------|
| Global config | `~/.craft/config.json` | Personal defaults such as API key, model, and language |
| Workspace config | `<workspace>/.craft/config.json` | Project-specific model overrides, tools, entry points, and automations |

### Merge Rules

- Global config provides the base.
- Workspace config overrides global config.
- Fields missing from the workspace keep the global value.

### Example

Global config stores your default API key and model:

```json
{
  "ApiKey": "sk-your-default-api-key",
  "Model": "gpt-4o-mini",
  "EndPoint": "https://api.openai.com/v1"
}
```

Workspace config overrides the current project:

```json
{
  "Model": "deepseek-chat",
  "EndPoint": "https://api.deepseek.com/v1",
  "DashBoard": {
    "Enabled": true
  },
  "Automations": {
    "Enabled": true
  }
}
```

DotCraft will use the global `ApiKey` and the workspace `Model`, `EndPoint`, Dashboard, and Automations settings.

### Common Fields

| Field | Description | Default |
|-------|-------------|---------|
| `ApiKey` | OpenAI-compatible API key | Empty |
| `Model` | Default model name | `gpt-4o-mini` |
| `EndPoint` | API endpoint URL | `https://api.openai.com/v1` |
| `Language` | UI language: `Chinese` / `English` | `Chinese` |
| `EnabledTools` | Globally enabled tool names. Empty enables all tools | `[]` |
| `DebugMode` | Prints untruncated tool arguments in the console | `false` |

DotCraft's base identity is built in. To customize project instructions, working norms, or long-term memory, prefer workspace files such as `.craft/AGENTS.md`, `.craft/SOUL.md`, and `.craft/MEMORY.md`.

## Usage Examples

| Goal | Next step |
|------|-----------|
| Configure API key and default model | Edit global `~/.craft/config.json` |
| Use a different model for one project | Edit that project's `.craft/config.json` |
| Limit file, shell, or network capability | Read [Security Configuration](./config/security.md) |
| Configure SubAgent roles, profiles, and recursion depth | Read [SubAgent Configuration Guide](./subagents_guide.md) |
| See every configuration field | Read [Full Configuration Reference](./reference/config.md) |
| Inspect merged configuration in the UI | Open the Settings page in [Dashboard](./dash_board_guide.md) |

## Advanced Topics

### Startup Settings

AppServer, Gateway, API, AG-UI, external channels, ports, and Dashboard listen addresses are read when a Host starts. Restart the relevant Host or Desktop background process after changing them.

### Runtime Settings

Model, tool filtering, language, and some Agent behavior usually affect new sessions. Whether an existing session changes immediately depends on the entry point and client reload behavior.

### Security Boundary

File access, shell execution, sandboxing, blacklists, and outside-workspace approval are security boundaries. Read [Security Configuration](./config/security.md) before exposing DotCraft through external channels, automations, or network-accessible services.

## Troubleshooting

### Config is written but not applied

Check the layer first: personal defaults go in global config, project behavior goes in workspace config. Startup settings require restarting the relevant Host.

### You do not want API keys committed to the repository

Put `ApiKey` in global `~/.craft/config.json`. Keep workspace config limited to model, endpoint, and project features.

### Dashboard or API ports conflict

Change `DashBoard.Port`, `Api.Port`, `AgUi.Port`, or use Gateway to share a port. Port fields are startup settings and require a restart.

### Tool access is rejected

Check `Security.BlacklistedPaths`, `Tools.File.RequireApprovalOutsideWorkspace`, `Tools.Shell.RequireApprovalOutsideWorkspace`, and sandbox settings.
