# DotCraft External CLI Subagent Design

| Field | Value |
|-------|-------|
| **Version** | 0.3.2 |
| **Status** | Active |
| **Date** | 2026-04-20 |

Purpose: define the stable architecture for DotCraft to delegate tasks to external coding CLIs while preserving DotCraft-owned approval and safety semantics.

## 1. Context

DotCraft already supports `SpawnSubagent` and has completed the following milestones:

- runtime abstraction and coordinator
- profile-driven runtime selection
- native + cli-oneshot runtime path
- approval and permission propagation across subagent boundary

The remaining design focus is no longer runtime shape, but keeping policy correctness consistent across subagent boundaries.

## 2. Scope and Direction

### 2.1 Runtime Types

Current/target runtime types:

- `native`
- `cli-oneshot`
- `acp` (future optional backend)

`cli-persistent` is intentionally dropped from scope.

### 2.2 Core Principle

DotCraft is the policy owner. Subagents are execution engines.

That means:

- native subagent tool calls must keep DotCraft approval behavior
- external CLI execution mode must be selected from DotCraft approval mode
- cancellation must reliably terminate delegated runtime process trees

## 3. Core Abstractions

### 3.1 `ISubAgentRuntime`

```csharp
public interface ISubAgentRuntime
{
    string RuntimeType { get; }
    Task<SubAgentSessionHandle> CreateSessionAsync(SubAgentProfile profile, SubAgentLaunchContext context, CancellationToken ct);
    Task<SubAgentRunResult> RunAsync(SubAgentSessionHandle session, SubAgentTaskRequest request, ISubAgentEventSink sink, CancellationToken ct);
    Task CancelAsync(SubAgentSessionHandle session, CancellationToken ct);
    Task DisposeSessionAsync(SubAgentSessionHandle session, CancellationToken ct);
}
```

`SubAgentLaunchContext` additionally carries the resolved approval mode, mapped extra launch args, the current `IApprovalService`, and the current `ApprovalContext` so runtimes receive enough policy signal without re-resolving it themselves.

### 3.2 `SubAgentProfile`

Profiles remain the runtime-specific extension point:

- `runtime`, `bin`, `args`, `env`
- `workingDirectoryMode`
- `supportsStreaming`, `supportsResume`
- `trustLevel`
- `permissionModeMapping`
- `timeout`

### 3.3 `SubAgentCoordinator`

Coordinator owns orchestration logic only:

- resolve profile and runtime
- resolve current approval mode from the active `IApprovalService`
- look up `permissionModeMapping[mode]` and attach mapped args to the launch context
- propagate `ApprovalContext` and `IApprovalService` into the runtime
- route event/progress back to DotCraft session pipeline
- aggregate final result to main agent

## 4. Execution and Coordination

### 4.1 Native Runtime

Uses DotCraft internal subagent pipeline. Runs with the same `IApprovalService` and `ApprovalContext` as the main agent turn, so sensitive tool calls made from within a subagent trigger the same approval path as equivalent main-agent calls.

### 4.2 CLI One-Shot Runtime

Uses subprocess per delegated task. DotCraft cannot introspect the external CLI's internal tool calls, so policy is applied at launch:

- mapped args from `permissionModeMapping[mode]` are appended between `profile.Args` and task payload args
- subprocess is bound to a platform-specific cleanup primitive so cancellation can terminate the full process tree

### 4.3 Multi-Turn Strategy

`SendSubagentInput` is removed from current design.

Future multi-turn behavior should be solved by a dedicated resume model (planned for a later milestone), not by adding ad-hoc inbox APIs to this milestone.

## 5. Approval and Permission Propagation

The main agent and its subagents must converge on the same approval behavior for equivalent operations. This section defines the model that makes that possible for both native and external runtimes.

### 5.1 Approval Modes

DotCraft exposes three stable modes the subagent layer understands:

| Mode | Origin `IApprovalService` | Intent |
|------|---------------------------|--------|
| `interactive` | `SessionApprovalService` / `ConsoleApprovalService` | Real user present; conservative defaults, prompt on sensitive actions |
| `auto-approve` | `AutoApproveApprovalService` | Headless automation channel; skip prompts, use permissive runtime flags |
| `restricted` | `InterruptOnApprovalService`, unknown or `null` services | Any approval need aborts the turn; runtime should launch in its most conservative mode |

`ChannelRoutingApprovalService` is resolved through its inner routing using the current `ApprovalContext.Source`, so a QQ/WeCom automation context correctly resolves to the downstream channel's mode.

### 5.2 Native Subagent Propagation

Native subagents inherit the parent session's approval pipeline instead of bypassing it:

- `SubAgentManager` receives the parent `IApprovalService` and `ApprovalContext`.
- Tool instances used inside the subagent (file, shell, and sandbox variants) share the same approval service chain as the parent turn.
- Approval requests emitted from the subagent context are prefixed with `[subagent:<label>] ` on the `path` or `command` payload field. The approval protocol shape does not change, so existing UIs render the marker as part of the human-readable target without needing schema updates.

### 5.3 External CLI Permission Mapping

External CLIs keep their own approval/sandbox semantics. DotCraft translates its own policy into the external CLI's launch flags:

- Coordinator resolves the mode with `SubAgentApprovalModeResolver`.
- Coordinator reads `profile.PermissionModeMapping[mode]` and splits it into args.
- Mapped args are appended to the subprocess command line before the task payload is delivered.
- A missing mapping entry is treated as "no extra args" (not an error).

This mapping is **advisory**: DotCraft declares intent and selects the safest available flag set; actual enforcement still depends on the external CLI implementation.

### 5.4 Built-in Profile Defaults

| Profile | `trustLevel` | Notable mapping defaults |
|---------|--------------|--------------------------|
| `native` | `trusted` | N/A (native runtime applies native approval) |
| `codex-cli` | `prompt` | `interactive` → `--sandbox read-only --ask-for-approval on-request`; `auto-approve` → `--dangerously-bypass-approvals-and-sandbox`; `restricted` → `--sandbox read-only` |
| `cursor-cli` | `prompt` | Base args kept minimal; per-mode flags supply `--mode`/`--trust` combinations matching the resolved mode |
| `custom-cli-oneshot` | `restricted` | Empty mapping by default, fully owned by the user |

Users may override any of these in workspace or global config. The built-in defaults exist so unconfigured installs still behave safely in each channel type.

### 5.5 Cancellation Semantics

Parent turn cancellation must actually terminate the whole delegated process tree:

- **Windows**: on start, the child process is bound to a `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` Job Object. On cancel/dispose, `TerminateJobObject` tears down the child and all descendants.
- **Unix**: process-group-based signaling (`SIGTERM`, then `SIGKILL` as fallback) is used so grandchild processes do not outlive the parent cancel.

Cleanup must also run on exception paths (e.g. timeout, pipe drain failure) to avoid orphan subprocesses.

## 6. Integration with Existing DotCraft Systems

### 6.1 Session/Event Pipeline

External runtime support must reuse existing session event delivery:

- progress mapped to `SubAgentProgress`
- final output returned as standard tool result payload
- cancellation reflected as normal turn cancellation result

### 6.2 Tool Surface

Current surface:

- `SpawnSubagent`

Deferred tooling (not part of current design):

- `WaitSubagent`
- explicit registry/list/cancel tools

Because main agent currently awaits subagent completion synchronously, list/cancel tools do not add practical value in the current architecture. They may return if/when the main agent loop supports truly concurrent background subagents.

## 7. Risk Focus

### 7.1 Approval Boundary Risk

Main agent and subagent must not diverge on approval behavior for equivalent operations. The native propagation design is the primary mitigation.

### 7.2 External Runtime Permission Drift

Profile defaults and runtime args must not allow a mode that is looser than the current channel approval policy. The mapping approach must fall back to a conservative default whenever the current mode cannot be resolved.

### 7.3 Lifecycle Reliability

Subprocess cancellation must terminate child process trees to avoid orphan workers and hidden side effects. Platform-specific primitives (Job Object, process group) are used because `Process.Kill(entireProcessTree: true)` alone is insufficient across all platforms and cancellation paths.

### 7.4 Prefix Marker Robustness

The `[subagent:<label>] ` marker is string-level. It is intentionally simple to keep the approval protocol stable, but it means downstream UIs must render the full target string for the marker to be visible.

## 8. Open Questions

1. Should external runtime selection stay explicit (profile-driven) or be model-selected in selected channels?
2. Should future `acp` runtime be managed in the same profile schema or split into a dedicated provider model?
3. Should `ChannelRoutingApprovalService` resolution be strict (all routes must be `auto-approve` to report `auto-approve`) or use the origin-channel route as implemented today?
4. Should profiles without any conservative mapping entry be allowed to launch in `restricted` mode, or should launch fail fast?

## 9. Milestone Status

- **M1**: Done
- **M2**: Done
- **M3**: Done
- **M4**: Dropped
- **M6**: Done (approval and permission propagation across subagent boundary; merged into this design)
