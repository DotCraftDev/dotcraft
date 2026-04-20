# DotCraft External CLI Subagent Design

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Active |
| **Date** | 2026-04-20 |

Purpose: define the stable architecture for DotCraft to delegate tasks to external coding CLIs while preserving DotCraft-owned approval and safety semantics.

## 1. Context

DotCraft already supports `SpawnSubagent` and has completed the first three milestones:

- runtime abstraction and coordinator
- profile-driven runtime selection
- native + cli-oneshot runtime path

The remaining design focus is no longer runtime shape, but policy correctness across subagent boundaries.

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

### 3.2 `SubAgentProfile`

Profiles remain the runtime-specific extension point:

- `runtime`, `bin`, `args`, `env`
- `workingDirectoryMode`
- `supportsStreaming`, `supportsResume`
- `permissionModeMapping`
- `timeout`

### 3.3 `SubAgentCoordinator`

Coordinator owns orchestration logic only:

- resolve profile and runtime
- prepare launch context
- apply permission mode mapping for external CLI
- route event/progress back to DotCraft session pipeline
- aggregate final result to main agent

## 4. Execution and Coordination

### 4.1 Native Runtime

Uses DotCraft internal subagent pipeline. Requirement: keep `IApprovalService` and `ApprovalContext` semantics intact when calls are made from subagent-internal tools.

### 4.2 CLI One-Shot Runtime

Uses subprocess per delegated task. Requirement: map DotCraft approval mode to runtime launch flags through `permissionModeMapping`, then execute with deterministic cleanup and cancellation.

### 4.3 Multi-Turn Strategy

`SendSubagentInput` is removed from current design.

Future multi-turn behavior should be solved by a dedicated resume model (planned for a later milestone), not by adding ad-hoc inbox APIs to this milestone.

## 5. Integration with Existing DotCraft Systems

### 5.1 Session/Event Pipeline

External runtime support must reuse existing session event delivery:

- progress mapped to `SubAgentProgress`
- final output returned as standard tool result payload
- cancellation reflected as normal turn cancellation result

### 5.2 Tool Surface

Current surface:

- `SpawnSubagent`

Deferred tooling (not part of current milestone):

- `WaitSubagent`
- explicit registry/list/cancel tools

Because main agent currently awaits subagent completion synchronously, list/cancel tools do not add practical value in the current architecture.

## 6. Risk Focus

### 6.1 Approval Boundary Risk

Main agent and subagent must not diverge on approval behavior for equivalent operations.

### 6.2 External Runtime Permission Drift

Profile defaults and runtime args must not allow a mode that is looser than current channel approval policy.

### 6.3 Lifecycle Reliability

Subprocess cancellation must terminate child process trees to avoid orphan workers and hidden side effects.

## 7. Open Questions

1. Should worktree isolation be implemented as a generic workspace isolation layer before subagent-specific integration?
2. Should external runtime selection stay explicit (profile-driven) or be model-selected in selected channels?
3. Should future `acp` runtime be managed in the same profile schema or split into a dedicated provider model?

## 8. Milestone Status

- **M1**: Done
- **M2**: Done
- **M3**: Done
- **M4**: Dropped
- **M5**: Future (generic workspace isolation first)
- **M6**: Current (approval and permission propagation across subagent boundary)
