# DotCraft Unified Channel Framework Milestone Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Draft |
| **Date** | 2026-04-12 |
| **Parent Spec** | [AppServer Protocol](appserver-protocol.md), [External Channel Media Foundation Milestone](external-channel-media-m1-foundation.md), [External Channel Channel-Tools Milestone](external-channel-media-m2-channel-tools.md) |

Purpose: Define **Milestone 3** of the channel unification refactor: built-in channels and external adapters must share one runtime model for capabilities, structured delivery, platform tools, and chat-context execution.

This milestone assumes active development. Compatibility fallbacks are intentionally removed from the primary design.

## 1. Goals

Milestone 3 completes the architectural unification.

Goals:

- Replace the split between built-in channel tools and external adapter tools with one runtime tool model.
- Replace text-only routing plus per-channel exceptions with one structured outbound message model.
- Make QQ / WeCom / external adapters participate in the same capability and tool-descriptor framework.
- Make the AppServer protocol describe the remote projection of that unified model instead of describing external channels as a separate architecture.

## 2. Non-Goals

Out of scope:

- Turning built-in channels into out-of-process adapters.
- Preserving old text-only delivery APIs as long-term compatibility paths.
- Rewriting stable platform SDK wrappers unless required to move logic behind the unified runtime.

The goal is runtime unification, not deployment unification.

## 3. Target Architecture

### 3.1 Unified Runtime Contract

All channels must implement one internal runtime contract:

- `IChannelRuntime`
- `IChannelRuntimeRegistry`
- `IChannelRuntimeToolProvider`

`IChannelRuntime` is the canonical source for:

- `Name`
- `GetDeliveryCapabilities()`
- `GetChannelTools()`
- `DeliverAsync(target, message, metadata, ct)`
- `ExecuteToolAsync(request, ct)`

Built-in and external channels use the same protocol DTO semantics:

- `ChannelDeliveryCapabilities`
- `ChannelToolDescriptor`
- `ChannelOutboundMessage`
- `ChannelMediaSource`
- `ExtChannelToolCallContext`
- `ExtChannelToolCallResult`

No parallel built-in-only descriptor model is allowed after M3.

### 3.2 Built-In vs Remote Backends

The unified runtime supports two execution backends:

- **Local backend**
  - QQ / WeCom execute tools and delivery in-process.
- **Remote backend**
  - external adapters execute tools through `ext/channel/toolCall`
  - external delivery uses `ext/channel/send`

The agent/runtime layer must not care which backend a channel uses.

### 3.3 Structured Delivery As The Only Primary Path

Structured delivery is the only primary outbound contract after M3.

Rules:

- `IMessageRouter` routes `ChannelOutboundMessage`, not raw text.
- `IChannelService` no longer exposes `DeliverMessageAsync`.
- `ext/channel/send` is the formal remote delivery contract for text and media.
- `ext/channel/deliver` is no longer part of the primary runtime design.

### 3.4 Unified Channel Tools

Channel-native tools must be descriptor-driven for every channel.

Rules:

- built-in channels must return `ChannelToolDescriptor` values
- external adapters still declare descriptors during `initialize`
- Session/Agent tool construction must append tools from the matching `thread.originChannel` only
- local and remote executions must return the same result shape and item lifecycle

`ToolProviderContext.ChannelClient` is not part of the M3 tool path.

## 4. Required Refactors

### 4.1 Registry And Injection

Introduce a unified runtime registry and use it everywhere channel tools are discovered.

Required changes:

- register native channel services in `IChannelRuntimeRegistry`
- register external hosts in the same registry
- replace external-only tool injection with a registry-backed `IChannelRuntimeToolProvider`

### 4.2 Message Routing

Refactor router and services to structured-first delivery.

Required changes:

- `IMessageRouter.DeliverAsync(channel, target, ChannelOutboundMessage, ...)`
- built-in channels implement structured delivery directly
- text broadcast helpers may remain as wrappers only when they construct `ChannelOutboundMessage(kind=text)`

### 4.3 Built-In Channel Migration

Move QQ and WeCom media logic out of legacy tool-provider-only paths.

Required result:

- QQ voice/video/file sending is exposed through runtime descriptors and runtime delivery
- WeCom voice/file sending is exposed through runtime descriptors and runtime delivery
- current-chat execution context is resolved by runtime context, not by ad hoc prompt instructions

### 4.4 Old Path Removal

The following are no longer primary channel-extension mechanisms:

- `DeliverMessageAsync`
- module-scanned QQ / WeCom tool providers as the source of channel tools
- `ToolProviderContext.ChannelClient` for channel tool execution
- external-only tool injection concepts in SessionService

Short-lived implementation leftovers are acceptable only if they are no longer in the runtime path.

## 5. Runtime Rules

### 5.1 One Capability Model

For any channel, the runtime must be able to answer:

- what delivery kinds exist
- what source kinds are accepted
- what platform tools exist
- whether chat context is required

### 5.2 One Tool Lifecycle

For any channel tool call:

1. validate arguments against `ChannelToolDescriptor.inputSchema`
2. resolve current channel execution context
3. fail early if `requiresChatContext = true` and context is missing
4. execute via local or remote backend
5. publish the same item lifecycle and result mapping

### 5.3 One Delivery Lifecycle

For any outbound send:

1. caller builds `ChannelOutboundMessage`
2. channel runtime validates kind/source against capabilities
3. runtime executes platform-specific delivery
4. caller receives `ExtChannelSendResult`

Built-in channels may translate directly to SDK calls; external adapters translate to `ext/channel/send`.

## 6. Test Plan

Required tests:

- `MessageRouter` routes structured `text` / `file` / `audio` messages to the matching channel runtime
- admin text broadcast still works
- QQ exposes runtime descriptors for current-chat voice/video/file
- QQ structured `audio` and `file` or `video` delivery succeed through the runtime contract
- WeCom exposes runtime descriptors for current-chat voice/file
- WeCom structured `audio` and `file` delivery succeed through the runtime contract
- external adapters still expose tools only to matching-origin threads
- external text delivery also routes through `ext/channel/send`
- missing chat context fails consistently for built-in and external runtime tools
- SessionService tool construction uses the unified runtime provider instead of external-only append logic

## 7. Acceptance Criteria

Milestone 3 is complete when:

- built-in and external channels share one internal runtime contract
- built-in and external channels share one structured outbound delivery model
- built-in and external channels share one descriptor-driven platform-tool model
- QQ / WeCom no longer rely on module-scanned tool providers as the primary channel-extension path
- `ext/channel/send` and `ext/channel/toolCall` are documented as the remote projection of the unified channel runtime
