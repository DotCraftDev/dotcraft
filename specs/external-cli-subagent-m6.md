# DotCraft External CLI Subagent — M6: Approval and Permission Propagation

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Active |
| **Date** | 2026-04-20 |
| **Parent Spec** | [External CLI Subagent Design](external-cli-subagent-design.md), [M5: Git Worktree Isolation](external-cli-subagent-m5.md), [Session Core](session-core.md) |

Purpose: ensure DotCraft approval and permission policy is correctly propagated across subagent boundaries, instead of only checking subagent launch.

---

## 1. Scope

### 1.1 In Scope

- Native subagent approval propagation (`IApprovalService` + `ApprovalContext`).
- External CLI permission mode mapping based on active DotCraft approval service type.
- Reliable cancellation propagation from parent turn cancellation to delegated subprocess tree.
- Documentation and profile defaults aligned with above behavior.

### 1.2 Out of Scope

- Launch approval gate for starting subagent runtime.
- `ListSubagents` and `CancelSubagent` tool surface.
- `SendSubagentInput`-driven persistent runtime interaction.
- Output sanitization and redact rules (deferred to a later milestone).

---

## 2. Problem Statement

M1–M3 delivered external subagent runtime capability, but policy behavior is inconsistent:

1. **Native policy gap**: native subagents can call tools without preserving the same approval semantics used by main agent flows.
2. **External permission drift**: external CLI launch args are not tied to active approval service mode in a strict and explicit way.
3. **Cancellation reliability risk**: cancellation may stop parent turn while leaving child/descendant processes alive.

The practical value of M6 is to fix these three runtime-level gaps. Launch approvals and subagent coordination tools are not sufficient to solve them.

---

## 3. Goals and Non-Goals

### 3.1 Goals

1. Equivalent operations should trigger equivalent approval behavior whether executed by main agent or native subagent.
2. External runtime permission flags should be derived from `IApprovalService` mode (`interactive`, `auto-approve`, `restricted`).
3. Parent turn cancellation should terminate external CLI subprocess trees deterministically.
4. Approval requests from native subagent context should be distinguishable to users using a lightweight prefix strategy.

### 3.2 Non-Goals

- Adding a dedicated UI or schema extension for subagent-specific approval metadata.
- Providing concurrent subagent orchestration primitives before architecture supports non-blocking main-agent waits.
- Replacing worktree isolation design in M5.

---

## 4. UX Scenarios

### 4.1 Scenario A — Native Subagent Triggers Real Approval

When native subagent performs sensitive tool operation, approval request appears exactly like existing approval flow but with source prefix.

Example request text:

- `path`: `[subagent:env-scan] E:\Git\other-repo\.env`
- `command`: `[subagent:fix-compile] dotnet test`

Expected behavior:

- approve -> subagent continues.
- deny -> subagent receives explicit permission failure and returns summary.

### 4.2 Scenario B — External CLI Follows Channel Policy

For `codex-cli`, DotCraft maps approval mode to launch flags:

- `interactive` -> `--sandbox read-only --ask-for-approval on-request`
- `auto-approve` -> `--dangerously-bypass-approvals-and-sandbox`
- `restricted` -> `--sandbox read-only`

Equivalent mappings should be provided for `cursor-cli` and documented for `custom-cli-oneshot`.

### 4.3 Scenario C — Ctrl+C Cancels Full Process Tree

When user cancels turn:

1. parent `CancellationToken` triggers.
2. runtime `CancelAsync` executes kill-tree sequence.
3. no orphan external CLI process remains.
4. final tool result reports cancellation.

---

## 5. Design

### 5.1 Native Approval Propagation

- `SubAgentManager` must receive `IApprovalService` and `ApprovalContext`.
- tool providers used by native subagent (`FileTools`, `ShellTools`, sandbox variants) must use same approval service chain as parent session.
- introduce `PrefixedApprovalService` decorator:
  - wraps `IApprovalService`
  - prepends `[subagent:<label>] ` to `path` and `command` fields
  - does not change approval protocol shape

### 5.2 Approval Mode Resolver

Add `SubAgentApprovalModeResolver.Resolve(IApprovalService?) -> string`:

- `SessionApprovalService`/`ConsoleApprovalService` => `interactive`
- `AutoApproveApprovalService` => `auto-approve`
- `InterruptOnApprovalService` => `restricted`
- unknown or null => `restricted` (safe default)

### 5.3 Permission Mapping Injection

- coordinator resolves current mode from approval service.
- reads `profile.PermissionModeMapping[mode]`.
- appends mapped args before task payload args during subprocess launch.
- no mapping entry means no extra args.

### 5.4 Built-in Profile Defaults

Built-in profiles must include stable mapping defaults:

- `codex-cli`: as defined in UX scenario.
- `cursor-cli`: conservative defaults for `interactive`/`restricted`, permissive default for `auto-approve` where CLI supports it.
- `custom-cli-oneshot`: empty mapping by default, user-owned.
- `dotcraft-native`: mapping not applicable.

### 5.5 Cancellation Semantics

`CliOneshotRuntime.CancelAsync` must terminate subtree:

- Windows: JobObject binding + `TerminateJobObject`.
- Unix: process group signal (`SIGTERM`, then `SIGKILL` fallback).

Runtime must guarantee best-effort cleanup even on timeout/exception paths.

---

## 6. Implementation Notes

Expected touchpoints:

- `src/DotCraft.Core/Agents/SubAgentRuntime.cs`
- `src/DotCraft.Core/Agents/SubAgentManager.cs`
- `src/DotCraft.Core/Agents/CliOneshotRuntime.cs`
- `src/DotCraft.Core/Configuration/SubAgentProfileConfig.cs`
- `src/DotCraft.Core/Tools/AgentTools.cs`
- `src/DotCraft.Core/Security/` (new approval decorator)

Model extension:

- `SubAgentLaunchContext` carries `ApprovalContext` and mapped launch args.

---

## 7. Acceptance Checklist

- [ ] Native subagent tool calls use a non-null approval service when parent session has one.
- [ ] Approval requests emitted from native subagent include `[subagent:<label>]` prefix in command/path payload.
- [ ] `ApprovalContext` is preserved across `AgentTools.SpawnSubagent -> SubAgentCoordinator -> SubAgentManager`.
- [ ] Approval mode resolver returns expected mode for interactive/auto/restricted services.
- [ ] `permissionModeMapping` args are appended for external cli launch according to resolved mode.
- [ ] Built-in profile defaults include practical mapping entries for `codex-cli` and `cursor-cli`.
- [ ] Cancelling a running cli-oneshot subagent terminates child process tree (not only root pid).
- [ ] `SpawnSubagent` result and event stream behave consistently for cancellation outcomes.
- [ ] Removed features (`launch approval gate`, list/cancel tools) are not referenced as current M6 deliverables.

---

## 8. Risks

1. External CLI flags may change between versions; mappings should remain profile-driven and easily overrideable.
2. Cross-platform kill-tree implementation can be brittle; tests should verify both direct and grandchild process termination behavior.
3. Prefix-based source marking is string-level and depends on consistent formatting in approval prompts.

---

## 9. Open Questions

1. Should we add profile-level opt-out for prefix decoration in channels that already provide rich source context?
2. For `ChannelRoutingApprovalService`, should resolver inspect inner service chain or treat it as `auto-approve` only when all routes are auto?
3. Should `restricted` mode fail launch directly for profiles without a conservative mapping entry?
