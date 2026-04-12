# DotCraft External Channel Adapter Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Living |
| **Date** | 2026-03-19 |
| **Parent Spec** | [AppServer Protocol](appserver-protocol.md) (Section 15) |

Purpose: Define the architecture, protocol extensions, configuration model, and behavioral contract that allow social channel adapters written in any language to integrate with DotCraft as first-class channels, preserving per-platform capabilities such as the Approval flow.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Prerequisites](#2-prerequisites)
- [3. Architecture](#3-architecture)
- [4. Connection Modes](#4-connection-modes)
- [5. Protocol Extensions](#5-protocol-extensions)
- [6. Channel-Specific Server Methods](#6-channel-specific-server-methods)
- [7. ExternalChannelHost](#7-externalchannelhost)
- [8. ExternalChannelManager](#8-externalchannelmanager)
- [9. Configuration](#9-configuration)
- [10. Adapter Behavioral Contract](#10-adapter-behavioral-contract)
- [11. Approval Flow in External Channels](#11-approval-flow-in-external-channels)
- [12. Security](#12-security)
- [13. Reference: Telegram Adapter](#13-reference-telegram-adapter)

---

## 1. Scope

### 1.1 What This Spec Defines

- The connection modes by which an out-of-process channel adapter communicates with a DotCraft server.
- The protocol extensions to the `initialize` handshake that identify a client as a channel adapter.
- The server-to-client extension methods for message delivery, runtime tool calls, and heartbeat.
- The server-side `ExternalChannelHost` and `ExternalChannelManager` components that integrate external adapters into the `GatewayHost`.
- The configuration schema for declaring external channels in `config.json`.
- The behavioral contract that any conforming channel adapter must satisfy.
- The Approval flow contract for external channels.

### 1.2 What This Spec Does Not Define

- The full AppServer wire protocol (message formats, thread/turn methods, event notifications). Those are defined in [appserver-protocol.md](appserver-protocol.md).
- Platform-specific UX (how each platform renders approval prompts, messages, or commands). Those are left entirely to the adapter implementation.
- The C# implementation of native channels (QQ, WeCom). Those are in-process channels that use `ISessionService` directly and are not affected by this spec.
- SDK implementation details. This spec defines the protocol-level contract; SDK authors are free to structure their implementations as they see fit.

### 1.3 Design Principle

The gateway-style architecture used by Nanobot/OpenClaw (a central `MessageBus` with flattened `InboundMessage`/`OutboundMessage`) loses platform-specific capabilities in transit. For DotCraft, the Approval flow — where each platform renders its own native UI (QQ reply, WeCom push, Telegram inline keyboard) — would become impossible to implement correctly under a flattened bus.

The External Channel Adapter pattern instead makes the adapter a **full Wire Protocol client**. The adapter controls the full thread and turn lifecycle, receives all session events including bidirectional approval requests, and remains responsible for platform-specific presentation. The Wire Protocol's JSON-RPC 2.0 framing is language-agnostic, so no C# binding is required.

This mirrors exactly how the CLI already works: the CLI is a Wire Protocol client that bridges a terminal UI to Session Core. An external channel adapter is a Wire Protocol client that bridges a social platform.

---

## 2. Prerequisites

This specification depends on the following:

| Dependency | Reference | Required For |
|------------|-----------|-------------|
| AppServer wire protocol (core) | [appserver-protocol.md §1–14](appserver-protocol.md) | All connection modes |
| WebSocket Transport | [appserver-protocol.md §15](appserver-protocol.md#15-websocket-transport) | WebSocket connection mode |
| `IChannelService` abstraction | `DotCraft.Core/Abstractions/IChannelService.cs` | ExternalChannelHost integration |
| `GatewayHost` | `DotCraft.App/Gateway/GatewayHost.cs` | Lifecycle orchestration |

The WebSocket Transport (appserver-protocol.md §15) must be implemented before `ExternalChannelHost` can use the WebSocket connection mode. The stdio subprocess connection mode reuses the existing `StdioTransport` without additional prerequisites.

---

## 3. Architecture

### 3.1 Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  DotCraft Process (GatewayHost)                                             │
│                                                                             │
│  ┌──────────────────────────────────────────┐                              │
│  │  Native Channels (C#, in-process)        │                              │
│  │  QQChannelService, WeComChannelService   │──┐                           │
│  └──────────────────────────────────────────┘  │                           │
│                                                 ▼                           │
│  ┌──────────────────────────────────────────┐  ┌──────────────────────┐   │
│  │  ExternalChannelManager                  │  │   SessionService     │   │
│  │                                          │  │   (shared)           │   │
│  │  ExternalChannelHost "channel-A"         │──│                      │   │
│  │  (subprocess/stdio transport)    ────────┘  │  AgentExecution      │   │
│  │                                          │  │  Persistence         │   │
│  │  ExternalChannelHost "channel-B"  ───────┘  └──────────────────────┘   │
│  │  (WebSocket transport)                   │                              │
│  └──────────────────────────────────────────┘                              │
└─────────────────────────────────────────────────────────────────────────────┘
         │ stdio (subprocess)          │ WebSocket
         ▼                             ▼
┌─────────────────────┐    ┌───────────────────────┐
│  Any Adapter        │    │  Any Adapter           │
│  (any language)     │    │  (any language)        │
│                     │    │                        │
│  Wire Protocol      │    │  Wire Protocol         │
│  Client             │    │  Client                │
│                     │    │                        │
│  platform SDK       │    │  platform SDK          │
└─────────────────────┘    └───────────────────────┘
         │                             │
    Platform API                  Platform API
```

> The connection mode (subprocess vs WebSocket) is an **operational** choice, not a platform requirement. Any platform adapter can use either mode. See §4.3 for guidance on which mode to choose.

### 3.2 Comparison to Native Channels

| Aspect | Native Channel (QQ/WeCom) | External Channel Adapter |
|--------|--------------------------|--------------------------|
| Language | C# | Any (Python, TypeScript, Go, …) |
| `ISessionService` | In-process direct call | Wire Protocol client |
| `IChannelService` | Implemented directly | Wrapped by `ExternalChannelHost` |
| Approval flow | `QQApprovalService`, `WeComApprovalService` | Adapter-side via `item/approval/request` |
| Lifecycle managed by | `GatewayHost` | `GatewayHost` via `ExternalChannelHost` |
| Platform SDK | In-process (e.g. `QQBotClient`) | Out-of-process (subprocess or networked) |

From `GatewayHost`'s perspective, native channels and external channels are both `IChannelService` instances. The Gateway does not distinguish between them.

---

## 4. Connection Modes

### 4.1 Subprocess (stdio)

DotCraft spawns the adapter as a child process and communicates over the child's stdin/stdout using the standard JSONL Wire Protocol.

```
GatewayHost
  └─ ExternalChannelHost (spawn)
       ├─ stdin  ──► adapter process stdin   (JSON-RPC requests/notifications)
       └─ stdout ◄── adapter process stdout  (JSON-RPC responses/notifications)
```

- DotCraft controls the adapter's lifecycle (start on gateway startup, stop on shutdown).
- The adapter process does not need a network port.
- `ExternalChannelHost` reuses the existing `StdioTransport`.
- `stderr` from the adapter is forwarded to DotCraft's diagnostic log stream.

Best for: single-machine deployments, simple operational model.

### 4.2 WebSocket (connect-out)

The adapter connects to DotCraft's existing AppServer WebSocket endpoint (appserver-protocol.md §15). The same `/ws` endpoint serves both regular AppServer clients (CLI, VS Code) and external channel adapters; the server distinguishes them by the presence of `channelAdapter` in the `initialize` handshake.

```
Adapter process
  └─ Wire Protocol WebSocket client
       └─ connects to ws://{AppServer.WebSocket.Host}:{AppServer.WebSocket.Port}/ws?token={token}
            └─ AppServerHost routes to ExternalChannelHost (via ExternalChannelRegistry)
                 └─ WebSocket connection
```

- The adapter manages its own lifecycle, deployment, and reconnection.
- DotCraft does not spawn the adapter; the adapter must be started separately.
- On connection, the adapter performs the `initialize` handshake with the `channelAdapter` capability (see §5). `AppServerHost` detects the `channelAdapter` capability and routes the connection to the corresponding `ExternalChannelHost` via `ExternalChannelRegistry`.
- No per-channel WebSocket port is needed. All external channel adapters share the AppServer WebSocket endpoint.

Best for: distributed deployments, containerized adapters, adapters that need independent scaling.

### 4.3 Choosing a Mode

The connection mode is an **operational decision** driven by deployment topology, not by which social platform is being integrated. Any platform adapter can use either mode.

Use subprocess mode when:
- All components run on the same machine.
- You want DotCraft to own the adapter's lifecycle (start, restart, stop).
- You want a minimal operational footprint with no exposed network ports.

Use WebSocket mode when:
- The adapter runs in a separate container, VM, or region.
- You need to restart or redeploy the adapter independently without restarting DotCraft.
- You want to run multiple instances of the same adapter concurrently.

---

## 5. Protocol Extensions

### 5.1 `channelAdapter` Capability

External channel adapters extend the standard `initialize` params with a `channelAdapter` capability object. When this object is present, the server treats the connection as a channel adapter and registers it with `ExternalChannelHost`.

**Extended `initialize` params**:

```json
{
  "clientInfo": {
    "name": "telegram-adapter",
    "version": "1.0.0"
  },
  "capabilities": {
    "approvalSupport": true,
    "streamingSupport": true,
    "channelAdapter": {
      "channelName": "telegram",
      "deliverySupport": true,
      "deliveryCapabilities": {
        "structuredDelivery": true,
        "media": {
          "file": {
            "supportsHostPath": false,
            "supportsUrl": false,
            "supportsBase64": true,
            "supportsCaption": true,
            "allowedMimeTypes": ["application/pdf"]
          }
        }
      },
      "channelTools": [
        {
          "name": "TelegramSendDocumentToCurrentChat",
          "description": "Send a document to the current Telegram chat.",
          "requiresChatContext": true,
          "inputSchema": {
            "type": "object",
            "properties": {
              "fileName": { "type": "string" }
            },
            "required": ["fileName"]
          }
        }
      ]
    }
  }
}
```

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `capabilities.channelAdapter` | object | yes (for channel adapters) | Identifies this connection as a channel adapter. Omit for regular clients. |
| `channelAdapter.channelName` | string | yes | The canonical channel name (e.g., `"telegram"`). Must match the name declared in server-side configuration. |
| `channelAdapter.deliverySupport` | boolean | no | Whether this adapter can receive `ext/channel/deliver` server requests. Default `true`. |
| `channelAdapter.deliveryCapabilities` | object | no | Structured delivery capability descriptor. Omit for text-only adapters. |
| `channelAdapter.channelTools` | array | no | Runtime-declared channel tool descriptors exposed to matching-origin threads for the life of this connection. |

When `channelAdapter` is present, the server records the channel name on the connection. The server responds with the standard `initialize` result (see appserver-protocol.md §3.2). No additional fields are added to the response in v1.

If the `channelName` is not recognized in the server configuration, the server closes the connection after the `initialize` response with a `system/event` notification of kind `"channelRejected"`. This prevents unauthorized adapters from registering under arbitrary channel names.

### 5.2 Backward Compatibility

`channelAdapter` is an additive field. Existing clients that do not send it are treated as regular AppServer clients (e.g. CLI, VS Code extension) and are not registered as channel adapters.

`channelTools` is also additive. Adapters that omit it behave exactly like M1/text-only integrations and will never receive `ext/channel/toolCall`.

---

## 6. Channel-Specific Server Methods

These are server-to-client extension methods (under the `ext/channel/` namespace, per appserver-protocol.md §11) used by DotCraft to push information to channel adapters.

### 6.1 `ext/channel/deliver`

Delivers a message to a specific target on the channel. Used by the Cron service, Heartbeat service, and cross-channel message routing. This is the Wire Protocol equivalent of `IChannelService.DeliverMessageAsync` for external channels.

**Direction**: server → client (request, requires response)

**Params**:

| Field | Type | Description |
|-------|------|-------------|
| `target` | string | Platform-specific delivery target. Format is channel-defined (e.g. `"group:12345"`, `"user:67890"`). |
| `content` | string | Message content. Plain text or markdown. |
| `metadata` | object? | Optional channel-specific delivery hints. |

**Example**:

```json
{ "jsonrpc": "2.0", "method": "ext/channel/deliver", "id": 200, "params": {
    "target": "group:12345",
    "content": "Scheduled report: build passed (3/3 tests).",
    "metadata": { "format": "markdown" }
} }
```

**Result**:

```json
{ "delivered": true }
```

If delivery fails, the adapter returns `{ "delivered": false, "errorCode": "...", "errorMessage": "..." }`. A failed delivery logs a warning on the server but does not fail the originating cron job.

If the adapter declared `deliverySupport: false` during `initialize`, the server must not send `ext/channel/deliver` to that adapter.

### 6.2 `ext/channel/send`

Structured delivery request for text and media payloads.

**Direction**: server → client (request, requires response)

**Params**:

| Field | Type | Description |
|-------|------|-------------|
| `target` | string | Platform-specific delivery target. |
| `message` | object | Structured outbound payload. |
| `metadata` | object? | Optional channel-specific hints. |

`message.kind` values standardized in M1:

- `text`
- `file`
- `audio`
- `image`
- `video`

Shared `message` fields:

- `kind: string`
- `text?: string`
- `caption?: string`
- `fileName?: string`
- `mediaType?: string`
- `source?: object`

`source.kind` may be:

- `hostPath`
- `url`
- `dataBase64`
- `artifactId`

Each media capability entry under `channelAdapter.deliveryCapabilities.media` supports:

- `maxBytes?: number`
- `allowedMimeTypes?: string[]`
- `allowedExtensions?: string[]`
- `supportsHostPath: boolean`
- `supportsUrl: boolean`
- `supportsBase64: boolean`
- `supportsCaption: boolean`

**Result**:

```json
{
  "delivered": true,
  "remoteMessageId": "abc123",
  "remoteMediaId": "media_xyz",
  "errorCode": null,
  "errorMessage": null
}
```

Compatibility rules:

- Text-only adapters may keep implementing only `ext/channel/deliver`.
- The server may route `message.kind = "text"` through either `ext/channel/deliver` or `ext/channel/send`.
- The server must never silently downgrade `file`, `audio`, `image`, or `video` to `ext/channel/deliver`.

If an adapter advertises `maxBytes` for a media kind, it should expect the server to reject sources it cannot validate against that limit. In the current milestone, remote `url` media is rejected when `maxBytes` is enforced because the server does not fetch remote bytes for size inspection.

### 6.3 `ext/channel/toolCall`

Runtime tool invocation for adapter-declared channel tools.

**Direction**: server → client (request, requires response)

**Params**:

| Field | Type | Description |
|-------|------|-------------|
| `threadId` | string | Thread in which the tool is being executed. |
| `turnId` | string | Turn that owns the tool call. |
| `callId` | string | Server-generated tool call identifier. |
| `tool` | string | Declared tool name from `channelAdapter.channelTools`. |
| `arguments` | object | Validated tool arguments matching `inputSchema`. |
| `context` | object | Current channel context (`channelName`, `channelContext`, `senderId`, `groupId`). |

**Result**:

```json
{
  "success": true,
  "contentItems": [
    { "type": "text", "text": "Sent report.pdf to the current chat." }
  ],
  "structuredResult": {
    "delivered": true,
    "fileName": "report.pdf"
  }
}
```

If the tool fails, the adapter returns `{ "success": false, "errorCode": "...", "errorMessage": "..." }`.

### 6.4 `ext/channel/heartbeat`

A JSON-RPC level health probe sent by DotCraft to verify the adapter's full message-processing pipeline is responsive. This is distinct from the transport-layer WebSocket ping/pong frames.

**Direction**: server → client (request, requires response)

**Params**: `{}`

**Result**: `{}`

If the adapter does not respond within the configured timeout, `ExternalChannelHost` marks the connection as unhealthy and initiates a reconnect cycle (subprocess mode: restart the process; WebSocket mode: close connection and wait for the adapter to reconnect).

---

## 7. ExternalChannelHost

`ExternalChannelHost` is the server-side bridge component. It implements `IChannelService` and wraps a Wire Protocol connection to an external adapter process.

### 7.1 Responsibilities

- Establishing and maintaining the transport connection to the adapter.
- Running the `AppServerRequestHandler` message loop for the adapter's connection, giving the adapter full access to `ISessionService`.
- Implementing text delivery via `ext/channel/deliver` and structured delivery via `ext/channel/send`.
- Forwarding injected `HeartbeatService` and `CronService` delivery events to the adapter through the negotiated delivery path.
- Monitoring adapter responsiveness via `ext/channel/heartbeat` and triggering restarts when the adapter becomes unresponsive.
- Subprocess lifecycle management (subprocess mode only): spawning, monitoring exit, and restarting with backoff.

### 7.2 Lifecycle

```
GatewayHost.StartAsync()
  └─ ExternalChannelHost.StartAsync()
       ├─ [subprocess mode] Spawn adapter process, wait for initialize handshake
       ├─ [websocket mode]  Wait for adapter to connect and complete initialize
       └─ Run AppServerRequestHandler message loop

GatewayHost.StopAsync()
  └─ ExternalChannelHost.StopAsync()
       ├─ [subprocess mode] Terminate adapter process
       └─ [websocket mode]  Close WebSocket connection
```

### 7.3 Restart Behavior (Subprocess Mode)

If the adapter process exits unexpectedly, `ExternalChannelHost` logs the exit code and restarts after a backoff delay. After a configurable number of consecutive failed starts, the channel is marked permanently failed and removed from the active channel list. While the adapter is down, `DeliverMessageAsync` is a no-op (delivery is best-effort).

### 7.4 `IChannelService` Mapping

| `IChannelService` member | `ExternalChannelHost` behavior |
|--------------------------|-------------------------------|
| `Name` | Channel name from `channelAdapter.channelName` in `initialize`. |
| `StartAsync()` | Establishes transport, performs handshake, starts message loop. |
| `StopAsync()` | Closes transport, stops message loop. |
| `DeliverMessageAsync(target, content)` | Legacy text helper; may route to `ext/channel/deliver` or structured `text` delivery. |
| `DeliverAsync(target, message, metadata)` | Structured delivery entry point used for text and media. |
| `ApprovalService` | `null` — approval is handled end-to-end by the adapter via Wire Protocol. |
| `ChannelClient` | `null` — platform client is out-of-process. |
| `HeartbeatService` | Injected by `GatewayHost`; delivery results forwarded through the negotiated delivery path. |
| `CronService` | Injected by `GatewayHost`; job results forwarded through the negotiated delivery path. |

---

## 8. ExternalChannelManager

`ExternalChannelManager` is a `GatewayHost`-level component that reads external channel configuration and creates the corresponding `ExternalChannelHost` instances.

### 8.1 Responsibilities

- Load the `"ExternalChannels"` section from `config.json` via `AppConfig.GetSection<ExternalChannelsConfig>("ExternalChannels")`.
- For each enabled external channel entry, create an `ExternalChannelHost` with the appropriate transport.
- For WebSocket-mode channels, register the channel in `ExternalChannelRegistry`. The adapter connects to the existing AppServer WebSocket endpoint (`/ws`), and `AppServerHost` routes the connection to the correct `ExternalChannelHost` by matching `channelAdapter.channelName` from the `initialize` params (see §4.2).
- Provide the created `IChannelService` list to `GatewayHost` alongside native channel services.

### 8.2 Integration Point in GatewayHost

`ExternalChannelManager` produces `IChannelService` instances that `GatewayHost` treats identically to native channels. The gateway's existing startup sequence applies to external channels without modification.

`ExternalChannelManager` is invoked during gateway host construction (inside `GatewayHostFactory.CreateHost()`). It reads configuration, creates `ExternalChannelHost` instances, and merges them into the channel list alongside native channels. For WebSocket-mode channels, no additional HTTP endpoint registration is needed — they reuse the existing AppServer WebSocket endpoint (`/ws`), with `AppServerHost` routing incoming `channelAdapter` connections to the corresponding `ExternalChannelHost` via `ExternalChannelRegistry`.

---

## 9. Configuration

External channels are declared in `config.json` under the `"ExternalChannels"` key. Each property name under `"ExternalChannels"` is the canonical channel name. Configuration is loaded via `AppConfig.GetSection<ExternalChannelsConfig>("ExternalChannels")`, following the same pattern as other DotCraft modules.

### 9.1 Schema

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `enabled` | boolean | yes | Whether this channel is active. |
| `transport` | string | yes | `"subprocess"` or `"websocket"`. |
| `command` | string | if subprocess | Command to start the adapter process. |
| `args` | string[] | no | Additional command-line arguments. |
| `workingDirectory` | string | no | Working directory for the subprocess. Defaults to workspace root. |
| `env` | object | no | Additional environment variables passed to the subprocess. |

> **WebSocket mode note**: WebSocket-mode channels reuse the existing AppServer WebSocket endpoint (configured under `"AppServer.WebSocket"`). The adapter connects to `ws://{host}:{port}/ws?token={token}` using the AppServer's host, port, and token settings. No per-channel port or token configuration is needed — the adapter is identified by `channelAdapter.channelName` during the `initialize` handshake.

> **Runtime declaration note**: `ExternalChannels` configuration only tells DotCraft how to start or accept the adapter connection. Structured delivery capabilities and `channelTools` are declared by the adapter itself during `initialize`; they are not static config fields in `config.json`.

### 9.2 Examples

**Subprocess mode** — DotCraft spawns and owns the adapter process:

```json
{
  "ExternalChannels": {
    "telegram": {
      "enabled": true,
      "transport": "subprocess",
      "command": "python",
      "args": ["-m", "dotcraft_telegram"],
      "workingDirectory": ".",
      "env": {
        "TELEGRAM_BOT_TOKEN": "your-token-here"
      }
    }
  }
}
```

**WebSocket mode** — adapter connects independently (the same adapter, different deployment):

```json
{
  "ExternalChannels": {
    "telegram": {
      "enabled": true,
      "transport": "websocket"
    }
  }
}
```

The adapter connects to `ws://127.0.0.1:{AppServer.WebSocket.Port}/ws?token={AppServer.WebSocket.Token}` and presents `channelAdapter.channelName = "telegram"` in its `initialize` params. The AppServer WebSocket endpoint is configured separately under the `"AppServer"` section (see appserver-protocol.md §15).

---

## 10. Adapter Behavioral Contract

This section defines the protocol-level obligations that any conforming external channel adapter must satisfy, regardless of implementation language or SDK.

### 10.1 Initialization

- The adapter **must** send `initialize` as the first message on connection, with `capabilities.channelAdapter` present.
- The adapter **must** send the `initialized` notification after receiving the `initialize` response before making any other requests.
- `channelAdapter.channelName` **must** match the channel name declared in server-side configuration.
- `channelAdapter.channelTools`, when present, **must** be declared during `initialize`; they are not loaded from server-side `ExternalChannels` configuration.
- `capabilities.approvalSupport` **must** be `true` if the adapter will handle approval requests. If set to `false`, the server auto-resolves approvals using workspace defaults and the adapter will never receive `item/approval/request`.

### 10.2 Thread and Turn Management

- The adapter is responsible for mapping platform identities to `SessionIdentity`. The `channelName` field in `SessionIdentity` **must** match the adapter's declared `channelName`.
- The adapter **must** use `thread/list` to locate existing threads for a given identity before creating a new one with `thread/start`. Creating duplicate threads for the same identity is a logical error.
- A paused thread must be resumed via `thread/resume` before submitting a new turn.
- The adapter **must not** call `turn/start` on a thread that already has a running turn. The server rejects this with `-32012`. The adapter should serialize user messages per thread or inform the user that the agent is busy.

### 10.3 Sender Context

- The adapter **must** populate `SenderContext` in `turn/start` with at minimum `senderId` and `senderName`. This enables correct attribution in the turn's `initiator` record and cross-channel audit logging.
- The adapter is responsible for permission checks before forwarding a message to DotCraft. DotCraft trusts the `SenderContext` presented by the adapter.
- The `groupId` field **must** be set to the platform-specific delivery target for the current chat or group (e.g. the Telegram `chat_id`). The server uses this value as the default delivery target when a cron job is created during the turn: if the cron payload does not specify a `to` field, the server falls back to `SenderContext.groupId`. Adapters that support delivery (`deliverySupport: true`) must therefore ensure `groupId` contains a value that their own `ext/channel/deliver` handler can accept as `target`. If no meaningful group context exists, omit `groupId`; the server will fall back to `senderId` instead.

### 10.4 Server-to-Client Requests

The adapter **must** handle the following server-initiated requests:

| Method | Required behavior |
|--------|-------------------|
| `item/approval/request` | Present platform-native approval UI; respond with `{ "decision": "..." }`. See §11. |
| `ext/channel/deliver` | Deliver `content` to `target` on the platform; respond with `{ "delivered": true/false }`. |
| `ext/channel/send` | Deliver a structured `message` payload to `target`; validate `message.kind` and source forms against the adapter's advertised capabilities. |
| `ext/channel/toolCall` | Execute a previously declared `channelTools` entry; return structured success/failure data without mutating the declared tool set. |
| `ext/channel/heartbeat` | Respond immediately with `{}`. |

The adapter **must not** ignore these requests. Failure to respond causes the server to time out (approval: `-32020` turn failure; heartbeat: connection marked unhealthy).

### 10.5 Connection Lifecycle (WebSocket Mode)

- The adapter is responsible for reconnecting after a disconnection. It should use exponential backoff.
- After reconnection, the adapter **must** re-perform the full `initialize` / `initialized` handshake.
- Any turns that were in progress at disconnection time will have failed on the server (approval timeout or turn cancellation). The adapter should not attempt to resume those turns.

---

## 11. Approval Flow in External Channels

This section describes how the Wire Protocol's bidirectional `item/approval/request` (appserver-protocol.md §7) maps to platform-native approval UX in external channels.

### 11.1 Sequence

The approval sequence for an external channel adapter is identical to the sequence for the CLI wire mode. The adapter plays the role that `WireApprovalHandler` plays in the CLI:

```
Platform User           Adapter                  DotCraft (AppServer)
      |                    |                            |
      |                    | turn/start                 |
      |                    |--------------------------->|
      |                    |                            | (agent runs...)
      |                    | item/approval/request      |
      |                    |<---------------------------|
      |  [platform-native  |                            |
      |   approval prompt] |                            |
      |<-------------------|                            |
      |                    |                            |
      | user responds      |                            |
      |------------------>|                            |
      |                    | JSON-RPC response          |
      |                    | { decision: "accept" }     |
      |                    |--------------------------->|
      |                    |                            |
      |                    | item/approval/resolved     |
      |                    |<---------------------------|
      |                    |                            | (agent continues...)
```

### 11.2 Adapter Obligations

- The adapter **must** present an approval prompt to the user on the platform using platform-native mechanisms (buttons, reply prompts, etc.).
- The adapter **must** map the platform's callback identifier to the Wire Protocol `request.id` and send the JSON-RPC response when the user responds.
- Multiple approval requests may be in flight on different threads simultaneously. The callback-to-request mapping **must** be per-request, not global.
- If the user does not respond before the server's approval timeout (`-32020`), the turn fails. The adapter should clean up any pending approval UI on timeout.

### 11.3 Decision Values

The adapter must support the five `SessionApprovalDecision` values (appserver-protocol.md §7.3). Adapters that cannot present all five may offer a simplified subset (e.g., "Approve" = `accept`, "Stop" = `cancel`). The Wire Protocol does not require every decision value to be surfaced.

---

## 12. Security

### 12.1 Subprocess Mode

Security is provided by OS process isolation. Communication is over anonymous pipes, not network-accessible. No authentication token is needed. Adapter code runs with the same privileges as DotCraft; operators must only configure trusted adapter commands.

### 12.2 WebSocket Mode

- **Shared AppServer endpoint**: WebSocket-mode external channels reuse the AppServer WebSocket endpoint (`/ws`), which is configured under the `"AppServer.WebSocket"` section of `config.json`. No per-channel endpoint is created.
- **Loopback-only binding** (default): The AppServer WebSocket endpoint binds to `127.0.0.1`. Remote adapters cannot connect without explicit configuration.
- **Bearer token**: Required when the endpoint is exposed beyond loopback. Passed as `?token=...` in the URL (see appserver-protocol.md §15.4). The token is shared across all AppServer clients (adapters and regular clients alike).
- **Channel name verification**: The server verifies that `channelAdapter.channelName` is registered in the `"ExternalChannels"` configuration. An adapter may not register under an unknown or disabled channel name.
- **No per-adapter identity isolation**: All connections to the same server process share `ISessionService`. An authorized adapter can operate on any thread matching its `SessionIdentity`. Operators requiring strict cross-channel isolation should use separate DotCraft instances.

---

## 13. Reference: Telegram Adapter

This section describes the design intent of the reference Telegram adapter (`sdk/python/examples/telegram/`). It is provided as guidance for adapter authors, not as a normative specification.

### 13.1 Design Goals

- Use the Telegram Bot API with long polling (no public IP or webhook required).
- Map each Telegram chat (private or group) to a DotCraft thread via `SessionIdentity`.
- Stream `item/agentMessage/delta` events to buffer the agent's reply, then send the final composed message to the chat.
- Present `item/approval/request` using Telegram inline keyboard buttons, mapping Telegram `callback_query` responses back to Wire Protocol approval decisions.
- Support `/new` and `/help` slash commands, mapping them to `thread/archive` + `thread/start` and local help text respectively.

### 13.2 Key Protocol Behaviors

The Telegram adapter demonstrates the following protocol obligations defined in §10:

- **Thread management**: On each incoming message, `thread/list` is called to find the active thread for the chat's `SessionIdentity`. If none exists, `thread/start` creates one.
- **SenderContext**: The Telegram user ID and display name are forwarded as `SenderContext` on every `turn/start`. The Telegram `chat_id` is also forwarded as `groupId`, which the server uses as the default delivery target for any cron jobs created during the session.
- **Approval**: The adapter intercepts `item/approval/request` mid-stream, presents a platform-native prompt, and sends the JSON-RPC response before resuming event consumption.
- **Delivery**: `ext/channel/deliver` is mapped to `bot.send_message()` targeting the stored chat ID for the given `target`, while `ext/channel/send` handles structured file delivery for the media kinds the adapter advertises.
- **Channel tools**: tool descriptors are declared during `initialize`; if the adapter declares any, it must also implement `ext/channel/toolCall` for those tools.
