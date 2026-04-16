# DotCraft External CLI Subagent Design

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-15 |

Purpose: summarize current findings and a proposed design direction for letting DotCraft delegate work to external third-party CLI agents such as Claude Code, Codex CLI, and Cursor CLI.

## 1. Background

DotCraft already supports native subagents through `SpawnSubagent`, but the current implementation is an internal lightweight agent execution path:

- the subagent reuses DotCraft's own `ChatClient`
- the subagent runs with a restricted DotCraft toolset
- the subagent is intended mainly for research, exploration, and parallel investigation

This is useful, but it is not the same as delegating work to an external coding agent runtime.

The desired capability is different:

- the main DotCraft agent should be able to dispatch a task to a third-party CLI agent
- the delegated agent may own code-writing execution for that task
- DotCraft should remain the orchestrator, not necessarily the code writer

## 2. Current DotCraft Reality

### 2.1 Existing Native Subagent Path

Current native subagent behavior is implemented around:

- `src/DotCraft.Core/Tools/AgentTools.cs`
- `src/DotCraft.Core/Agents/SubAgentManager.cs`
- `src/DotCraft.Core/Agents/SubAgentProgressAggregator.cs`
- `src/DotCraft.Core/Agents/SubAgentProgressBridge.cs`

Key properties:

- `SpawnSubagent` is exposed as a normal tool to the main agent
- `SubAgentManager` creates a second DotCraft agent instance
- the subagent uses DotCraft file, shell, and web tools
- progress and token data already flow through the existing session/event pipeline

### 2.2 ACP Does Not Directly Solve This Requirement

DotCraft's current ACP implementation is primarily producer-oriented:

- DotCraft can expose itself to ACP-capable clients
- DotCraft can bridge IDE ACP requests into AppServer sessions
- DotCraft supports ACP extension proxying for file/terminal access

However, this is not yet the same as being a robust consumer/orchestrator of arbitrary third-party coding CLIs.

Important correction from this research:

- DotCraft's current ACP support should not be treated as proof that external CLI subagent support is already mostly solved
- external CLI subagent support requires a dedicated runtime/session abstraction

## 3. Design Principles

This design adopts the following principles for external CLI subagent integration:

1. treat external coding agents as runtimes/sessions, not raw shell commands
2. separate orchestration from runtime-specific process handling
3. prefer structured contracts (`--json`, explicit capabilities, predictable lifecycle)
4. isolate code-writing execution via worktree/session boundaries
5. support both one-shot and persistent execution styles

## 4. Synthesis

This leads to the following conclusion:

> The best fit for DotCraft is not "make ACP the center of everything" and not "shell out blindly to any CLI", but rather a dedicated external subagent runtime layer, with CLI runtime support as the primary path and ACP runtime support as an optional backend type.

## 5. Proposed Direction

### 5.1 Core Goal

Introduce a generalized external subagent system in DotCraft that allows the main agent to dispatch tasks to:

- native DotCraft subagents
- persistent third-party CLI agents
- one-shot third-party CLI agents
- future ACP-backed runtimes where appropriate

### 5.2 High-Level Architecture

Proposed layers:

1. `SubAgentCoordinator`
2. `ISubAgentRuntime`
3. runtime implementations
4. profile/config registry
5. existing session/progress/event integration

### 5.3 Suggested Runtime Types

At minimum:

- `native`
  - current DotCraft implementation
- `cli-persistent`
  - for long-lived subprocess-backed runtimes
- `cli-oneshot`
  - for per-request subprocess execution
- `acp`
  - optional future runtime type when an external agent already exposes a solid ACP bridge

## 6. Proposed Core Abstractions

### 6.1 `ISubAgentRuntime`

Suggested responsibilities:

- create or attach to a session
- send a task/message
- stream progress/events if supported
- cancel execution
- stop/cleanup session resources
- report capability metadata

Illustrative shape:

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

### 6.2 `SubAgentProfile`

Profiles should configure vendor/runtime-specific behavior without changing orchestrator logic.

Suggested fields:

- `name`
- `runtime`
- `bin`
- `args`
- `env`
- `workingDirectoryMode`
- `supportsStreaming`
- `supportsResume`
- `supportsModelSelection`
- `supportsWorktree`
- `inputFormat`
- `outputFormat`
- `permissionModeMapping`
- `timeout`
- `sanitizationRules`

Initial profile examples:

- `dotcraft-native`
- `claude-code`
- `codex-cli`
- `cursor-cli`
- `custom-cli`

### 6.3 `SubAgentCoordinator`

Suggested responsibilities:

- resolve the selected profile
- allocate working directory or git worktree
- launch runtime sessions
- bridge runtime events into existing DotCraft progress/session events
- maintain task state and lifecycle
- aggregate final result back to the main agent

## 7. Execution Model

### 7.1 Native Runtime

Uses the existing DotCraft subagent machinery.

Purpose:

- preserve current behavior
- remain the default path for lightweight exploration and research

### 7.2 Persistent CLI Runtime

Use when the target agent supports an interactive long-lived process.

Characteristics:

- start subprocess once
- communicate over stdin/stdout or another structured channel
- preserve runtime state across messages
- better for iterative coding tasks and long conversations

Best candidate:

- Claude Code style engines

### 7.3 One-Shot CLI Runtime

Use when the target agent is easiest to invoke per request.

Characteristics:

- start a new subprocess for each delegated request
- simpler lifecycle
- easier to integrate initially
- weaker session continuity unless emulated externally

Good candidate:

- Codex CLI style engines

### 7.4 ACP Runtime

This should be optional, not foundational.

Use when:

- a third-party runtime already exposes a stable ACP bridge
- ACP gives better structured interaction than raw CLI integration

Do not assume:

- all desired third-party agents will have mature ACP support

## 8. Coordination Features to Add

The runtime layer alone is not enough. DotCraft should also gain explicit coordination features.

Recommended additions:

- subagent session registry
- subagent task state
- worktree allocation per code-writing subagent
- inbox/message passing between main agent and subagent
- explicit wait/cancel/stop lifecycle
- plan approval checkpoints for risky delegated changes

Suggested future tool surface:

- `SpawnSubagent`
- `SendSubagentInput`
- `WaitSubagent`
- `CancelSubagent`
- `ListSubagents`

The current `SpawnSubagent` tool can be expanded rather than replaced.

## 9. Integration with Existing DotCraft Systems

### 9.1 Session/Event Pipeline

DotCraft already has:

- session events
- subagent progress aggregation
- wire/AppServer delivery
- CLI/Desktop/TUI rendering paths

External runtime support should reuse these, not bypass them.

Required adaptation:

- runtime-specific progress must be translated into DotCraft's `SubAgentProgress`
- final result must return as a normal tool result or agent message payload
- token accounting should be supported where feasible, but may be optional per runtime

### 9.2 Existing `SpawnSubagent`

Current behavior:

- only supports native DotCraft subagent execution

Proposed evolution:

- keep `SpawnSubagent`
- add optional runtime/profile selection
- default to `native`

Illustrative parameters:

- `task`
- `label`
- `profile`
- `runtime`
- `isolationMode`

## 10. Recommended MVP

The initial milestone should be intentionally narrow.

### 10.1 MVP Scope

Implement:

- runtime abstraction
- profile-based runtime selection
- native runtime adapter
- one persistent or one-shot external CLI runtime path
- final-result delegation flow
- basic progress and lifecycle handling

### 10.2 Suggested First Profiles

Recommended order:

1. `dotcraft-native`
2. `claude-code` or a generic `custom-cli-persistent`
3. `codex-cli` or a generic `custom-cli-oneshot`

`cursor-cli` should likely remain experimental until its automation surface is proven stable enough.

### 10.3 What MVP Should Not Try to Solve

Avoid in the first milestone:

- universal ACP integration
- fully generic multi-turn external agent memory semantics
- vendor-specific rich tool event mapping for every CLI
- perfect token accounting across all engines
- deep team-style multi-subagent workflows

## 11. Risks and Constraints

### 11.1 Structured IO Risk

Many third-party CLIs are not designed first for automation.

Risk:

- brittle stdout parsing
- version-specific behavior changes

Mitigation:

- prefer structured modes when available
- keep parsing adapters runtime-specific
- define fallback behavior clearly

### 11.2 Security Risk

Delegated coding agents may execute shell commands or edit files aggressively.

Mitigation:

- default to worktree isolation
- enforce explicit cwd boundaries
- preserve DotCraft approval hooks where possible
- require per-profile trust settings

### 11.3 Lifecycle Complexity

Persistent agents introduce cleanup, cancellation, and restart concerns.

Mitigation:

- make one-shot support first-class
- keep persistent runtime state explicit
- add deterministic shutdown and stale-session cleanup

### 11.4 Capability Variance

Not all CLIs expose:

- resume
- model selection
- permission mode
- structured events

Mitigation:

- capability-driven profile model
- degrade gracefully by runtime type

## 12. Open Questions

The following still need product and implementation decisions:

1. Should external code-writing subagents always run in separate worktrees by default?
2. Should external runtime selection be user-configured only, or also model-selected by the main agent?
3. How much of the delegated runtime's raw output should be exposed to the user?
4. Should DotCraft persist external runtime sessions across restarts?
5. Should approval policy be owned entirely by DotCraft, or partially delegated to runtime-specific profiles?
6. Should the external runtime API surface be exposed only through tools, or also through slash commands / config-managed workflows?

## 13. Next Steps

Recommended immediate follow-up work:

1. Define the `SubAgentProfile` config schema.
2. Design the `ISubAgentRuntime` and coordinator interfaces.
3. Decide the initial isolation model for external code-writing agents.
4. Implement `native` as a runtime adapter over the existing subagent path.
5. Implement one external runtime prototype:
   - either `cli-persistent`
   - or `cli-oneshot`
6. Extend `SpawnSubagent` to accept profile/runtime selection.
7. Bridge runtime events into existing `SubAgentProgress` notifications.
8. Validate the design on one real delegated coding workflow end to end.

## 14. Summary

The main result of this research is a design correction:

- DotCraft's current ACP implementation is not, by itself, the solution to external CLI subagents
- the correct center of gravity is a runtime/session abstraction for third-party coding CLIs
- ACP should be treated as one possible runtime backend, not the only path

This direction appears to be a strong foundation for adding external third-party CLI subagent support to DotCraft.

## 15. Resources

The following repositories were used as analysis data sources during the investigation phase:

- `Enderfga/openclaw-claude-code`: <https://github.com/Enderfga/openclaw-claude-code>
- `HKUDS/ClawTeam`: <https://github.com/HKUDS/ClawTeam>
- `HKUDS/CLI-Anything`: <https://github.com/HKUDS/CLI-Anything>
