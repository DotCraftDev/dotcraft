# DotCraft External CLI Subagent — M6: Security, Trust, and Coordination Robustness

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [M1: Runtime Abstraction](external-cli-subagent-m1.md), [M2: One-Shot CLI Runtime](external-cli-subagent-m2.md), [M4: Persistent CLI Runtime](external-cli-subagent-m4.md), [M5: Git Worktree Isolation](external-cli-subagent-m5.md), [Session Core](session-core.md) |

Purpose: Define the trust policy model, launch approval gates, permission mode mapping, output sanitization, coordination tool surface, stale resource cleanup, and graceful degradation requirements needed to make external CLI subagent support production-ready.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. Trust Policy Model](#3-trust-policy-model)
- [4. Launch Approval Gate](#4-launch-approval-gate)
- [5. Permission Mode Mapping](#5-permission-mode-mapping)
- [6. Output Sanitization](#6-output-sanitization)
- [7. Coordination Tool Surface](#7-coordination-tool-surface)
- [8. Stale Resource Cleanup](#8-stale-resource-cleanup)
- [9. Graceful Degradation](#9-graceful-degradation)
- [10. Constraints and Compatibility Notes](#10-constraints-and-compatibility-notes)
- [11. Acceptance Checklist](#11-acceptance-checklist)
- [12. Open Questions](#12-open-questions)

---

## 1. Scope

### 1.1 What This Spec Defines

- The per-profile trust level system: `trusted`, `prompt`, and `restricted`.
- The launch approval gate: when and how DotCraft requests user confirmation before spawning an external subagent.
- Permission mode mapping: how DotCraft's approval policy translates to external CLI permission flags.
- Output sanitization rules: profile-configurable patterns that scrub sensitive content from subprocess output before it reaches session events or tool results.
- The coordination tool surface: `ListSubagents`, `CancelSubagent`, and `WaitSubagent`.
- Stale resource cleanup: comprehensive policies for orphaned processes, worktrees, and session registry entries.
- Graceful degradation: clear error behavior when external CLIs are not installed, mismatched in version, or requested to perform unsupported operations.

### 1.2 What This Spec Does Not Define

- ACP runtime implementation.
- Team-style multi-agent negotiation or agent-to-agent communication.
- Cross-machine subagent dispatch.
- Profile distribution via the skills or plugin system.
- Dashboard UI for managing subagent profiles.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. **User control over external agent trust**: Users can declare per-profile trust levels. External agents do not run autonomously by default when they are potentially dangerous code-writing tools.
2. **Approval integration**: Launch approval gates use the existing `IApprovalService` infrastructure so the approval flow is familiar across channels.
3. **Minimal over-permissioning**: DotCraft translates its own approval policy to the external CLI's permission flags where possible, rather than always granting maximum permissions to the external agent.
4. **Defense against injection and leakage**: Output from external CLIs is sanitized before entering session events or tool results, limiting the blast radius of a compromised or misbehaving external agent.
5. **Operational visibility**: The main agent has tools to enumerate active subagents, cancel them, and wait for them — making multi-subagent orchestration tractable.
6. **No orphaned resources**: All external processes, worktrees, and session registry entries are cleaned up deterministically on any shutdown or error path.

### 2.2 Non-Goals

- Sandbox-level process isolation (container/VM enforcement of directory boundaries). This is a future concern for high-trust deployments.
- Preventing external agents from making network requests or accessing files outside the worktree. DotCraft declares trust policy intent; enforcement is the user's responsibility for the external agent's own permission mode.
- Real-time monitoring of what the external agent is doing inside the subprocess (e.g., intercepting its tool calls).

---

## 3. Trust Policy Model

### 3.1 Trust Levels

Every profile declares a `trustLevel` field:

| `trustLevel` | Launch behavior | Worktree default | Notes |
|--------------|-----------------|-----------------|-------|
| `trusted` | No approval required; launches immediately | Profile default | Use for CLIs the user has vetted and configured explicitly |
| `prompt` | Requires user approval via `IApprovalService` before each launch | `worktree` if `supportsWorktree` | Default for built-in code-writing profiles |
| `restricted` | Requires approval; additionally limits allowed working directory to `workspace` (overrides `worktree`) and enforces output sanitization regardless of profile settings | `workspace` | Use for unknown or unverified CLIs |

### 3.2 Default Trust Level

- Built-in code-writing profiles (`claude-code`, `codex-cli`) default to `trustLevel: "prompt"`.
- `custom-cli-oneshot` and `custom-cli-persistent` default to `trustLevel: "restricted"`.
- `dotcraft-native` always behaves as `trusted` and is exempt from the approval gate.

### 3.3 Trust Level Override

Users can override the trust level in workspace or global config by specifying `trustLevel` in the profile entry. Reducing trust (e.g., from `trusted` to `prompt`) is always permitted. Increasing trust for a built-in profile is permitted only when the profile is declared in workspace config (not inherited from the built-in registry), to ensure intentional opt-in.

---

## 4. Launch Approval Gate

### 4.1 When Approval Is Requested

When the coordinator resolves a profile with `trustLevel: "prompt"` or `"restricted"` for a `SpawnSubagent` or `SendSubagentInput` (first call to a new session), it checks `IApprovalService` before calling `CreateSessionAsync`.

Approval is not requested on subsequent `SendSubagentInput` calls to an already-approved persistent session within the same DotCraft session. The approval is scoped to the session, not per-turn.

### 4.2 Approval Request Content

The `ApprovalRequest` emitted via `IApprovalService.RequestShellApprovalAsync` carries:

| Field | Value |
|-------|-------|
| `title` | `"Launch external subagent: <profileName>"` |
| `description` | The task instruction (first 500 characters) + working directory + runtime type |
| `riskLevel` | `Medium` for `prompt`; `High` for `restricted` |
| `context.profileName` | Profile name |
| `context.binary` | Resolved binary path |
| `context.workingDirectory` | Resolved working directory |
| `context.worktreeEnabled` | Whether a worktree will be created |

### 4.3 Approval Outcomes

| Outcome | Coordinator behavior |
|---------|----------------------|
| Approved | Proceed with `CreateSessionAsync` |
| Rejected | Return an error tool result: `"Subagent launch cancelled by user."` |
| Timed out | Treat as rejected; return an error tool result |

### 4.4 Session-Level Approval Scope Memory

Once a persistent session is approved and running, the coordinator stores an approval scope entry keyed by `(profileName, sessionId)`. Subsequent `SendSubagentInput` calls on the same session skip the approval gate. If the session is disposed and a new one is created for the same profile, approval is requested again.

### 4.5 Channel Compatibility

Channels that use `AutoApproveApprovalService` (e.g., headless automation contexts) bypass the approval gate for external subagents, consistent with their behavior for file and shell tool approvals. This is an explicit choice by the channel and must be documented in channel configuration.

---

## 5. Permission Mode Mapping

### 5.1 Purpose

DotCraft's approval policy (whether the user is present and interactive, or the channel is running in automated mode) should be translated into the appropriate permission flags for the external CLI. This prevents the external agent from receiving blanket permissions when DotCraft is operating in a more restrictive mode.

### 5.2 `permissionModeMapping` Profile Field

The profile's `permissionModeMapping` field is a dictionary mapping DotCraft approval mode names to CLI flag arrays:

```json
{
  "permissionModeMapping": {
    "interactive": ["--permission-mode", "default"],
    "auto-approve": ["--dangerously-skip-permissions"],
    "restricted": ["--permission-mode", "read-only"]
  }
}
```

The coordinator selects the appropriate flags based on the active `ApprovalPolicy` for the current session and appends them to the argument list when launching the subprocess.

### 5.3 DotCraft Approval Mode Names

| DotCraft mode | Description |
|---------------|-------------|
| `interactive` | Session uses `SessionApprovalService`; user is expected to be present |
| `auto-approve` | Session uses `AutoApproveApprovalService`; no user present |
| `restricted` | Session uses `InterruptOnApprovalService`; any approval need cancels the turn |

If `permissionModeMapping` does not contain the current mode, no additional permission flags are appended.

### 5.4 Mapping Is Advisory

DotCraft cannot enforce the external CLI's behavior. The permission mode mapping declares DotCraft's intent; the external agent may or may not honor it depending on the CLI's design and the user's local configuration of that CLI.

---

## 6. Output Sanitization

### 6.1 Purpose

External CLI output may contain sensitive content: API keys echoed in error messages, environment variable dumps, or malformed content intended to manipulate the main agent's context. Sanitization rules filter output before it enters session events or tool results.

### 6.2 `sanitizationRules` Profile Field

The profile's `sanitizationRules` field is a list of rule objects applied in order to captured output:

```json
{
  "sanitizationRules": [
    { "type": "regex-redact", "pattern": "sk-[A-Za-z0-9]{48}", "replacement": "[REDACTED_API_KEY]" },
    { "type": "regex-redact", "pattern": "Bearer [A-Za-z0-9_\\-\\.]+", "replacement": "[REDACTED_TOKEN]" },
    { "type": "max-length", "limit": 1048576 }
  ]
}
```

### 6.3 Rule Types

| Rule type | Fields | Behavior |
|-----------|--------|---------|
| `regex-redact` | `pattern`, `replacement` | Replace all matches of `pattern` (regex) with `replacement` in the output string |
| `max-length` | `limit` | Truncate output to `limit` bytes; append a truncation marker |
| `strip-ansi` | *(none)* | Remove ANSI escape sequences (always applied regardless of rules) |
| `allow-json-only` | *(none)* | Discard any content that is not valid JSON (for `outputFormat: "json"` profiles) |

ANSI stripping (`strip-ansi`) is applied unconditionally before any profile-specified rules.

### 6.4 Global Default Sanitization Rules

A set of default sanitization rules is applied to all external subagent output regardless of profile, unless explicitly overridden:

- Redact patterns matching common API key formats (OpenAI, Anthropic, etc.).
- Cap total output at 1 MB.

Profiles may add rules on top of the defaults. Profiles may not remove the default API key redaction or the global size cap.

### 6.5 Sanitization Application Points

Sanitization is applied at two points:

1. **Progress event lines** (M3): each stdout line passed to `OnProgressLine` is sanitized before being included in `ExternalSubAgentProgressPayload.line`.
2. **Final result text**: `SubAgentRunResult.Text` is sanitized before being returned as the `SpawnSubagent` tool result.

---

## 7. Coordination Tool Surface

### 7.1 `ListSubagents`

Enumerates all active external subagent sessions registered in the coordinator.

**Parameters:** None.

**Returns:** A structured list of active sessions:

| Field | Type | Description |
|-------|------|-------------|
| `agentId` | string | Session identifier |
| `profileName` | string | Profile in use |
| `status` | string | Current session status (e.g., `Ready`, `Busy`, `PendingReview`) |
| `worktreePath` | string? | Worktree path if applicable |
| `startedAt` | string | ISO timestamp of session creation |
| `turnCount` | int | Number of completed turns |

This tool is exposed to the main agent so it can make informed decisions about which sessions to reuse, cancel, or wait for.

### 7.2 `CancelSubagent`

Cancels an active external subagent session.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `agentId` | string | yes | Session to cancel |
| `discardChanges` | bool | no | If true and a worktree exists, remove the worktree without merging (default: false) |

**Behavior:**

1. Look up the session in the coordinator registry.
2. If the session is `Busy`, cancel the in-progress `RunAsync` call.
3. Call `DisposeSessionAsync` on the runtime.
4. If `discardChanges: true` and a worktree exists, remove it via `SubAgentWorktreeManager`.
5. If `discardChanges: false` and a worktree exists, leave it in `PendingReview` for later review or manual cleanup.
6. Emit `ExternalSubAgentFailed` (if cancelled mid-turn) or `ExternalSubAgentWorktreeDiscarded` (if worktree discarded).

### 7.3 `WaitSubagent`

Blocks the main agent's current tool call until a specified session completes its current turn or becomes `Ready`.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `agentId` | string | yes | Session to wait for |
| `timeoutSeconds` | int | no | Maximum wait time (default: 60) |

**Behavior:**

1. Poll the session's `Status` every 500ms until it transitions out of `Busy`, or until the timeout.
2. On `Ready`: return a result indicating the session is ready for the next input.
3. On `Unresponsive`/`Disposed`: return a result indicating the session has ended.
4. On timeout: return a result indicating the session is still running.

`WaitSubagent` is intended for orchestration scenarios where the main agent launches multiple persistent subagents concurrently and needs to synchronize before proceeding.

### 7.4 `ListSubagentProfiles`

Returns the list of available profile names and their runtime types.

**Parameters:** None.

**Returns:** Profile names, runtime type, trust level, and capability flags. Does not expose sensitive env vars or binary paths from the profile config.

This tool is read-only and carries no risk. It is also exposed as a slash command for direct user use.

---

## 8. Stale Resource Cleanup

### 8.1 Cleanup Inventory

The coordinator is responsible for cleaning up the following resource types:

| Resource | Cleanup trigger | Cleanup action |
|----------|-----------------|---------------|
| Active subprocess (one-shot) | Turn cancellation, process timeout | Kill process |
| Active subprocess (persistent, Busy) | Turn cancellation | Cancel in-progress RunAsync; kill if unresponsive |
| Active subprocess (persistent, Ready/Unresponsive) | Stale session sweep, DotCraft exit | Graceful shutdown or force kill |
| Session registry entry | Session disposal | Remove from registry |
| Worktree (Active) | Session disposal, DotCraft exit | `git worktree remove --force`; `git branch -D` |
| Worktree (PendingReview, stale) | Stale worktree sweep | Same as Active |
| Worktree (Merged) | Post-merge cleanup | Already handled in M5; sweep catches missed ones |

### 8.2 DotCraft Process Exit Cleanup

On DotCraft shutdown (graceful or crash via process exit handlers):

1. All `Busy` persistent sessions: cancel in-progress operation; force kill if it does not respond within 2 seconds.
2. All `Ready` persistent sessions: graceful shutdown signal; force kill if no exit within 5 seconds.
3. All `Active` worktrees: `git worktree remove --force`.
4. Registry cleared.

The shutdown handler is registered via `IHostApplicationLifetime.ApplicationStopping` (or equivalent) and runs before the process exits.

### 8.3 Sweep Schedule

| Sweep type | Interval | Action |
|------------|----------|--------|
| Persistent session stale sweep | 60 seconds | Dispose sessions in `Ready` with no activity for > `staleSessionTimeout` |
| Worktree stale sweep | 10 minutes | Remove `PendingReview` worktrees older than `subAgentWorktreeReviewTimeout`; remove orphaned `Active` worktrees from disposed sessions |
| Registry consistency check | 5 minutes | Remove session registry entries for processes that have exited unexpectedly |

---

## 9. Graceful Degradation

### 9.1 Binary Not Installed

When a profile's `bin` cannot be resolved:
- Return a clear error tool result: `"Profile '<name>' requires '<bin>' to be installed and available on PATH. Please install it and try again."`
- Do not leave any resources allocated (no session registry entry, no worktree).

### 9.2 Version Mismatch or Unsupported Flags

Some CLIs evolve their automation interfaces. If the configured `args` cause the subprocess to exit immediately with a usage error:
- The error is surfaced as a normal non-zero exit result.
- The tool result includes stderr content which typically contains the usage hint.
- The profile maintainer (user or DotCraft built-in) is responsible for keeping flags current.

DotCraft does not attempt to auto-detect CLI versions or auto-correct argument lists.

### 9.3 Unsupported Operation Errors

When an operation is requested that requires a capability the profile does not declare:

| Operation | Required capability | Error result |
|-----------|--------------------|----|
| `SendSubagentInput` on a one-shot session | `supportsResume` | `"Profile '<name>' does not support multi-turn sessions."` |
| `workingDirectoryMode: "worktree"` on a non-git workspace | git availability | `"Worktree isolation requires a git repository."` |
| `persistentFraming: "newline-json"` on a CLI that doesn't support JSON | N/A | Error result with subprocess stderr |

### 9.4 Channel Without Approval Support

Channels using `AutoApproveApprovalService` bypass the launch approval gate. This is by design. The channel must document this behavior to users who configure it.

Channels using `InterruptOnApprovalService` will cancel any turn that reaches the approval gate. The main agent receives a cancellation result and should surface this to the user.

### 9.5 Tool Description for Unavailable Profiles

If a profile named in a `SpawnSubagent` call does not exist in the registry, the coordinator returns:
`"Profile '<name>' is not configured. Available profiles: [list from registry]. Use ListSubagentProfiles to see all options."`

---

## 10. Constraints and Compatibility Notes

- M6 security features apply to all external runtimes (`cli-oneshot`, `cli-persistent`). The `dotcraft-native` runtime is exempt from the approval gate and trust level system.
- The approval gate integrates with `IApprovalService` and thus flows through the same `ApprovalRequest` item type and `ResolveApprovalAsync` path used by file and shell tool approvals. No new approval infrastructure is needed.
- Output sanitization must not mutate the captured output in place. A sanitized copy is used for events and tool results; internal logging (if any) uses the unsanitized copy.
- `ListSubagents`, `CancelSubagent`, `WaitSubagent`, and `ListSubagentProfiles` are exposed as agent tools. `ListSubagentProfiles` is additionally exposed as a slash command. No other M6 features require slash command surface.
- The stale cleanup sweeps must not block the main request path. They run as background tasks managed by the coordinator's hosted service lifecycle.
- Regex patterns in `sanitizationRules` are compiled once at profile load time and cached. Patterns that fail to compile surface a configuration warning at startup.

---

## 11. Acceptance Checklist

- [ ] Trust levels `trusted`, `prompt`, and `restricted` are implemented and respected by the coordinator.
- [ ] `trustLevel` defaults are correct for all built-in profiles.
- [ ] Launch approval gate calls `IApprovalService.RequestShellApprovalAsync` with correct content for `prompt` and `restricted` profiles.
- [ ] Approval is not requested for `trusted` profiles or `dotcraft-native`.
- [ ] Session-level approval scope memory prevents re-asking for the same persistent session.
- [ ] `AutoApproveApprovalService` bypasses the gate correctly.
- [ ] `permissionModeMapping` fields are resolved and correct flags are appended to subprocess args.
- [ ] ANSI stripping is always applied before any sanitization rules.
- [ ] `regex-redact`, `max-length`, `strip-ansi`, and `allow-json-only` sanitization rule types work correctly.
- [ ] Default API key redaction rules are applied to all external subagent output.
- [ ] Sanitized output is used in `ExternalSubAgentProgressPayload.line` and `SpawnSubagent` tool results.
- [ ] `ListSubagents` returns correct session information for all active sessions.
- [ ] `CancelSubagent` cancels `Busy` sessions, disposes the runtime, and optionally discards worktrees.
- [ ] `WaitSubagent` polls correctly and respects `timeoutSeconds`.
- [ ] `ListSubagentProfiles` returns profile names, runtime types, and capability flags without exposing sensitive config.
- [ ] DotCraft shutdown handler disposes all active processes and `Active` worktrees.
- [ ] Stale session sweep disposes idle persistent sessions correctly.
- [ ] Stale worktree sweep removes `PendingReview` worktrees older than the configured timeout.
- [ ] Registry consistency check removes orphaned entries for exited processes.
- [ ] Missing binary produces a clear, actionable error result with no resource allocation.
- [ ] Missing profile name returns a helpful error with available profile list.
- [ ] Unsupported operations return clear error results (no crash or hanging tool call).

---

## 12. Open Questions

1. Should the launch approval gate be per-session (once per `SpawnSubagent` that creates a new session) or per-`RunAsync` turn? (Current preference: per-session, with persistent session scope memory.)
2. Should the default sanitization rules be configurable at the global config level, or should they always be non-negotiable platform defaults?
3. Should `CancelSubagent` with `discardChanges: false` on a worktree session warn the user that the worktree will accumulate if not merged or discarded?
4. Should `WaitSubagent` be implemented as a true blocking call (blocking the LLM loop until the session is ready) or as a polling tool that returns immediately with status and expects the agent to call it again? (Current preference: polling, since true blocking would stall the turn.)
5. Should regex sanitization rules have a configurable timeout to prevent catastrophic backtracking?
6. Should `ListSubagentProfiles` be restricted to the main agent only, or also available as a user-facing slash command visible in the CLI and Desktop command palette?
