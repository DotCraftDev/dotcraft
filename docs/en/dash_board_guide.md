# DotCraft DashBoard Debugging Tool Guide

DotCraft DashBoard is a built-in Web debugging tool for real-time viewing of request/response data, tool call traces, token usage statistics, and other Trace data during Agent execution. Inspired by [Microsoft Agent Framework DevUI](https://learn.microsoft.com/en-us/agent-framework/user-guide/devui/?pivots=programming-language-python) (Python version), but without conversation and workflow orchestration features.

## Feature Overview

| Feature | Description |
|---------|-------------|
| Dashboard | Summary statistics: session count, request count, tool call count, token usage (input/output/total) |
| Sessions | Session list sorted by recent activity, with detail view and deletion |
| Trace Timeline | Event timeline for each session, filterable by type (request, response, tool call, token, error) |
| Settings | Configuration file editor with global config viewing and workspace config editing, real-time merge preview |
| Real-time Updates | Real-time event push to frontend via SSE (Server-Sent Events) |

---

## Quick Start

### 1. Enable DashBoard

Add the `DashBoard` config section in `config.json`:

```json
{
    "DashBoard": {
        "Enabled": true,
        "Host": "127.0.0.1",
        "Port": 5880
    }
}
```

### 2. Start DotCraft

Start DotCraft normally (supported in all modes). The console will output the DashBoard address:

```
DashBoard started at http://127.0.0.1:5880/dashboard
```

### 3. Open DashBoard

Visit the address shown in the console (default `http://127.0.0.1:5880/dashboard`) in your browser to see the DashBoard interface.

### 4. Trigger Agent Execution

Send messages to the Agent via CLI, QQ, WeCom, or API. The DashBoard will display all events during execution in real time.

---

## Configuration

| Config Item | Type | Default | Description |
|-------------|------|---------|-------------|
| `DashBoard.Enabled` | bool | `false` | Enable DashBoard debugging tool |
| `DashBoard.Host` | string | `"127.0.0.1"` | DashBoard Web service listen address |
| `DashBoard.Port` | int | `5880` | DashBoard Web service listen port |

> **Security Note**: DashBoard exposes Agent execution details (including prompt content, tool call parameters and results). Only enable in development/debugging environments and bind to `127.0.0.1` to prevent external access.

---

## Runtime Modes

DashBoard behavior varies slightly across runtime modes:

| Runtime Mode | DashBoard Startup | Access URL |
|-------------|-------------------|------------|
| CLI | Standalone Web server | `http://{DashBoard.Host}:{DashBoard.Port}/dashboard` |
| QQ Bot | Standalone Web server | `http://{DashBoard.Host}:{DashBoard.Port}/dashboard` |
| WeCom Bot | Standalone Web server | `http://{DashBoard.Host}:{DashBoard.Port}/dashboard` |
| API | Integrated into API server | `http://{Api.Host}:{Api.Port}/dashboard` |

> **API Mode Note**: In API mode, DashBoard routes are registered directly on the existing API server without starting a separate server, so use the API address and port to access. `DashBoard.Host` and `DashBoard.Port` are not effective in API mode.

---

## Trace Event Types

Event types captured by DashBoard:

| Event Type | Description | Data Included |
|-----------|-------------|---------------|
| `Request` | User request received by Agent | Request content (prompt) |
| `Response` | Agent's final reply | Reply content |
| `ToolCallStarted` | Tool call started | Tool name, icon, call parameters |
| `ToolCallCompleted` | Tool call completed | Tool name, return result, duration (ms) |
| `TokenUsage` | Token usage report | Input token count, output token count |
| `ContextCompaction` | Context compaction triggered | No additional data |
| `Error` | Error during execution | Error message |

---

## API Endpoints

DashBoard provides the following REST API, all prefixed with `/api/`:

### GET /DashBoard

Returns the DashBoard frontend page (HTML/CSS/JS embedded SPA).

### GET /DashBoard/api/summary

Get global statistics summary.

**Response example**:

```json
{
    "sessionCount": 3,
    "totalRequests": 12,
    "totalToolCalls": 28,
    "totalInputTokens": 45000,
    "totalOutputTokens": 8500,
    "totalTokens": 53500
}
```

### GET /DashBoard/api/sessions

Get all sessions, sorted by most recent activity in descending order.

**Response example**:

```json
[
    {
        "sessionKey": "cli:default",
        "startedAt": "2025-01-01T10:00:00.000Z",
        "lastActivityAt": "2025-01-01T10:05:30.000Z",
        "totalInputTokens": 15000,
        "totalOutputTokens": 3200,
        "totalTokens": 18200,
        "requestCount": 4,
        "toolCallCount": 10
    }
]
```

### GET /DashBoard/api/sessions/{sessionKey}/events

Get all events for a specified session, sorted by time in ascending order.

**Response example**:

```json
[
    {
        "id": "a1b2c3d4e5f6",
        "type": "Request",
        "sessionKey": "cli:default",
        "timestamp": "2025-01-01T10:00:00.000Z",
        "content": "Search for the latest AI news"
    },
    {
        "id": "f6e5d4c3b2a1",
        "type": "ToolCallStarted",
        "sessionKey": "cli:default",
        "timestamp": "2025-01-01T10:00:01.000Z",
        "toolName": "WebSearch",
        "toolIcon": "🔍",
        "toolArguments": "{\"query\":\"latest AI news 2025\"}"
    }
]
```

### DELETE /api/sessions/{sessionKey}

Delete all Trace data for a specified session.

### DELETE /api/sessions

Clear all session Trace data.

### GET /api/events/stream

SSE (Server-Sent Events) endpoint for real-time Trace event push.

**Usage**:

```javascript
const eventSource = new EventSource('/dashboard/api/events/stream');
eventSource.onmessage = (event) => {
    const traceEvent = JSON.parse(event.data);
    console.log(traceEvent);
};
```

---

## Frontend Interface

The DashBoard frontend is an embedded Single Page Application (SPA) that requires no additional build steps and is rendered directly by the server.

### Dashboard Page

Displays global statistics cards:

- **Sessions** - Active session count
- **Requests** - Total request count
- **Tool Calls** - Total tool call count
- **Input Tokens** - Total input token count
- **Output Tokens** - Total output token count
- **Total Tokens** - Total token count

### Sessions Page

- Session list showing each session's Key, request count, tool call count, and token statistics
- Click a session to enter Trace Timeline for detailed events
- Supports deleting individual sessions or clearing all sessions

### Trace Timeline Page

- Displays all events in chronological order within a session
- Supports filtering by event type: All / Requests / Responses / Tools / Tokens / Errors
- Each event can be expanded to view detailed content (request content, tool parameters, return results, etc.)
- Tool call events show duration
- Supports auto-scroll to latest event (toggleable)

### Settings Page

Configuration file editor that supports:
- View global config (read-only, typically at `~/.craft/config.json`)
- Edit workspace config (writable, at `.craft/config.json`)
- Real-time preview of merged global and workspace config
- Support for tri-state boolean values (inherit/on/off)
- Requires DotCraft restart after saving config to take effect

---

## Configuration Examples

### Basic Development Debugging

```json
{
    "ApiKey": "sk-your-llm-key",
    "Model": "gpt-4o-mini",
    "DashBoard": {
        "Enabled": true
    }
}
```

Access at default address `http://127.0.0.1:5880/dashboard`.

### API Mode + DashBoard

```json
{
    "ApiKey": "sk-your-llm-key",
    "Model": "gpt-4o-mini",
    "Api": {
        "Enabled": true,
        "Port": 8080,
        "AutoApprove": true
    },
    "DashBoard": {
        "Enabled": true
    }
}
```

Access DashBoard at `http://localhost:8080/dashboard` (integrated into API server).

### Custom Port

```json
{
    "DashBoard": {
        "Enabled": true,
        "Host": "0.0.0.0",
        "Port": 9090
    }
}
```

> **Note**: Setting `Host` to `0.0.0.0` allows external network access to DashBoard. Ensure the network environment is secure.
---

## Related Documentation

- [Configuration Guide](./config_guide.md) - Complete configuration reference
- [API Mode Guide](./api_guide.md) - OpenAI-compatible API service
- [QQ Bot Guide](./qq_bot_guide.md) - QQ Bot mode
- [WeCom Guide](./wecom_guide.md) - WeCom Bot mode
- [Documentation Index](./index.md) - Full documentation navigation
