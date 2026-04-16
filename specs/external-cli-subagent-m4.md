# DotCraft External CLI Subagent — M4: Persistent CLI Runtime

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [M1: Runtime Abstraction](external-cli-subagent-m1.md), [M3: Session Integration](external-cli-subagent-m3.md), [External CLI Subagent Design](external-cli-subagent-design.md) |

Purpose: Define the behavior, communication protocol, health monitoring, lifecycle management, and tool surface for persistent (long-lived) external CLI subagent sessions — enabling multi-turn iterative task delegation without restarting the external agent process between turns.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. Persistent Runtime Model](#3-persistent-runtime-model)
- [4. Process Communication Protocol](#4-process-communication-protocol)
- [5. Session Handle and State](#5-session-handle-and-state)
- [6. Multi-Turn Message Exchange](#6-multi-turn-message-exchange)
- [7. Health Monitoring and Recovery](#7-health-monitoring-and-recovery)
- [8. Shutdown and Cleanup](#8-shutdown-and-cleanup)
- [9. `SendSubagentInput` Tool](#9-sendsubagentinput-tool)
- [10. Built-In Persistent Profile](#10-built-in-persistent-profile)
- [11. Session Registry](#11-session-registry)
- [12. Constraints and Compatibility Notes](#12-constraints-and-compatibility-notes)
- [13. Acceptance Checklist](#13-acceptance-checklist)
- [14. Open Questions](#14-open-questions)

---

## 1. Scope

### 1.1 What This Spec Defines

- The `CliPersistentRuntime` implementation of `ISubAgentRuntime` for long-lived subprocess management.
- The stdin/stdout communication framing contract between DotCraft and a persistent CLI agent.
- The `PersistentSubAgentSessionHandle` state model including process handle, framing state, and health status.
- Multi-turn message exchange: how follow-up instructions are sent to an already-running agent.
- Health monitoring: subprocess liveness detection, stale session detection, and crash recovery behavior.
- Graceful and forced shutdown procedures with deterministic resource cleanup.
- The `SendSubagentInput` tool that allows the main agent to send follow-up instructions to a running subagent.
- The persistent-mode profile for `claude-code` and the `custom-cli-persistent` template profile.
- The `SubAgentCoordinator`'s session registry for active persistent sessions.

### 1.2 What This Spec Does Not Define

- Git worktree lifecycle for persistent subagents. That is defined in the M5 spec.
- Trust policies and launch approval for persistent sessions. Those are defined in the M6 spec.
- Cross-DotCraft-restart session persistence. Persistent sessions do not survive process restart in this milestone.
- The `ListSubagents`, `CancelSubagent`, and `WaitSubagent` coordination tools. Those are defined in the M6 spec.
- Vendor-specific structured tool event parsing from the persistent agent's output.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. **Iterative coding delegation**: The main agent can start a persistent external CLI session for a coding task and send follow-up refinement instructions without restarting the agent process.
2. **Stable subprocess lifecycle**: The persistent runtime starts the subprocess once, maintains a stable communication channel, and keeps the process alive across multiple `SendSubagentInput` calls.
3. **Health awareness**: The runtime detects subprocess crashes and unresponsive agents and surfaces the failure clearly to the main agent.
4. **Deterministic cleanup**: Every persistent session has a defined shutdown path. Sessions do not become zombie processes when DotCraft exits or when the parent turn completes.
5. **Profile-configurable framing**: The communication framing (delimiter, encoding, timeout) is specified in the profile so that `CliPersistentRuntime` works with any CLI that supports a machine-readable interactive mode, not just a specific vendor.

### 2.2 Non-Goals

- Guaranteeing that the persistent agent's internal context (chat history, memory) survives across DotCraft restarts.
- Implementing agent-to-agent negotiation or back-channel communication between concurrent subagents.
- Parsing the persistent agent's tool calls or internal reasoning into DotCraft session items.
- Supporting arbitrary terminal (TTY) modes. The persistent runtime uses redirected stdio only.

---

## 3. Persistent Runtime Model

A persistent subagent session has a longer lifetime than a single `SpawnSubagent` tool call:

```
SpawnSubagent(profile: "claude-code-persistent", task: "...")
       │ session does not exist → CliPersistentRuntime.CreateSessionAsync
       │ subprocess started, session handle registered in coordinator
       ▼
CliPersistentRuntime.RunAsync
       │ sends initial task, reads response, returns result
       │ session handle remains in registry (process still running)
       ▼
tool result → main agent

... later turns ...

SendSubagentInput(agentId: "...", message: "...")
       │ coordinator looks up session handle
       ▼
CliPersistentRuntime.RunAsync (subsequent turn)
       │ sends follow-up, reads response, returns result
       ▼
tool result → main agent

... eventually ...

CancelSubagent(agentId: "...") or session timeout
       ▼
CliPersistentRuntime.DisposeSessionAsync
       │ graceful shutdown signal, then force-kill if needed
```

The subprocess is started exactly once per persistent session and lives until the session is explicitly disposed or the coordinator performs cleanup.

---

## 4. Process Communication Protocol

### 4.1 Transport

Communication uses the subprocess's redirected stdin (DotCraft → agent) and stdout (agent → DotCraft). Stderr is captured separately for diagnostic purposes and fed into the event sink as `isStderr: true` lines.

### 4.2 Framing Modes

The profile's `persistentFraming` field selects the framing mode:

| `persistentFraming` | Description |
|---------------------|-------------|
| `newline-json` | Each message is a single line of JSON on stdin; each response is a single line of JSON on stdout. This is the preferred mode for CLIs that support it. |
| `sentinel` | Each message is a block of text terminated by a sentinel string (specified in `persistentSentinel`); response is terminated by the same sentinel on stdout. Useful for CLIs that do not support JSON but do support structured delimiters. |
| `eof-per-turn` | Each message is written to stdin then stdin is closed; the response is all of stdout until EOF. A new stdin pipe is opened for the next turn. Some CLIs treat each stdin-close as end of input. |

Default `persistentFraming`: `newline-json`.

### 4.3 JSON Message Shapes

When `persistentFraming` is `newline-json`, DotCraft sends:

```json
{"role": "user", "content": "<task text>"}
```

And expects the CLI to respond with a JSON object containing at minimum a `content` field (or the path specified by `outputJsonPath`):

```json
{"role": "assistant", "content": "<response text>", "usage": {"input_tokens": 123, "output_tokens": 456}}
```

The `usage` field is optional. Unknown fields in the response are ignored.

### 4.4 Framing Timeout

After sending a message, the runtime waits up to `profile.timeout` seconds for the first byte of the response. If no byte arrives within that window, the session is declared unresponsive (see §7). While the response is arriving, the timeout resets on each received line (to accommodate slow streaming responses).

### 4.5 Profile-Specified Framing

All framing parameters are profile-configurable:

| Profile field | Purpose |
|---------------|---------|
| `persistentFraming` | Framing mode (see §4.2) |
| `persistentSentinel` | Sentinel string for `sentinel` framing mode |
| `persistentStartupArgs` | Extra args used only when starting the persistent process (e.g., `--interactive`) |
| `persistentReadySignal` | Regex pattern matched against stdout that signals the process is ready to receive the first message |
| `persistentShutdownSignal` | String or JSON message sent to stdin to request graceful shutdown |

---

## 5. Session Handle and State

### 5.1 `PersistentSubAgentSessionHandle`

A persistent session handle carries:

| Field | Type | Description |
|-------|------|-------------|
| `AgentId` | string | Normalized label used across events and tool calls |
| `ProfileName` | string | Name of the profile that created this session |
| `Process` | Process | The OS process object |
| `StdinWriter` | StreamWriter | Writer for the process's stdin (kept open across turns) |
| `StdoutReader` | StreamReader | Reader for the process's stdout |
| `Status` | enum | `Starting`, `Ready`, `Busy`, `Unresponsive`, `Disposed` |
| `TurnCount` | int | Number of completed `RunAsync` turns on this session |
| `LastActivityAt` | DateTimeOffset | Timestamp of last successful message exchange |
| `CancellationSource` | CancellationTokenSource | Used to cancel in-progress reads |

### 5.2 Status Transitions

```
Starting → Ready       (process started, ready signal received or startup timeout passed)
Ready    → Busy        (RunAsync called, message sent, awaiting response)
Busy     → Ready       (response received, RunAsync completed)
Busy     → Unresponsive (response timeout or no first-byte within framing timeout)
Unresponsive → Disposed (cleanup invoked after unresponsive detection)
Ready    → Disposed    (graceful shutdown or DisposeSessionAsync called)
```

The `Disposed` state is terminal. Once a session handle is disposed, it is removed from the coordinator's session registry and must not be reused.

---

## 6. Multi-Turn Message Exchange

### 6.1 Initial Turn via `SpawnSubagent`

The first `SpawnSubagent` call with a persistent profile:
1. Triggers `CreateSessionAsync` which starts the subprocess and waits for the ready signal.
2. Triggers `RunAsync` which sends the initial task and returns the first response.
3. The session handle remains `Ready` in the coordinator's registry.

### 6.2 Subsequent Turns via `SendSubagentInput`

Subsequent instructions to the same persistent session use `SendSubagentInput` (see §9), which:
1. Looks up the session handle in the coordinator registry by `agentId`.
2. Calls `CliPersistentRuntime.RunAsync` with the follow-up message on the existing session handle.
3. Returns the response as a tool result.

### 6.3 Concurrent Turn Protection

A persistent session cannot handle concurrent `RunAsync` calls. If `SendSubagentInput` is called while the session is `Busy`:
- The call is rejected with an error result: `"Agent agentId is currently busy. Wait for the current turn to complete before sending another message."`.
- The rejection does not affect the in-progress turn.

### 6.4 Context Continuity

DotCraft does not manage the external agent's internal context. The external CLI is responsible for maintaining its own chat history across turns. DotCraft's role is to deliver the next message and capture the response.

---

## 7. Health Monitoring and Recovery

### 7.1 Liveness Detection

The runtime checks subprocess liveness:
- After each completed `RunAsync` turn, confirm the process has not exited.
- Before each `RunAsync` call, confirm `Status` is `Ready` (not `Unresponsive` or `Disposed`).
- If the process has exited unexpectedly between turns, set `Status` to `Disposed`, emit `ExternalSubAgentFailed`, and remove the session from the registry.

### 7.2 Unresponsive Detection

A session becomes `Unresponsive` when:
- The framing timeout elapses with no response bytes received.
- The process is still running (has not exited).

On unresponsive detection:
1. Set `Status` to `Unresponsive`.
2. Emit `ExternalSubAgentFailed` with a message indicating the timeout.
3. Send a kill signal to the subprocess.
4. Set `Status` to `Disposed` and remove from registry.
5. Return an error `SubAgentRunResult` to the caller.

### 7.3 Stale Session Cleanup

The coordinator runs a background cleanup sweep every 60 seconds (configurable). Sessions in `Ready` status with no activity for longer than `profile.staleSessionTimeout` (default: 30 minutes) are automatically disposed. The sweep calls `DisposeSessionAsync` on stale sessions.

### 7.4 No Automatic Reconnect

When a session is lost (crash, timeout, or stale cleanup), it is not automatically restarted. The main agent receives an error result and may choose to call `SpawnSubagent` again to create a new session.

---

## 8. Shutdown and Cleanup

### 8.1 Graceful Shutdown

When `DisposeSessionAsync` is called:
1. If `profile.persistentShutdownSignal` is configured, write it to stdin and wait up to 5 seconds for the process to exit cleanly.
2. If the process does not exit within 5 seconds, proceed to forced shutdown.

### 8.2 Forced Shutdown

If graceful shutdown fails or is not configured:
1. Send a kill signal to the process.
2. Wait up to 2 seconds for the process to exit.
3. Release all OS handles (stdin writer, stdout reader, process object).

### 8.3 DotCraft Process Exit

When the DotCraft process exits, all active persistent session handles must be disposed. The coordinator registers a disposal callback on application shutdown that calls `DisposeSessionAsync` on all registered sessions. Forced shutdown is used if graceful shutdown does not complete within 10 seconds total.

### 8.4 Cleanup Guarantees

- `DisposeSessionAsync` must not throw even if the process is already dead.
- The session is removed from the coordinator registry before OS handles are released.
- Cleanup is idempotent: calling `DisposeSessionAsync` on an already-disposed session is a no-op.

---

## 9. `SendSubagentInput` Tool

### 9.1 Purpose

`SendSubagentInput` allows the main agent to send a follow-up instruction to a running persistent subagent without creating a new subprocess. It is the primary interaction primitive for multi-turn external agent collaboration.

### 9.2 Parameters

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `agentId` | string | yes | The `agentId` of an active persistent session (as seen in `ExternalSubAgentLaunched` events or returned by `SpawnSubagent`) |
| `message` | string | yes | The follow-up instruction text |

### 9.3 Behavior

1. Look up the session in the coordinator registry by `agentId`.
2. If not found: return an error result listing known active `agentId` values.
3. If found but `Status` is `Busy`: return a rejection result (see §6.3).
4. If found and `Status` is `Ready`: call `CliPersistentRuntime.RunAsync` with the message and return the response.
5. If found but `Status` is `Unresponsive` or `Disposed`: return an error result indicating the session has ended.

### 9.4 Tool Description

The tool description exposed to the main agent must convey:
- This tool is only valid for persistent sessions started via `SpawnSubagent` with a persistent-capable profile.
- The `agentId` is provided in the result of the initial `SpawnSubagent` call.
- Calling `SendSubagentInput` on a completed one-shot session returns an error.

---

## 10. Built-In Persistent Profile

### 10.1 `claude-code` (persistent mode)

The M2 `claude-code` profile is for one-shot invocations. A separate profile activates the persistent mode:

> **Note:** The exact flags, framing, and startup behavior for Claude Code's interactive/streaming mode must be validated against the actual CLI during implementation. The values below represent design intent only.

| Field | Value |
|-------|-------|
| `runtime` | `cli-persistent` |
| `bin` | `claude` |
| `persistentFraming` | `newline-json` |
| `persistentStartupArgs` | `["--output-format", "stream-json"]` |
| `persistentReadySignal` | *(to be determined during research)* |
| `persistentShutdownSignal` | `{"type": "exit"}` |
| `supportsStreaming` | true |
| `supportsResume` | true |
| `timeout` | 120 |
| `staleSessionTimeout` | 1800 |

### 10.2 `custom-cli-persistent`

A template profile for user-defined persistent CLIs. Required fields: `bin`, `persistentFraming`. All other fields have defaults.

| Field | Value |
|-------|-------|
| `runtime` | `cli-persistent` |
| `bin` | *(required)* |
| `persistentFraming` | `newline-json` |
| `timeout` | 120 |
| `staleSessionTimeout` | 1800 |

---

## 11. Session Registry

### 11.1 Registry Contract

The coordinator maintains a `ConcurrentDictionary<string, PersistentSubAgentSessionHandle>` keyed by `agentId`. This registry is:

- Written on `CreateSessionAsync` (add new handle).
- Read on `RunAsync` (look up existing handle for `SendSubagentInput`).
- Written on `DisposeSessionAsync` (remove handle).

### 11.2 Concurrency Limits

The number of concurrent persistent sessions is bounded by `SubagentPersistentMaxConcurrency` (default: 3, configurable). Attempting to create a new persistent session when the limit is reached returns an error result suggesting that an existing session should be cancelled first.

### 11.3 Session Enumeration

The registry is readable by the coordinator for the `ListSubagents` tool [M6]. In M4, no public enumeration tool exists; the registry is internal state.

### 11.4 agentId Generation

The `agentId` for a persistent session is the normalized label derived from the `SpawnSubagent` call's `label` and `task` parameters, consistent with `SubAgentManager.NormalizeLabel`. This ensures the same label appears in session events, tool results, and `SendSubagentInput` calls.

---

## 12. Constraints and Compatibility Notes

- `CliPersistentRuntime` and `CliOneshotRuntime` are separate implementations sharing the `ISubAgentRuntime` interface. They may share utility classes for process management but must not share mutable state.
- One-shot sessions created via `CliOneshotRuntime` are not entered into the session registry. `SendSubagentInput` cannot target a one-shot session.
- The persistent session registry is in-memory only and is cleared on DotCraft restart. There is no persistence file for session handles.
- The M3 event sink and event types apply to persistent sessions identically. `ExternalSubAgentLaunched` is emitted when the subprocess starts; `ExternalSubAgentProgress` carries streaming lines; `ExternalSubAgentCompleted` or `ExternalSubAgentFailed` is emitted after each `RunAsync` call, not only after session disposal.
- The `profile.timeout` applies per `RunAsync` call (per turn), not to the entire persistent session lifetime.

---

## 13. Acceptance Checklist

- [ ] `CliPersistentRuntime` implements `ISubAgentRuntime` and is registered in the coordinator for `runtime: "cli-persistent"`.
- [ ] `CreateSessionAsync` starts the subprocess, waits for the ready signal (or startup timeout), and registers the session handle in the coordinator registry.
- [ ] All three framing modes (`newline-json`, `sentinel`, `eof-per-turn`) are implemented and selectable via the profile.
- [ ] `RunAsync` sends the message in the configured framing, reads the response, and returns `SubAgentRunResult`.
- [ ] Follow-up turns via `SendSubagentInput` use the existing process without restarting.
- [ ] Concurrent turn rejection works: `SendSubagentInput` on a `Busy` session returns a clear error result.
- [ ] Subprocess crash between turns is detected; `ExternalSubAgentFailed` is emitted; session is removed from registry.
- [ ] Framing timeout detection works; unresponsive session is killed and removed.
- [ ] Stale session cleanup sweep runs at the configured interval and disposes sessions exceeding `staleSessionTimeout`.
- [ ] Graceful shutdown sends `persistentShutdownSignal` and waits; forced kill is used on timeout.
- [ ] DotCraft process exit triggers cleanup of all registered persistent sessions.
- [ ] `SendSubagentInput` tool is exposed to the main agent with correct parameter schema.
- [ ] Built-in profiles `claude-code` (persistent) and `custom-cli-persistent` are registered.
- [ ] `SubagentPersistentMaxConcurrency` limit is enforced; excess requests return a clear error result.
- [ ] M3 event sink integration: launched/progress/completed/failed events emitted correctly per turn.
- [ ] End-to-end test with a real persistent CLI (e.g., a Python REPL in `newline-json`-compatible mode) validates the multi-turn exchange.

---

## 14. Open Questions

1. Should the `claude-code` persistent profile name be `claude-code-persistent` (distinct from the oneshot profile) or should a single `claude-code` profile support both modes via a `mode` field?
2. How should the ready signal timeout be handled if a CLI takes longer than expected to start (e.g., downloading a model)? Should there be a separate `startupTimeout` distinct from the per-turn `timeout`?
3. Should `SendSubagentInput` be gated on the same approval flow as `SpawnSubagent` for profiles with `trustLevel: "prompt"`, or is one approval at session creation sufficient?
4. Should the event model emit `ExternalSubAgentCompleted`/`ExternalSubAgentFailed` after each turn, or only at session disposal? (Current preference: after each turn, so the main agent can see per-turn results in real time.)
5. Should persistent sessions support a `context` field (conversation history as JSON) that DotCraft maintains and injects at the start of each `RunAsync` call, to supplement the external CLI's own memory?
