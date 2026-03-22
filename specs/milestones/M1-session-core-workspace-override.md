# M1 — Session Core: Per-Thread Workspace Override

| Field | Value |
|-------|-------|
| **Milestone** | M1 |
| **Title** | Session Core: Per-Thread Workspace Override |
| **Status** | Pending |
| **Parent Spec** | [Automations Lifecycle](../automations-lifecycle.md) §9.1 |
| **Depends On** | — (no prerequisites) |
| **Blocks** | M2, M3 |

## Overview

Currently `AgentFactory` and `ToolProviderContext` are constructed once at AppServer startup and bind all tool invocations to the process-level workspace path. Every agent thread — regardless of what `identity.workspacePath` or `thread.WorkspacePath` contains — executes file reads, writes, and shell commands against the same single root directory.

This milestone adds `ThreadConfiguration.WorkspaceOverride`, a field that causes `BuildAgentForConfigAsync` to create a workspace-scoped tool context for the thread. When set, all tools (file I/O, shell, memory, etc.) operate on the override path instead of the global one. The thread remains registered under the AppServer's main workspace for discoverability via `thread/list`.

This is the prerequisite that allows the Automations orchestrator to provision isolated per-task workspace directories and point an agent at them without modifying any global state.

## Scope

### In Scope

- Add `WorkspaceOverride` field to `ThreadConfiguration` in `DotCraft.Core`.
- Update `BuildAgentForConfigAsync` in `SessionService` to detect the field and construct a scoped `ToolProviderContext`.
- Scope `MemoryStore`, `PathBlacklist`, and all standard tool providers to the override path when set.
- Wire Protocol pass-through: `thread/start` config already forwards `ThreadConfiguration` as-is; no handler changes are needed beyond the model update.
- Unit tests covering the scoped context construction and the fallback to the global path when `WorkspaceOverride` is null.

### Out of Scope

- Tool profiles (`ToolProfile` field) — covered in M2.
- Approval policy (`ApprovalPolicy` field) — covered in M2.
- The `thread/list` `channelName` filter — covered in M2.
- Any Automations module code — covered in M3 onwards.

## Requirements

### R1.1 — ThreadConfiguration.WorkspaceOverride field

`ThreadConfiguration` in `src/DotCraft.Core/Protocol/ThreadConfiguration.cs` gains a new nullable string field:

```csharp
/// <summary>
/// When set, all tools for this thread operate on this workspace path
/// instead of the AppServer's root workspace path.
/// The thread is still registered under the AppServer's root workspace
/// for discoverability via thread/list.
/// </summary>
public string? WorkspaceOverride { get; set; }
```

The field is nullable. A null value means "use the process-level default" — existing behaviour is unchanged.

### R1.2 — BuildAgentForConfigAsync uses the override path

In `SessionService.BuildAgentForConfigAsync`, when `config.WorkspaceOverride` is non-null and non-empty:

1. Derive the `.craft` path as `Path.Combine(config.WorkspaceOverride, ".craft")`.
2. Construct a new `ToolProviderContext` with:
   - `WorkspacePath = config.WorkspaceOverride`
   - `BotPath = craftPath`
   - All other fields inherited from the process-level context (Config, ChatClient, ApprovalService, etc.)
3. Construct a new `MemoryStore(craftPath)` scoped to the override.
4. Construct a new `PathBlacklist([])` (no inherited blacklist from the main workspace).
5. Pass the scoped context to `AgentFactory.CreateToolsForMode(mode, toolProviderContext)`.

When `config.WorkspaceOverride` is null, `BuildAgentForConfigAsync` behaves identically to today.

### R1.3 — AgentFactory exposes a context-parameterised tool creation method

`AgentFactory.CreateToolsForMode` currently uses the instance-level `_toolProviderContext`. Add an overload that accepts an explicit context:

```csharp
public IReadOnlyList<IAgentToolProvider> CreateToolsForMode(
    AgentMode mode,
    ToolProviderContext? contextOverride = null);
```

When `contextOverride` is provided, it is used instead of `_toolProviderContext` for this call only. The instance-level context is not mutated.

### R1.4 — Wire Protocol model updated

`ThreadConfigurationWire` (or the JSON DTO that maps to `ThreadConfiguration`) gains the `workspaceOverride` field (camelCase, nullable string). Existing clients that omit the field receive the default null behaviour.

### R1.5 — No change to thread storage

`thread.WorkspacePath` (from `identity.workspacePath`) remains unchanged — it is used for thread ownership and discoverability. `WorkspaceOverride` affects only the tool execution context, not where the thread is stored.

## Acceptance Criteria

| # | Criterion |
|---|-----------|
| AC1 | A thread created with `config.workspaceOverride = "/tmp/task-workspace"` runs `FileWrite` against `/tmp/task-workspace`, not the AppServer's root workspace. |
| AC2 | A thread created without `workspaceOverride` (null) behaves identically to today: tools use the process-level workspace path. |
| AC3 | `thread/list` returns the thread when queried with the AppServer's root `workspacePath`, not `/tmp/task-workspace`. |
| AC4 | `MemoryStore` for a workspace-override thread is initialised from `/tmp/task-workspace/.craft`, not the main `.craft`. |
| AC5 | `AgentFactory._toolProviderContext` (process-level) is not mutated when `WorkspaceOverride` is set on a thread. |
| AC6 | Existing unit tests in `DotCraft.Core.Tests` continue to pass without modification. |
| AC7 | The `thread/start` Wire Protocol request accepts `"workspaceOverride"` in `config` and the field value reaches `BuildAgentForConfigAsync`. |

## Affected Files

| File | Change |
|------|--------|
| `src/DotCraft.Core/Protocol/ThreadConfiguration.cs` | Add `WorkspaceOverride` property |
| `src/DotCraft.Core/Protocol/SessionService.cs` | Update `BuildAgentForConfigAsync` to branch on `WorkspaceOverride` |
| `src/DotCraft.Core/Agents/AgentFactory.cs` | Add `contextOverride` overload to `CreateToolsForMode` |
| `src/DotCraft.Core/Protocol/AppServer/AppServerProtocol.cs` | Add `workspaceOverride` to `ThreadConfiguration` wire DTO (if separate from domain model) |
| `tests/DotCraft.Core.Tests/` | Add unit tests for scoped context construction |
