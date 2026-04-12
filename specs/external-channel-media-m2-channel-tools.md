# DotCraft External Channel Channel-Tools Milestone Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-12 |
| **Parent Spec** | [External Channel Media Foundation Milestone](external-channel-media-m1-foundation.md) |

Purpose: Define **Milestone 2** of the external-channel media refactor: a runtime tool-extension mechanism that lets external adapters expose platform-specific capabilities to the agent.

## Table of Contents

- [1. Goals](#1-goals)
- [2. Non-Goals](#2-non-goals)
- [3. Current Problem](#3-current-problem)
- [4. Design Overview](#4-design-overview)
- [5. Protocol Changes](#5-protocol-changes)
- [6. Server Injection Rules](#6-server-injection-rules)
- [7. Tool Execution Semantics](#7-tool-execution-semantics)
- [8. Validation and Security](#8-validation-and-security)
- [9. Test Plan](#9-test-plan)
- [10. Acceptance Criteria](#10-acceptance-criteria)

---

## 1. Goals

Milestone 2 makes external adapters first-class providers of platform-native tools.

Goals:

- Let an external adapter declare **channel-specific tools** during connection setup.
- Let the server inject those tools into the agent tool list for matching channel threads only.
- Add a request/response protocol so the server can ask the adapter to execute a declared tool.
- Reuse Milestone 1 media/artifact infrastructure so tool outputs can return text and media-aware results.

---

## 2. Non-Goals

Out of scope for Milestone 2:

- Full QQ / WeCom migration.
- Marketplace, plugin installation UI, or remote tool package discovery.
- General-purpose tool hosting for arbitrary non-channel clients.
- Standardizing every platform capability into protocol-level verbs.

Milestone 2 is specifically about **adapter-declared platform tools**.

---

## 3. Current Problem

Today, QQ and WeCom can expose platform tools because they are in-process C# modules:

- tool providers are collected from modules;
- `ToolProviderContext.ChannelClient` can carry a native SDK client;
- `QQToolProvider` and `WeComToolProvider` create ordinary `AITool` instances.

External channels do not have an equivalent path:

- `ExternalChannelHost.ChannelClient` is `null`;
- adapters are out-of-process;
- there is no protocol for "declare a tool" or "execute a tool call";
- therefore external adapters cannot expose capabilities like:
  - Telegram `send_document`
  - Feishu file upload + send
  - platform-native card, message recall, or media APIs

Milestone 2 closes that gap.

---

## 4. Design Overview

### 4.1 Adapter-Declared Tool Descriptors

During initialization, an adapter may declare `channelTools`.

Each tool descriptor must include:

- `name`
- `description`
- `inputSchema`
- `outputSchema?`
- `display?`
- `requiresChatContext: bool`
- `deferLoading?: bool`

These tools are channel-scoped, not globally available.

### 4.2 Server-Generated Runtime Tools

The server generates runtime `AITool` wrappers from adapter descriptors.

Each generated runtime tool:

- validates arguments against the declared schema;
- captures the current thread and turn context;
- sends an adapter request when invoked;
- maps the result back into the agent runtime as a normal tool result.

### 4.3 Thread-Scoped Visibility

External channel tools must only be injected when:

- the thread origin channel matches the adapter channel name; and
- the adapter connection is active; and
- the tool passed validation and registration.

This avoids leaking Telegram-only tools into Feishu, Desktop, CLI, or other threads.

---

## 5. Protocol Changes

### 5.1 Initialization Extension

Extend `capabilities.channelAdapter` with an optional `channelTools` array:

```json
{
  "channelAdapter": {
    "channelName": "telegram",
    "deliverySupport": true,
    "channelTools": [
      {
        "name": "TelegramSendDocumentToCurrentChat",
        "description": "Send a document to the current Telegram chat.",
        "requiresChatContext": true,
        "inputSchema": {
          "type": "object",
          "properties": {
            "filePath": { "type": "string" },
            "caption": { "type": "string" }
          },
          "required": ["filePath"]
        }
      }
    ]
  }
}
```

### 5.2 New Server-to-Adapter Request

Add:

- `ext/channel/toolCall`

Request shape:

```json
{
  "threadId": "thread_001",
  "turnId": "turn_001",
  "callId": "call_001",
  "tool": "TelegramSendDocumentToCurrentChat",
  "arguments": {
    "filePath": "/tmp/report.pdf",
    "caption": "Latest report"
  },
  "context": {
    "channelName": "telegram",
    "channelContext": "123456789",
    "senderId": "42",
    "groupId": "123456789"
  }
}
```

### 5.3 Response Shape

Adapter returns:

```json
{
  "success": true,
  "contentItems": [
    { "type": "text", "text": "Document sent." }
  ],
  "structuredResult": {
    "remoteMessageId": "msg_123"
  }
}
```

Milestone 2 content item types:

- `text`
- `image`

Media-returning content beyond that should use `structuredResult` and be expanded later only if needed.

### 5.4 Lifecycle Notifications

Tool calls must appear in the turn item stream as a distinct item kind:

- `externalChannelToolCall`

Required observable lifecycle:

1. `item/started`
2. adapter request `ext/channel/toolCall`
3. adapter response
4. `item/completed`

This keeps external tools traceable like ordinary tool calls.

---

## 6. Server Injection Rules

### 6.1 Registration

On successful initialization:

- store validated adapter tool descriptors on the connection;
- index them by channel name;
- expose them to thread-scoped tool building for matching origin channels.

### 6.2 Injection Point

During tool construction for a thread:

- inspect the effective channel origin;
- query active adapter tool descriptors for that channel;
- create runtime tool wrappers and append them after core tools;
- omit all adapter tools when the adapter is disconnected.

### 6.3 Naming

Tool names must be globally unique at runtime.

Conflict policy:

- if a declared adapter tool name conflicts with a built-in tool or another registered adapter tool, registration fails for that tool;
- the connection remains valid, but the conflicting tool is unavailable;
- the server logs the conflict and exposes it in diagnostics.

---

## 7. Tool Execution Semantics

### 7.1 Context

Every adapter tool call must receive:

- thread id
- turn id
- call id
- tool name
- validated arguments
- current channel context
- current sender identity when available

### 7.2 Chat-Context Requirement

If `requiresChatContext = true` and the current thread lacks channel context, the tool call must fail before dispatch.

### 7.3 Timeouts

Adapter tool calls must have bounded execution time.

Required behavior:

- timeout produces a failed tool item;
- adapter late responses are ignored after timeout resolution;
- timeout values are server-defined in this milestone, not adapter-defined.

### 7.4 Result Mapping

`success = false` does not crash the turn. It is surfaced as a normal failed tool result item that the agent can react to.

---

## 8. Validation and Security

### 8.1 Schema Validation

The server must validate:

- `inputSchema` is a supported JSON Schema subset;
- arguments conform before adapter dispatch.

### 8.2 Tool Safety

Adapter tools are trusted only within the declared adapter boundary.

Required constraints:

- tools are only callable from matching-origin threads;
- tool descriptors are immutable for the life of the connection;
- server never forwards undeclared arguments;
- server never forwards tool calls to disconnected adapters.

### 8.3 Approval

Milestone 2 does not create a new approval protocol. Tool calls still rely on the adapter and current thread approval model.

However, the design must preserve room for later per-tool approval policy if needed.

---

## 9. Test Plan

Required tests:

- adapter registers one valid tool and it appears only in matching-channel threads;
- invalid `inputSchema` causes descriptor rejection;
- tool name conflict causes descriptor rejection without breaking the connection;
- tool invocation sends `ext/channel/toolCall` with thread and channel context;
- tool success yields completed `externalChannelToolCall` item;
- tool timeout yields failed item;
- tool called on a thread with missing chat context fails when `requiresChatContext = true`;
- disconnected adapter removes its tools from future thread tool construction;
- existing text-only adapters still function without channel tools.

---

## 10. Acceptance Criteria

Milestone 2 is complete when:

- an external adapter can declare platform-native tools through the wire protocol;
- those tools are injected into matching channel threads only;
- the server can invoke them via `ext/channel/toolCall`;
- results are reflected in the ordinary turn item lifecycle;
- the mechanism is reusable for Telegram, Feishu, and future external platforms;
- the design remains compatible with later QQ / WeCom unification.
