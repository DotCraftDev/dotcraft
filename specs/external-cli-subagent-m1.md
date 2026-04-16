# DotCraft External CLI Subagent — M1: Runtime Abstraction, Profile System, and Native Adapter

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |
| **Parent Spec** | [External CLI Subagent Design](external-cli-subagent-design.md), [Session Core](session-core.md) |

Purpose: Define the core runtime abstraction layer, the subagent profile configuration schema, and the migration of the existing native subagent path to use the new coordinator — establishing the foundation for external CLI subagent support with zero behavior change for current users.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. Architecture Overview](#3-architecture-overview)
- [4. `ISubAgentRuntime` Contract](#4-isubagentruntime-contract)
- [5. `SubAgentProfile` Configuration Schema](#5-subagentprofile-configuration-schema)
- [6. `SubAgentCoordinator` Responsibilities](#6-subagentcoordinator-responsibilities)
- [7. `NativeSubAgentRuntime` Adapter](#7-nativesubagentruntime-adapter)
- [8. `SpawnSubagent` Tool Evolution](#8-spawnsubagent-tool-evolution)
- [9. Profile Registry and Built-In Profiles](#9-profile-registry-and-built-in-profiles)
- [10. Constraints and Compatibility Notes](#10-constraints-and-compatibility-notes)
- [11. Acceptance Checklist](#11-acceptance-checklist)
- [12. Open Questions](#12-open-questions)

---

## 1. Scope

### 1.1 What This Spec Defines

- The `ISubAgentRuntime` interface: session creation, task execution, cancellation, and disposal lifecycle.
- The `SubAgentProfile` configuration model and the `[ConfigSection("SubAgentProfiles")]` schema.
- The `SubAgentCoordinator` responsibilities: profile resolution, runtime routing, and lifecycle ownership.
- The `NativeSubAgentRuntime` adapter that wraps the existing `SubAgentManager` behind the new interface.
- The evolution of the `SpawnSubagent` tool to accept an optional `profile` parameter.
- The built-in `dotcraft-native` profile and the profile registry contract.
- Backward compatibility constraints that guarantee zero behavior change for existing users.

### 1.2 What This Spec Does Not Define

- Any external CLI process management or subprocess launching. Those are defined in the M2 and M4 specs.
- Git worktree allocation or isolation. That is defined in the M5 spec.
- Session event pipeline extensions for external subagent visibility. That is defined in the M3 spec.
- Approval gates or trust policies for subagent launch. Those are defined in the M6 spec.
- ACP runtime implementation or any runtime type beyond `native`.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. **Introduce a stable runtime abstraction**: Define `ISubAgentRuntime` as the single contract that all subagent backends — native or external — implement. All routing logic lives in the coordinator, not in tool implementations.
2. **Introduce a profile-based configuration model**: `SubAgentProfile` captures all runtime-specific settings (binary, args, env, capability flags) in a named, composable structure that can be extended without code changes.
3. **Preserve all existing native subagent behavior**: The `NativeSubAgentRuntime` adapter wraps `SubAgentManager` without altering its tool set, concurrency model, approval behavior, progress reporting, or token accounting.
4. **Expose profile selection to the main agent**: `SpawnSubagent` gains a `profile` parameter so the main agent can delegate to a named profile. Defaulting to `dotcraft-native` preserves existing call sites.
5. **Establish extension points for future milestones**: The coordinator and runtime interface must be extensible to one-shot CLI, persistent CLI, and worktree-isolated runtimes without restructuring.

### 2.2 Non-Goals

- Implementing any external runtime. The only runtime after M1 is `native`.
- Changing the tool set, approval behavior, or concurrency limits of the native subagent path.
- Streaming external process output into the session event pipeline.
- Exposing profile management through slash commands or the Dashboard.
- Supporting profile distribution via the skills or plugin system.

---

## 3. Architecture Overview

M1 introduces a coordinator layer between the `SpawnSubagent` tool and the concrete subagent execution path.

```
SpawnSubagent tool
       │ profile name (or default "dotcraft-native")
       ▼
SubAgentCoordinator
       │ resolves SubAgentProfile → selects ISubAgentRuntime
       ▼
ISubAgentRuntime  ◄──── NativeSubAgentRuntime (wraps SubAgentManager)
                  ◄──── CliOneshotRuntime       [M2]
                  ◄──── CliPersistentRuntime    [M4]
```

The coordinator owns the profile registry and is the sole point of runtime selection. The tool layer does not need to know which runtime backs a given profile.

---

## 4. `ISubAgentRuntime` Contract

### 4.1 Interface Responsibilities

An `ISubAgentRuntime` implementation must:

- Create or attach to a subagent session given a profile and launch context.
- Execute a delegated task on that session, streaming progress through a sink.
- Support cooperative cancellation at any point during execution.
- Dispose session resources deterministically when the session ends.
- Declare its runtime type identifier and capability set.

### 4.2 Session Handle

A `SubAgentSessionHandle` is an opaque value returned by `CreateSessionAsync`. It carries sufficient state for the runtime to route subsequent calls to the correct subprocess or in-process context. The coordinator holds session handles for active subagents.

### 4.3 Task Request and Result

`SubAgentTaskRequest` carries:

- `Task` — the instruction text submitted to the subagent.
- `Label` — display name for progress UI, consistent with the existing `NormalizeLabel` convention.
- `WorkingDirectory` — the directory the subagent should treat as its root (resolved by the coordinator before calling the runtime).

`SubAgentRunResult` carries:

- `Text` — the final response text from the subagent.
- `IsError` — whether the result represents a failure.
- `TokensUsed` — optional input/output token counts (present only when the runtime can report them).

### 4.4 Event Sink

`ISubAgentEventSink` is the channel through which a runtime reports incremental progress. In M1 its implementation delegates directly to `SubAgentProgressBridge`, reusing the existing progress reporting path without modification. Later milestones will extend the sink to carry structured events from external processes.

### 4.5 Lifecycle Rules

- `CreateSessionAsync` is called once per `SpawnSubagent` invocation for runtimes where session setup is distinct from task execution.
- `RunAsync` may be called once (one-shot) or multiple times (persistent). In M1, `NativeSubAgentRuntime` expects exactly one `RunAsync` call per session.
- `DisposeSessionAsync` is always called, even when `RunAsync` throws or is cancelled.
- `CancelAsync` is called when the parent turn is cancelled before `RunAsync` returns. Implementations must interrupt their work promptly and allow `DisposeSessionAsync` to proceed.

---

## 5. `SubAgentProfile` Configuration Schema

### 5.1 Config Section

Profiles are declared under `[ConfigSection("SubAgentProfiles")]` as a named dictionary. Each entry is a `SubAgentProfile` object.

Example config layout:

```json
{
  "SubAgentProfiles": {
    "dotcraft-native": { "runtime": "native" },
    "claude-code": {
      "runtime": "cli-oneshot",
      "bin": "claude",
      "args": ["--print", "--output-format", "json"],
      "workingDirectoryMode": "workspace"
    }
  }
}
```

### 5.2 Profile Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `runtime` | string | yes | Runtime type identifier: `native`, `cli-oneshot`, `cli-persistent` |
| `bin` | string | for CLI runtimes | Executable name or absolute path |
| `args` | string[] | for CLI runtimes | Fixed argument list prepended to every invocation |
| `env` | object | no | Environment variable overrides applied to the subprocess |
| `workingDirectoryMode` | string | no | `workspace` (default), `worktree` [M5], or `specified` |
| `supportsStreaming` | bool | no | Whether the runtime can stream incremental output |
| `supportsResume` | bool | no | Whether the runtime supports multi-turn message exchange |
| `supportsWorktree` | bool | no | Whether the runtime can be isolated via git worktree [M5] |
| `supportsModelSelection` | bool | no | Whether a model identifier can be injected via args |
| `inputFormat` | string | no | `text` (default) or `json` |
| `outputFormat` | string | no | `text` (default) or `json` |
| `timeout` | int | no | Per-task timeout in seconds; 0 means no limit |
| `trustLevel` | string | no | `trusted`, `prompt`, or `restricted` [M6] |
| `permissionModeMapping` | object | no | DotCraft approval mode → CLI flag mapping [M6] |
| `sanitizationRules` | object | no | Output sanitization rules [M6] |

Fields that are not applicable to `native` profiles are ignored by `NativeSubAgentRuntime`.

### 5.3 Profile Validation

The coordinator validates profiles at startup:

- `runtime` must be a registered runtime type identifier.
- CLI-specific fields (`bin`, `args`) must not be present on `native` profiles.
- Unknown fields are preserved (forward-compatible).

Validation failures surface as configuration warnings, not startup errors, unless the profile is the default.

---

## 6. `SubAgentCoordinator` Responsibilities

### 6.1 Profile Resolution

Given a profile name, the coordinator:

1. Looks up the name in the profile registry (user-configured profiles merged with built-in profiles, with user-configured taking precedence).
2. Falls back to `dotcraft-native` if the profile name is absent and a default is needed.
3. Returns a resolved `SubAgentProfile` or an error if the name does not exist and was explicitly specified.

### 6.2 Runtime Selection

The coordinator maintains a registry of `ISubAgentRuntime` implementations keyed by `runtime` string. Profile resolution produces a profile; the coordinator selects the runtime by matching `profile.Runtime`.

### 6.3 Lifecycle Management

The coordinator:

- Calls `CreateSessionAsync` before the first task.
- Passes the session handle and task request to `RunAsync`.
- Calls `DisposeSessionAsync` in a `finally` block.
- Propagates cancellation from the parent turn's `CancellationToken`.

In M1, session handles are transient (one per `SpawnSubagent` invocation). Later milestones introduce persistent session state for the coordinator.

### 6.4 Working Directory Resolution

The coordinator resolves the effective working directory before calling the runtime:

- `workspace` — the current workspace root.
- `specified` — the directory passed by the main agent in the tool call (validated to be within or adjacent to the workspace).
- `worktree` [M5] — a coordinator-allocated git worktree path.

---

## 7. `NativeSubAgentRuntime` Adapter

### 7.1 Behavioral Contract

`NativeSubAgentRuntime` wraps `SubAgentManager.SpawnAsync` behind `ISubAgentRuntime`. Its behavior must be identical to the current `SpawnSubagent` tool behavior:

- Same tool set (read, write, grep, find, exec, web).
- Same concurrency gate (`SubagentMaxConcurrency`).
- Same progress reporting via `SubAgentProgressBridge`.
- Same token accounting via `TokenTracker.SubAgentInputTokens` / `SubAgentOutputTokens`.
- Same error handling: exceptions become `"Error: {message}"` result strings.
- No approval service (`approvalService: null`).

### 7.2 Session Handle

`NativeSubAgentRuntime.CreateSessionAsync` returns a handle immediately without allocating resources. `RunAsync` calls `SubAgentManager.SpawnAsync`, which performs all internal setup. `DisposeSessionAsync` is a no-op.

### 7.3 Token Reporting

`NativeSubAgentRuntime` reports token counts in `SubAgentRunResult.TokensUsed` by reading from the existing `TokenTracker` after `SpawnAsync` returns, preserving the current accounting behavior.

---

## 8. `SpawnSubagent` Tool Evolution

### 8.1 New Parameter

The `SpawnSubagent` tool gains one new optional parameter:

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `profile` | string | `dotcraft-native` | Named profile to use for this subagent invocation |

All existing parameters (`task`, `label`) are unchanged.

### 8.2 Backward Compatibility

- Calls omitting `profile` behave identically to current behavior.
- The tool description exposed to the LLM is updated to mention the `profile` option, but existing prompts and tool call patterns continue to work without modification.

### 8.3 Tool Description Update

The tool description should convey:

- DotCraft native subagents are the default for research, exploration, and parallel investigation tasks.
- Named profiles enable delegation to external coding agents where the profile has been configured by the user.
- The tool description must not enumerate available profile names (these are workspace-specific).

---

## 9. Profile Registry and Built-In Profiles

### 9.1 Registry Resolution Order

1. User-configured profiles from workspace `.craft/config.json` under `SubAgentProfiles`.
2. User-configured profiles from global `~/.craft/config.json` under `SubAgentProfiles`.
3. Built-in profiles compiled into the coordinator.

A profile name present in workspace config takes precedence over the same name in global config, which takes precedence over built-in defaults.

### 9.2 Built-In Profiles in M1

| Name | Runtime | Notes |
|------|---------|-------|
| `dotcraft-native` | `native` | Default; wraps existing `SubAgentManager` |

Built-in profiles for `claude-code`, `codex-cli`, and `custom-cli-oneshot` are defined in M2. They are referenced in the profile schema documentation but not registered in M1.

### 9.3 Profile Discoverability

A `ListSubagentProfiles` administrative action (not a main agent tool) is reserved for M6 when the full coordination toolkit is introduced. In M1, configured profile names are visible in the Dashboard schema output.

---

## 10. Constraints and Compatibility Notes

- The existing `SubAgentManager` class is retained and remains the implementation behind `NativeSubAgentRuntime`. It is not removed or restructured in M1.
- `CoreToolProvider` and `SandboxToolProvider` continue to construct `SubAgentManager` internally but route all `SpawnSubagent` calls through `SubAgentCoordinator`.
- The `AgentTools.SpawnSubagent` function signature change (adding `profile`) must not break any serialized agent history or tool call records.
- All existing tests that exercise `SpawnSubagent` must pass without modification. New tests verify that the coordinator correctly routes to `NativeSubAgentRuntime` when the profile is `dotcraft-native` or absent.
- Config schema auto-generation for the Dashboard is updated to reflect the new `SubAgentProfiles` section.

---

## 11. Acceptance Checklist

- [ ] `ISubAgentRuntime` interface is defined with `CreateSessionAsync`, `RunAsync`, `CancelAsync`, and `DisposeSessionAsync`.
- [ ] `SubAgentSessionHandle`, `SubAgentTaskRequest`, `SubAgentRunResult`, and `ISubAgentEventSink` are defined.
- [ ] `SubAgentProfile` config model is defined with all fields in §5.2.
- [ ] `[ConfigSection("SubAgentProfiles")]` is registered and appears in Dashboard schema output.
- [ ] `SubAgentCoordinator` resolves profiles, selects runtimes, and manages the lifecycle.
- [ ] `NativeSubAgentRuntime` wraps `SubAgentManager.SpawnAsync` and produces identical results to the previous direct path.
- [ ] `SpawnSubagent` tool exposes the `profile` parameter; calls without `profile` default to `dotcraft-native`.
- [ ] All existing `SpawnSubagent` tests pass without modification.
- [ ] New coordinator routing tests pass: named profile → correct runtime; absent profile → native runtime.
- [ ] Profile validation surfaces a warning for unknown runtime types.
- [ ] The `dotcraft-native` built-in profile is registered and active.

---

## 12. Open Questions

1. Should the coordinator be a `DotCraft.Core` class or a module-level class? (Current preference: `DotCraft.Core`, parallel to `SubAgentManager`.)
2. Should `ISubAgentRuntime` implementations be discovered via DI or registered explicitly in the coordinator? (Preference: explicit registration to avoid coupling to DI container specifics.)
3. Should `SubAgentProfile` support inheritance (a profile extending another profile)? Useful for custom overrides of `claude-code` defaults but adds complexity.
4. Should profile validation errors block startup for explicitly specified profiles in the default config key, or always be warnings?
