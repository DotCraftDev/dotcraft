# AppServer Mode Guide

## Overview

AppServer is DotCraft's wire protocol server that exposes Agent capabilities (session management, tool invocation, approval flows) to external clients via JSON-RPC. Built-in entry points like CLI and ACP communicate with the Agent through AppServer, and you can also start AppServer directly to build custom integrations.

**Use cases**:

- 🔌 Custom IDE / editor integration
- 🌐 Remote development (CLI connecting to a remote AppServer)
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

### Connecting CLI to a Remote AppServer

```bash
# Connect to a running AppServer
dotcraft --remote ws://127.0.0.1:9100/ws

# With token authentication
dotcraft --remote ws://server:9100/ws --token my-secret
```

### Authenticated WebSocket Service

```bash
# Server: listen on all interfaces, require token
dotcraft app-server --listen ws://0.0.0.0:9100 --token my-secret

# Client: connect with token
dotcraft --remote ws://server:9100/ws --token my-secret
```

## Command-Line Reference

### Subcommands and Global Options

| Command / Option | Description |
|------------------|-------------|
| `dotcraft` | Interactive CLI (default mode) |
| `dotcraft app-server` | Start AppServer (defaults to stdio mode) |
| `dotcraft -acp` / `dotcraft acp` | ACP mode (editor / IDE integration) |
| `--listen <URL>` | AppServer transport, used with `app-server` |
| `--remote <URL>` | CLI connects to remote AppServer, used with default mode |
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

AppServer communicates over stdin/stdout using newline-delimited JSON (JSONL). This is the standard subprocess communication method — the CLI automatically starts AppServer as a subprocess by default.

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
dotcraft --remote ws://server:9100/ws --token my-secret
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

**CLI Client Configuration**:

| Config Item | Description | Default |
|-------------|-------------|---------|
| `CLI.AppServerUrl` | Remote AppServer WebSocket URL | empty |
| `CLI.AppServerToken` | Remote connection auth token | empty |
| `CLI.InProcess` | Use in-process mode (for debugging) | `false` |
| `CLI.AppServerBin` | Custom AppServer executable path | empty (uses current process) |

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
│  Client (CLI / IDE / Custom)                    │
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

### Relationship Between CLI and AppServer

By default, the CLI automatically starts `dotcraft app-server` as a subprocess and communicates via stdio. You don't need to manually start AppServer — but the following scenarios require manual management:

| Scenario | Approach |
|----------|----------|
| Local terminal usage | Just run `dotcraft`, AppServer is managed automatically |
| Remote development | Start `dotcraft app-server --listen ws://...` remotely, connect locally with `dotcraft --remote ws://...` |
| Multiple clients sharing workspace | Start WebSocket mode, each client connects independently |
| Custom client integration | Start AppServer, communicate via JSON-RPC in any language |
| Debug Agent behavior | Use `CLI.InProcess = true` to skip subprocess |

## Further Reading

- [Configuration Guide](./config_guide.md): Full configuration reference, including AppServer and CLI config items
- [ACP Mode Guide](./acp_guide.md): Editor / IDE integration (also based on wire protocol)
- [AppServer Protocol Specification](../specs/appserver-protocol.md): Complete JSON-RPC protocol specification (§15 covers WebSocket transport)
