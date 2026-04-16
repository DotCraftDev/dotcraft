# DotCraft External CLI Subagent — M3: Session Integration and Progress Streaming

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [M1: Runtime Abstraction](external-cli-subagent-m1.md), [M2: One-Shot CLI Runtime](external-cli-subagent-m2.md), [Session Core](session-core.md), [AppServer Protocol](appserver-protocol.md) |

Purpose: Define how external subagent activity is integrated into DotCraft's session event pipeline, enabling all clients (CLI, TUI, Desktop) to observe the lifecycle and incremental progress of external CLI subagents, and establishing the wire protocol extensions required to carry this information.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. Event Model Overview](#3-event-model-overview)
- [4. `ISubAgentEventSink` Extension](#4-isubagenteventsink-extension)
- [5. External Subagent Lifecycle Events](#5-external-subagent-lifecycle-events)
- [6. Progress Streaming from Subprocess Output](#6-progress-streaming-from-subprocess-output)
- [7. Token and Cost Accounting](#7-token-and-cost-accounting)
- [8. Wire Protocol Extensions](#8-wire-protocol-extensions)
- [9. Client Rendering Contract](#9-client-rendering-contract)
- [10. Constraints and Compatibility Notes](#10-constraints-and-compatibility-notes)
- [11. Acceptance Checklist](#11-acceptance-checklist)
- [12. Open Questions](#12-open-questions)

---

## 1. Scope

### 1.1 What This Spec Defines

- The extension of `ISubAgentEventSink` to carry structured lifecycle events from external runtimes.
- The lifecycle event set for external subagents: launched, progress update, completed, failed.
- How `CliOneshotRuntime` streams stdout/stderr into progress updates during subprocess execution.
- Token and cost accounting when external CLIs provide usage data in their output.
- Wire protocol extensions to the AppServer Protocol for delivering external subagent events to out-of-process clients.
- The client rendering contract for CLI spinner, TUI subagent progress panel, and Desktop subagent card.

### 1.2 What This Spec Does Not Define

- The internal session item model for persistent multi-turn subagent sessions. That is defined in the M4 spec.
- Git worktree lifecycle events. Those are defined in the M5 spec.
- Approval request events for subagent launch. Those are defined in the M6 spec.
- Vendor-specific structured tool event mapping (e.g., parsing a Claude Code tool call stream into DotCraft tool call items). This is out of scope for the current feature cycle.
- The complete AppServer Protocol specification. This spec defines only the extensions needed for external subagent visibility.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. **Observable subagent work**: Users watching the CLI, TUI, or Desktop clients see that an external agent is running, what it is doing (at a coarse progress level), and when it finishes or fails.
2. **Reuse existing infrastructure**: The event sink, progress bridge, and session event pipeline are extended rather than replaced. External subagent progress appears in the same region of the UI as native subagent progress.
3. **Streaming progress where possible**: When the profile declares `supportsStreaming: true`, stdout lines are forwarded to the event sink as they arrive rather than waiting for subprocess exit.
4. **Optional token accounting**: When an external CLI reports usage data in a parseable form, it is extracted and added to the turn's token totals.
5. **Graceful degradation**: Clients that do not yet handle new external subagent event types display a reasonable fallback (e.g., a generic "subagent running" indicator) rather than crashing or going blank.

### 2.2 Non-Goals

- Parsing external agent tool calls (file writes, shell executions) into structured session items. The external agent's actions are opaque to DotCraft in this milestone.
- Real-time diff preview of changes the external agent is making.
- Bidirectional interaction with the running external agent from the user (that is a persistent runtime concern in M4).
- Modifying the Wire Protocol's approval flow to gate external subagent launches (M6).

---

## 3. Event Model Overview

The existing session event pipeline works as follows for native subagents:

```
SubAgentManager.SpawnAsync
       │ progress updates
       ▼
SubAgentProgressBridge (static registry of ProgressEntry)
       │ ~200ms snapshots
       ▼
SubAgentProgressAggregator
       │ SessionEventType.SubAgentProgress
       ▼
ISessionService event stream → AppServer → clients
```

M3 extends this pipeline so that `CliOneshotRuntime` can feed structured lifecycle events and incremental stdout lines into the same `SubAgentProgressBridge`, and so that new event types for launch, completion, and failure travel through the same AppServer wire path.

The extended pipeline for external runtimes:

```
CliOneshotRuntime (or CliPersistentRuntime [M4])
       │ ISubAgentEventSink calls
       ▼
ExternalSubAgentEventSink
       │ updates SubAgentProgressBridge entry
       │ emits structured lifecycle SessionEvents
       ▼
SubAgentProgressAggregator (extended) + SessionService
       │ SubAgentProgress + new ExternalSubAgentEvent types
       ▼
AppServer wire → clients
```

---

## 4. `ISubAgentEventSink` Extension

### 4.1 New Sink Methods

The `ISubAgentEventSink` interface is extended with:

| Method | When called |
|--------|-------------|
| `OnLaunched(sessionHandle, profile, workingDirectory)` | Immediately after the subprocess starts successfully. |
| `OnProgressLine(sessionHandle, line, isStderr)` | For each line of stdout/stderr received during streaming (only when `supportsStreaming` is true). |
| `OnCompleted(sessionHandle, result)` | When `RunAsync` returns a successful result. |
| `OnFailed(sessionHandle, errorMessage)` | When `RunAsync` returns an error result or throws. |

For the M1 native runtime adapter, all new methods are no-ops (the sink is a null implementation or passes through to the existing `SubAgentProgressBridge` path). This preserves backward compatibility.

### 4.2 `ExternalSubAgentEventSink` Implementation

`ExternalSubAgentEventSink` is the concrete implementation used by external runtimes. It:

- Registers a `ProgressEntry` in `SubAgentProgressBridge` when `OnLaunched` is called.
- Updates the progress entry's current tool display to the most recent `OnProgressLine` content (trimmed to a configurable max display length).
- Emits a `SessionEvent` of type `ExternalSubAgentLaunched`, `ExternalSubAgentProgress`, and `ExternalSubAgentCompleted`/`ExternalSubAgentFailed` at the appropriate lifecycle points.
- Marks the progress entry as completed in `SubAgentProgressBridge` when `OnCompleted` or `OnFailed` is called.

---

## 5. External Subagent Lifecycle Events

### 5.1 New Session Event Types

The following `SessionEventType` values are added:

| Event type | Payload | When emitted |
|------------|---------|--------------|
| `ExternalSubAgentLaunched` | `ExternalSubAgentLaunchedPayload` | After subprocess starts |
| `ExternalSubAgentProgress` | `ExternalSubAgentProgressPayload` | On each progress line (streaming) or at intervals (non-streaming) |
| `ExternalSubAgentCompleted` | `ExternalSubAgentCompletedPayload` | On successful result |
| `ExternalSubAgentFailed` | `ExternalSubAgentFailedPayload` | On error result or exception |

### 5.2 Payload Shapes

**`ExternalSubAgentLaunchedPayload`**

| Field | Type | Description |
|-------|------|-------------|
| `agentId` | string | Unique identifier for this invocation (matches the `SpawnSubagent` label/normalized key) |
| `profileName` | string | The profile name used |
| `runtimeType` | string | `cli-oneshot` or `cli-persistent` |
| `workingDirectory` | string | Resolved working directory for this invocation |

**`ExternalSubAgentProgressPayload`**

| Field | Type | Description |
|-------|------|-------------|
| `agentId` | string | Matches launched event |
| `line` | string | Most recent output line (may be truncated for display) |
| `isStderr` | bool | Whether the line came from stderr |
| `elapsedMs` | int | Milliseconds since launch |

**`ExternalSubAgentCompletedPayload`**

| Field | Type | Description |
|-------|------|-------------|
| `agentId` | string | Matches launched event |
| `elapsedMs` | int | Total execution time |
| `tokensUsed` | TokenUsage? | Optional; see §7 |

**`ExternalSubAgentFailedPayload`**

| Field | Type | Description |
|-------|------|-------------|
| `agentId` | string | Matches launched event |
| `errorMessage` | string | Human-readable failure reason |
| `elapsedMs` | int | Time until failure |

### 5.3 Event Ordering Guarantees

- `ExternalSubAgentLaunched` is always emitted before any `ExternalSubAgentProgress` events for the same `agentId`.
- Exactly one terminal event (`ExternalSubAgentCompleted` or `ExternalSubAgentFailed`) is emitted per `agentId`, after all progress events.
- Events for concurrent subagents may be interleaved; clients disambiguate by `agentId`.

---

## 6. Progress Streaming from Subprocess Output

### 6.1 Streaming Mode

When `profile.supportsStreaming` is `true`, `CliOneshotRuntime` reads stdout and stderr asynchronously line by line and calls `sink.OnProgressLine` for each line as it arrives. The process is still awaited to completion before `RunAsync` returns.

When `profile.supportsStreaming` is `false` (the default for M2 profiles), stdout and stderr are captured after process exit. `OnProgressLine` is not called. A single `ExternalSubAgentProgress` event is emitted only to indicate the agent is running (no incremental lines).

### 6.2 Heartbeat for Non-Streaming Mode

For non-streaming invocations, the runtime emits an `ExternalSubAgentProgress` heartbeat event every 5 seconds (configurable) while the subprocess is running. The heartbeat carries the elapsed time and a fixed `line: "(running...)"` message. This prevents the progress UI from appearing stale.

### 6.3 Line Buffer and Display Limits

- Lines exceeding 500 characters are truncated at 500 characters for display in progress events (the full output is still captured for the final result).
- ANSI escape sequences are stripped from lines before they appear in progress events.
- Binary or non-UTF8 content is replaced with `"(binary output)"`.

---

## 7. Token and Cost Accounting

### 7.1 Optional Extraction

Token accounting from external CLIs is optional and capability-gated. A profile may declare `outputTokenField` and `outputInputTokenField` as JSON field paths into the parsed output (only valid when `outputFormat: "json"`).

If both fields are present and parseable as integers in the JSON output:
- They are reported in `ExternalSubAgentCompletedPayload.tokensUsed`.
- They are added to the parent turn's `TokenTracker.SubAgentInputTokens` / `SubAgentOutputTokens`, consistent with how native subagent tokens are accounted.

If the fields are absent or the output format is `text`, token accounting is skipped silently for that invocation.

### 7.2 Token Reporting in Result

`SubAgentRunResult.TokensUsed` carries the extracted token counts (or `null` if unavailable). The coordinator passes this to `TokenTracker` after `RunAsync` returns.

---

## 8. Wire Protocol Extensions

### 8.1 New AppServer Notifications

The AppServer emits the following new JSON-RPC notifications to connected clients for the new session event types:

| Notification method | Corresponds to |
|--------------------|----------------|
| `session/externalSubAgentLaunched` | `ExternalSubAgentLaunched` session event |
| `session/externalSubAgentProgress` | `ExternalSubAgentProgress` session event |
| `session/externalSubAgentCompleted` | `ExternalSubAgentCompleted` session event |
| `session/externalSubAgentFailed` | `ExternalSubAgentFailed` session event |

Payload serialization follows the existing AppServer Protocol conventions: camelCase JSON, no nulls in required fields, optional fields omitted when null.

### 8.2 Backward Compatibility

Clients that do not subscribe to or handle the new notification methods must not crash or lose other session events. Existing AppServer notification routing must be additive.

### 8.3 `SubAgentProgress` Unchanged

The existing `session/subAgentProgress` notification continues to carry native subagent progress as before. External subagent progress is delivered via the new notifications rather than overloading `subAgentProgress`. Clients may display both in the same UI region.

---

## 9. Client Rendering Contract

### 9.1 CLI

- On `ExternalSubAgentLaunched`: display a spinner row with the format `[profile-name] launching…`.
- On `ExternalSubAgentProgress` (streaming line): update the spinner row to show the first 80 characters of the latest line.
- On `ExternalSubAgentProgress` (heartbeat): update the elapsed time in the spinner row.
- On `ExternalSubAgentCompleted`: replace the spinner with a checkmark and the elapsed time.
- On `ExternalSubAgentFailed`: replace the spinner with an error indicator and the first line of the error message.

### 9.2 TUI

- External subagent progress appears in the same subagent progress panel as native subagents.
- Each active external subagent occupies one row identified by `agentId`.
- The row shows: profile name, current progress line (truncated), and elapsed time.
- Completed and failed rows persist briefly (same duration as native subagent rows) before being cleared.

### 9.3 Desktop

- An external subagent card appears in the agent activity area when `ExternalSubAgentLaunched` is received.
- The card shows: profile name, working directory, and a streaming log of recent progress lines (max 20 lines shown).
- On completion, the card shows a summary with elapsed time and, if available, token counts.
- On failure, the card shows the error message with an error visual treatment.

### 9.4 Graceful Fallback

Clients that do not yet implement the new notification handlers should display a generic subagent indicator (e.g., a spinner with "external agent running") for the duration between `ExternalSubAgentLaunched` and a terminal event. This is acceptable for the initial delivery of M3.

---

## 10. Constraints and Compatibility Notes

- The new `SessionEventType` values must not break existing `switch` expressions on `SessionEventType` in adapters that do not handle them. All adapters must have a default/discard case or pattern.
- `SubAgentProgressAggregator` is extended to poll and emit `ExternalSubAgentProgress` heartbeats alongside native subagent progress snapshots. The polling interval remains ~200ms for native progress; external heartbeats have their own 5-second interval.
- `ExternalSubAgentEventSink` must be constructed by the coordinator, not by `CliOneshotRuntime` itself. The runtime receives the sink as a parameter so it remains testable with a mock sink.
- The `agentId` used in events must match the normalized label used by `SubAgentProgressBridge` for the same invocation, so clients correlating across event types see consistent identifiers.

---

## 11. Acceptance Checklist

- [ ] `ISubAgentEventSink` is extended with `OnLaunched`, `OnProgressLine`, `OnCompleted`, and `OnFailed`.
- [ ] Native runtime sink implementation is a no-op for the new methods (or passes through to existing bridge).
- [ ] `ExternalSubAgentEventSink` implementation emits lifecycle session events at correct points.
- [ ] `ExternalSubAgentLaunched`, `ExternalSubAgentProgress`, `ExternalSubAgentCompleted`, and `ExternalSubAgentFailed` session event types exist and carry correct payloads.
- [ ] `CliOneshotRuntime` calls sink methods at the correct lifecycle points.
- [ ] Streaming mode (line-by-line) and non-streaming mode (heartbeat) both produce `ExternalSubAgentProgress` events.
- [ ] ANSI stripping and line truncation are applied to progress line content.
- [ ] Token accounting extracts fields from JSON output when `outputTokenField` is configured.
- [ ] AppServer emits four new JSON-RPC notifications for the new event types.
- [ ] Existing AppServer notification flow is unaffected.
- [ ] CLI client renders launched/progress/completed/failed states correctly.
- [ ] TUI renders external subagent rows in the progress panel.
- [ ] Desktop renders external subagent cards.
- [ ] Clients without the new handlers do not crash on receiving the new notifications.
- [ ] `agentId` is consistent across all events for the same invocation.

---

## 12. Open Questions

1. Should the `ExternalSubAgentProgress` heartbeat interval (currently 5 seconds) be configurable at the global level, the profile level, or both?
2. Should progress lines from the external agent also be persisted as session items (for later replay/review), or only delivered as transient events?
3. Should streaming stdout lines be shown to the user verbatim, or should DotCraft attempt to summarize or filter them? (Preference: verbatim with ANSI stripping in M3; summarization is a future enhancement.)
4. How should the Desktop client handle a large number of concurrent external subagents (e.g., 5+ cards open at once)? Should there be a limit on visible cards?
5. Should `ExternalSubAgentLaunched` be emitted for native subagents too (for uniformity), or remain a distinguishing signal for external runtimes only?
