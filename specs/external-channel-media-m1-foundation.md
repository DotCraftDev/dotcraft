# DotCraft External Channel Media Foundation Milestone Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-12 |
| **Parent Spec** | [AppServer Protocol](appserver-protocol.md), [External Channel Adapter](external-channel-adapter.md) |

Purpose: Define **Milestone 1** of the external-channel media refactor: a shared capability model, media artifact foundation, and a structured external-channel delivery protocol that extends today's text-only `ext/channel/deliver` path without breaking existing adapters.

This milestone spec is a design-and-acceptance companion to the living protocol specs. When conflicts arise, the updated wire contracts in [appserver-protocol.md](appserver-protocol.md) and [external-channel-adapter.md](external-channel-adapter.md) are authoritative.

## Table of Contents

- [1. Goals](#1-goals)
- [2. Non-Goals](#2-non-goals)
- [3. Current Problem](#3-current-problem)
- [4. Architecture Changes](#4-architecture-changes)
- [5. Protocol Changes](#5-protocol-changes)
- [6. Runtime Behavior](#6-runtime-behavior)
- [7. Compatibility](#7-compatibility)
- [8. Validation and Errors](#8-validation-and-errors)
- [9. Test Plan](#9-test-plan)
- [10. Acceptance Criteria](#10-acceptance-criteria)

---

## 1. Goals

Milestone 1 establishes the minimum shared infrastructure required for external channels to deliver non-text payloads safely and consistently.

Goals:

- Introduce a **unified channel capability model** that can describe text, file, audio, image, and video delivery support.
- Introduce a **shared media artifact abstraction** so file paths, URLs, and base64 payloads can be normalized before delivery.
- Add a **structured external-channel delivery request** for adapters that support non-text payloads.
- Preserve the existing `ext/channel/deliver(target, content)` behavior for text-only adapters.
- Make the new foundation reusable by both future external adapters and future QQ / WeCom migration work.

---

## 2. Non-Goals

Milestone 1 does not attempt to solve every future media problem.

Out of scope:

- External adapter tool registration and invocation. That is Milestone 2.
- Full migration of QQ / WeCom to the new stack. That is Milestone 3.
- New `turn/start` user input kinds such as generic `file` or `audio`.
- Realtime audio conversation protocol. If needed later, it must remain separate from ordinary message delivery.
- Rich card UI, inline buttons, message recall, or platform-native interactive components.

---

## 3. Current Problem

Today, external channels only expose a text-first delivery contract:

- `ext/channel/deliver` carries `target`, `content`, and optional `metadata`.
- `ExternalChannelHost.DeliverMessageAsync` only accepts a string payload.
- The Python and TypeScript adapter SDKs expose `on_deliver/onDeliver(target, content, metadata)`.

This design has three hard limits:

1. The server cannot express "send this file" or "send this voice clip" as a first-class request.
2. Media sources are not normalized. Different adapters would need to invent incompatible conventions for paths, URLs, and inline bytes.
3. Capability discovery is too coarse. `deliverySupport` only says whether an adapter can receive text delivery requests at all.

Milestone 1 fixes these limits at the capability and delivery layers before any tool-facing extensibility is introduced.

---

## 4. Architecture Changes

### 4.1 Channel Capability Descriptor

Add a runtime descriptor that represents what a channel can deliver.

Required fields:

- `channelName: string`
- `supportsTextDelivery: bool`
- `supportsStructuredDelivery: bool`
- `media.file?: ChannelMediaConstraints`
- `media.audio?: ChannelMediaConstraints`
- `media.image?: ChannelMediaConstraints`
- `media.video?: ChannelMediaConstraints`

`ChannelMediaConstraints` must support:

- `maxBytes?: long`
- `allowedMimeTypes?: string[]`
- `allowedExtensions?: string[]`
- `supportsHostPath: bool`
- `supportsUrl: bool`
- `supportsBase64: bool`
- `supportsCaption: bool`

The descriptor is the single source of truth for delivery validation and UI discoverability.

### 4.2 Channel Media Source

Introduce a discriminated source model for outbound media:

- `hostPath`
- `url`
- `dataBase64`
- `artifactId`

Each outbound non-text payload must use exactly one source kind.

### 4.3 Channel Media Artifact

Introduce a normalized artifact record produced by the server before adapter delivery:

- `id`
- `kind`
- `mediaType`
- `byteLength`
- `fileName`
- `sourceKind`
- `hostPath?`
- `sha256?`

Artifacts let the server validate payloads once and keep later tool / delivery flows aligned.

### 4.4 Shared Resolver

Add a server-side media resolver abstraction:

- `IChannelMediaResolver`
- Responsibility: accept raw source input, validate it, materialize if needed, and return a `ChannelMediaArtifact`.

Required behavior:

- `hostPath` must resolve to an existing host file.
- `url` may remain remote only when the target channel declares URL support; otherwise the server must reject the request.
- `dataBase64` must decode successfully and may be materialized into a temporary artifact file when needed.
- `artifactId` must resolve to an existing artifact record.
- `artifactId` in Milestone 1 is a host-local temporary handle. The protocol does not guarantee cross-process, cross-restart, or cross-adapter persistence.

---

## 5. Protocol Changes

### 5.1 Capability Negotiation

Extend external adapter initialization so adapters can declare structured delivery capabilities.

Add a new optional field under `capabilities.channelAdapter`:

```json
{
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
          "allowedMimeTypes": ["application/pdf", "text/plain"]
        },
        "audio": {
          "supportsHostPath": false,
          "supportsUrl": false,
          "supportsBase64": true,
          "allowedMimeTypes": ["audio/ogg", "audio/mpeg"]
        }
      }
    }
  }
}
```

If omitted, the adapter is treated as text-only unless future defaults are explicitly documented.

### 5.2 New Server-to-Adapter Request

Add a new request:

- `ext/channel/send`

Request shape:

```json
{
  "target": "group:12345",
  "message": {
    "kind": "file",
    "caption": "Latest report",
    "fileName": "report.pdf",
    "mediaType": "application/pdf",
    "source": {
      "kind": "artifactId",
      "artifactId": "artifact_001"
    }
  },
  "metadata": {
    "origin": "cron"
  }
}
```

Supported `message.kind` values in Milestone 1:

- `text`
- `file`
- `audio`
- `image`
- `video`

### 5.3 Response Shape

`ext/channel/send` returns:

```json
{
  "delivered": true,
  "remoteMessageId": "abc123",
  "remoteMediaId": "media_xyz",
  "errorCode": null,
  "errorMessage": null
}
```

When delivery fails, `delivered` must be `false` and `errorCode` must be set.

### 5.4 Legacy Path

`ext/channel/deliver` remains valid and unchanged for text-only delivery.

Server routing rules:

- If payload kind is `text` and the adapter does not support structured delivery, the server may fall back to `ext/channel/deliver`.
- If payload kind is not `text`, the server must never silently degrade to `ext/channel/deliver`.

---

## 6. Runtime Behavior

### 6.1 Server Delivery Flow

For non-text delivery:

1. Resolve adapter capability descriptor from the connection.
2. Validate that the target media kind is supported.
3. Normalize the source via `IChannelMediaResolver`.
4. Validate artifact size, mime type, and source-kind compatibility.
5. Send `ext/channel/send`.
6. Interpret the adapter response and log structured diagnostics.

### 6.2 Targeting

Target semantics remain channel-defined. Milestone 1 does not standardize target formats beyond the existing string-based contract.

### 6.3 Timeouts

Structured media delivery must use the same best-effort semantics as today's text delivery:

- delivery errors are logged;
- cron / heartbeat delivery attempts do not fail the originating job solely because media delivery failed;
- callers receive a structured failure result when they directly initiate the send.

---

## 7. Compatibility

Milestone 1 must be safe to ship before any adapter is upgraded.

Compatibility rules:

- Existing adapters that only implement `ext/channel/deliver` continue to work unchanged.
- Existing SDKs remain source-compatible for text delivery.
- Structured delivery is opt-in by declared capability.
- No existing thread or turn wire payloads change in this milestone.

---

## 8. Validation and Errors

Define adapter-facing and caller-facing failure categories.

Required error codes:

- `UnsupportedDeliveryKind`
- `UnsupportedMediaSource`
- `MediaTooLarge`
- `MediaTypeNotAllowed`
- `MediaArtifactNotFound`
- `MediaResolutionFailed`
- `AdapterDeliveryFailed`
- `AdapterProtocolViolation`

Validation rules:

- `message.kind` must be compatible with the adapter capability descriptor.
- `text` messages must not include `source`.
- Exactly one media source variant must be present for non-text payloads.
- `caption` is rejected if the adapter does not declare `supportsCaption`.
- URL sources must be absolute `http` or `https` URLs.
- MIME type checks use the resolved artifact's detected type when available; otherwise the declared type is used.

---

## 9. Test Plan

Required tests:

- Text-only adapter receives legacy `ext/channel/deliver` for text payloads.
- Structured-delivery adapter receives `ext/channel/send` for text payloads when explicitly enabled.
- File payload with supported `artifactId` source succeeds.
- Audio payload with unsupported media kind is rejected before adapter dispatch.
- Base64 media source decodes into a temporary artifact and is cleaned up after use.
- URL source is rejected when the adapter does not support remote URLs.
- Adapter returns `delivered: false` and the server surfaces `AdapterDeliveryFailed`.
- Existing Telegram and Feishu example adapters remain functional for text-only flows until upgraded.

---

## 10. Acceptance Criteria

Milestone 1 is complete when all of the following are true:

- The server can represent non-text external-channel delivery as a structured request.
- Adapters can declare delivery capabilities beyond the old `deliverySupport` boolean.
- Media sources are normalized through one shared server-side resolver.
- Existing text-only adapters continue to work without code changes.
- The resulting abstractions are reusable by Milestone 2 channel tools and Milestone 3 QQ / WeCom migration.
