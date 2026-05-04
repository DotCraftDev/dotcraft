# DotCraft Dashboard Guide

Dashboard is DotCraft's Web debugging and visual configuration interface. Use it to inspect sessions, traces, tool calls, automation state, and merged configuration. It is the fastest way to answer "what did the Agent do?" and "why did this config apply?"

## Quick Start

### 1. Enable Dashboard

Add this to `.craft/config.json`:

```json
{
  "DashBoard": {
    "Enabled": true,
    "Host": "127.0.0.1",
    "Port": 8080
  }
}
```

### 2. Start DotCraft

```bash
dotcraft gateway
```

### 3. Open Dashboard

Default URL:

```text
http://127.0.0.1:8080/dashboard
```

### 4. Trigger an Agent Run

Start a conversation from CLI, Desktop, TUI, or another entry point. Dashboard will show sessions, tool calls, errors, and configuration state.

## Configuration

| Field | Description | Default |
|-------|-------------|---------|
| `DashBoard.Enabled` | Enables Dashboard | `false` |
| `DashBoard.Host` | Listen address | `127.0.0.1` |
| `DashBoard.Port` | Listen port | `8080` |

Setting `Host` to `0.0.0.0` allows external network access. Dashboard can expose prompts, tool arguments, and tool results, so verify the network boundary first.

## Usage Examples

| Scenario | Use |
|----------|-----|
| Confirm the model can be called | Trigger one session and inspect Trace Timeline |
| Debug a failed tool call | Open session details and filter Tools / Errors |
| Inspect merged configuration | Open Settings and compare global, workspace, and merged config |
| Review automation state | Use the Automations panel with Gateway + Automations |

## Advanced Topics

### Runtime Modes

| Mode | Description |
|------|-------------|
| CLI standalone Dashboard | Debugs the current local entry point |
| Gateway Dashboard | Shares a backend with API, AG-UI, Automations, and external channels |
| Shared port | API, AG-UI, and Dashboard can be merged into one HTTP service by Gateway |

### Frontend Pages

| Page | Purpose |
|------|---------|
| Dashboard | Runtime summary and entry-point state |
| Sessions | Session list and details |
| Trace Timeline | Timeline view of Agent, tool, and error events |
| Settings | Config schema, global config, workspace config, and merged config |
| Automations | Local tasks, Cron, GitHub sources, and review state |

### API Reference

For Dashboard trace event types and HTTP endpoints, see [Dashboard API](./reference/dashboard-api.md).

## Troubleshooting

### Browser cannot open Dashboard

Confirm `DashBoard.Enabled = true` and use the address printed by the console. The default path is `http://127.0.0.1:8080/dashboard`.

### Automations panel is missing

The Automations panel requires Gateway to load the Automations module. CLI standalone Dashboard is best for the current session and does not display the full automation orchestration state.

### Changes in Settings do not appear

Model fields usually affect new sessions. AppServer, port, Gateway, and external channel settings are startup fields and require restarting DotCraft.
