# AppServer Mode Guide

## Overview

AppServer is DotCraft's wire protocol server that exposes Agent capabilities (session management, tool invocation, approval flows) to external clients via JSON-RPC. TUI, Desktop, ACP, external channels, and custom integrations can connect to the same AppServer.

**Use cases**:

- 🔌 Custom IDE / editor integration
- 🌐 Remote development (clients connecting to a remote AppServer)
- 👥 Multiple clients sharing the same workspace
- 🔧 Building non-C# clients (any language with WebSocket / stdio support)

## Quick Start

### Starting AppServer

```bash
# stdio mode (default, for subprocess communication)
dotcraft app-server

# Pure WebSocket mode (for remote connections, multiple clients)
dotcraft app-server --listen ws://127.0.0.1:9100

# stdio + WebSocket dual mode (supports both transports simultaneously)
dotcraft app-server --listen ws+stdio://127.0.0.1:9100
```

### Connecting from the Command Line

```bash
# Run one command against a running AppServer
dotcraft exec --remote ws://127.0.0.1:9100/ws "Summarize this workspace"

# With token authentication
dotcraft exec --remote ws://server:9100/ws --token my-secret "Summarize this workspace"
```

### Authenticated WebSocket Service

```bash
# Server: listen on all interfaces, require token
dotcraft app-server --listen ws://0.0.0.0:9100 --token my-secret

# Client: connect with token
dotcraft exec --remote ws://server:9100/ws --token my-secret "Check status"
```

## Command-Line Reference

### Subcommands and Global Options

| Command / Option | Description |
|------------------|-------------|
| `dotcraft exec <prompt>` | Run one command-line Agent task |
| `dotcraft exec -` | Read input from stdin and run one task |
| `dotcraft app-server` | Start AppServer (defaults to stdio mode) |
| `--listen <URL>` | AppServer transport, used with `app-server` |
| `--remote <URL>` | Client connection to a remote AppServer, used with `exec` or ACP |
| `--token <VALUE>` | WebSocket auth token, used with `--listen` or `--remote` |

### `--listen` URL Schemes

| Scheme | Transport Mode | stdout Behavior | Example |
|--------|---------------|-----------------|---------|
| `stdio://` | Pure stdio (default) | Reserved for JSON-RPC | `--listen stdio://` |
| `ws://host:port` | Pure WebSocket | Normal console output | `--listen ws://127.0.0.1:9100` |
| `wss://host:port` | Pure WebSocket (TLS) | Normal console output | `--listen wss://0.0.0.0:9100` |
| `ws+stdio://host:port` | stdio + WebSocket | Reserved for JSON-RPC | `--listen ws+stdio://127.0.0.1:9100` |

## Transport Modes

### stdio (Default)

AppServer communicates over stdin/stdout using newline-delimited JSON (JSONL). This is the local subprocess communication method commonly used by TUI, Desktop, ACP, and custom clients.

```
Client (stdin) → JSON-RPC Request → AppServer
AppServer → JSON-RPC Response/Notification → Client (stdout)
AppServer → Diagnostic logs → stderr
```

**Characteristics**:
- 1:1 communication (one client per server process)
- stdout is reserved for the wire protocol; console logs go to stderr
- No network configuration needed; ideal for local development

### WebSocket

AppServer starts a WebSocket listener on the specified address. Each WebSocket text frame carries a complete JSON-RPC message.

```bash
dotcraft app-server --listen ws://127.0.0.1:9100
```

**Characteristics**:
- Multiple concurrent client connections (each maintains independent initialization state and thread subscriptions)
- stdout is not reserved; console output works normally
- Supports remote connections and network authentication

### stdio + WebSocket Dual Mode

Starts both transports simultaneously, useful when you need both subprocess communication and remote connections.

```bash
dotcraft app-server --listen ws+stdio://127.0.0.1:9100
```

## Security Authentication

When AppServer listens on a non-loopback address (not `127.0.0.1` / `::1`), **it is strongly recommended** to set up token authentication.

### Server-Side Token Setup

```bash
dotcraft app-server --listen ws://0.0.0.0:9100 --token my-secret
```

### Client-Side Token Usage

```bash
dotcraft exec --remote ws://server:9100/ws --token my-secret "Check status"
```

The token is passed via the WebSocket connection URL query parameter: `ws://host:port/ws?token=<value>`

> ⚠️ **Security Warning**: Binding to `0.0.0.0` without a token leaves AppServer open to all network requests without authentication.

## Configuration

### Command-Line Arguments (Recommended)

Command-line arguments take precedence over config file values. It is recommended to start directly via command line:

```bash
dotcraft app-server --listen ws://127.0.0.1:9100 --token my-secret
```

### config.json Configuration (Alternative)

You can also configure AppServer via `config.json`, suitable for deployments that require fixed configuration:

**AppServer Configuration**:

| Config Item | Description | Default |
|-------------|-------------|---------|
| `AppServer.Mode` | Transport mode: `Disabled` / `Stdio` / `WebSocket` / `StdioAndWebSocket` | `Disabled` |
| `AppServer.WebSocket.Host` | WebSocket listen address | `127.0.0.1` |
| `AppServer.WebSocket.Port` | WebSocket listen port | `9100` |
| `AppServer.WebSocket.Token` | WebSocket auth token | empty |

**Command-Line Client Configuration**:

| Config Item | Description | Default |
|-------------|-------------|---------|
| `CLI.AppServerUrl` | Remote AppServer WebSocket URL used by `dotcraft exec` | empty |
| `CLI.AppServerToken` | Remote connection auth token used by `dotcraft exec` | empty |
| `CLI.AppServerBin` | Custom executable path used when `dotcraft exec` starts the local Hub/AppServer | empty (uses current process) |

**Configuration Examples**:

```json
{
    "AppServer": {
        "Mode": "WebSocket",
        "WebSocket": {
            "Host": "0.0.0.0",
            "Port": 9100,
            "Token": "my-secret"
        }
    }
}
```

```json
{
    "CLI": {
        "AppServerUrl": "ws://server:9100/ws",
        "AppServerToken": "my-secret"
    }
}
```

## How It Works

### Architecture

```
┌─────────────────────────────────────────────────┐
│  Client (TUI / Desktop / ACP / Custom)          │
│  ┌───────────┐  ┌───────────┐  ┌───────────┐   │
│  │  stdin/out │  │ WebSocket │  │ WebSocket │   │
│  └─────┬─────┘  └─────┬─────┘  └─────┬─────┘   │
└────────┼───────────────┼──────────────┼─────────┘
         │               │              │
    ┌────┴───────────────┴──────────────┴────┐
    │         AppServer (JSON-RPC)           │
    │  ┌──────────────────────────────────┐  │
    │  │       ISessionService            │  │
    │  │  (Threads, Sessions, Tools,      │  │
    │  │   Approval Flows)                │  │
    │  └──────────────────────────────────┘  │
    │  ┌───────┐ ┌────────┐ ┌────────────┐  │
    │  │ Tools │ │ Memory │ │   Skills   │  │
    │  └───────┘ └────────┘ └────────────┘  │
    └────────────────────────────────────────┘
```

### Relationship Between Clients and AppServer

Local clients usually start or ensure the workspace AppServer automatically. These scenarios are good fits for manually managing AppServer:

| Scenario | Approach |
|----------|----------|
| One command-line task | Use `dotcraft exec "..."`; the command connects to the backend |
| Remote development | Start `dotcraft app-server --listen ws://...` remotely, then connect clients to WebSocket |
| Multiple clients sharing workspace | Start WebSocket mode, each client connects independently |
| Custom client integration | Start AppServer, communicate via JSON-RPC in any language |

## Usage Examples

| Goal | Recommended approach |
|------|----------------------|
| Run one task from a script | Use `dotcraft exec "..."` |
| Share one backend across Desktop / TUI / ACP | Start `dotcraft app-server --listen ws://127.0.0.1:9100` |
| Connect to a remote workspace | Listen with WebSocket on the server, connect clients to `/ws` |
| Build a custom client | Use JSON-RPC 2.0 over stdio or WebSocket through the Wire Protocol |

## Troubleshooting

### WebSocket clients cannot connect

Confirm the server was started with `--listen ws://...` or `ws+stdio://...`, and the client URL includes the `/ws` path.

### Authentication fails

When the server sets `--token`, TUI, Desktop, ACP, `dotcraft exec`, or custom clients must send the same token. Do not use an empty token for remote deployments.

## Further Reading

- [Configuration Guide](./config_guide.md): Full configuration reference, including AppServer config items
- [ACP Mode Guide](./acp_guide.md): Editor / IDE integration (also based on wire protocol)
- [AppServer Protocol Specification](https://github.com/DotHarness/dotcraft/blob/master/specs/appserver-protocol.md): Complete JSON-RPC protocol specification (§15 covers WebSocket transport)
