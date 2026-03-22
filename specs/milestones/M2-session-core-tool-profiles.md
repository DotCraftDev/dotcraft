# M2 — Session Core: Tool Profiles, Approval Policy, and Thread/List Filter

| Field | Value |
|-------|-------|
| **Milestone** | M2 |
| **Title** | Session Core: Tool Profiles, Approval Policy & Thread/List Filter |
| **Status** | Pending |
| **Parent Spec** | [Automations Lifecycle](../automations-lifecycle.md) §9.2–9.4 |
| **Depends On** | M1 |
| **Blocks** | M3 |

## Overview

This milestone completes the Session Core extension work begun in M1 by adding the remaining two `ThreadConfiguration` fields — `ToolProfile` and `ApprovalPolicy` — and adding a `channelName` filter to the `thread/list` Wire Protocol method.

**Tool profiles** allow a source (e.g., the Automations module) to register a named set of tool providers, then reference that name in thread config instead of hard-coding a tool list. This decouples the orchestrator from knowing which tools each source needs.

**Approval policy** allows an individual thread to override the process-level approval behaviour. For automations, most threads use `AutoApprove`; when a human review gate is triggered, the thread is interrupted rather than prompting interactively.

**Thread/list channelName filter** allows the Desktop to query all threads belonging to a specific channel (e.g., `"automations"`), making the task board possible without the Desktop needing to track thread IDs.

## Scope

### In Scope

- `IToolProfileRegistry` interface and `ToolProfileRegistry` implementation in `DotCraft.Core`.
- `ThreadConfiguration.ToolProfile` field and integration in `BuildAgentForConfigAsync`.
- `ThreadConfiguration.ApprovalPolicy` field and integration in `BuildAgentForConfigAsync`.
- `thread/list` handler updated to accept an optional `channelName` filter.
- Wire Protocol DTO updates for the new fields.
- Unit tests for all three capabilities.

### Out of Scope

- `IAutomationSource.RegisterToolProfile` — the mechanism by which Automations sources populate the registry — covered in M3/M4.
- Any Automations module code beyond the registry contract.

## Requirements

### R2.1 — IToolProfileRegistry

A new interface `IToolProfileRegistry` is added to `DotCraft.Core.Agents`:

```csharp
/// <summary>
/// Stores named tool-provider sets that threads can reference by profile name.
/// Profiles are registered at startup or by IAutomationSource implementations.
/// </summary>
public interface IToolProfileRegistry
{
    void Register(string profileName, IReadOnlyList<IAgentToolProvider> providers);
    bool TryGet(string profileName, out IReadOnlyList<IAgentToolProvider>? providers);
}
```

A singleton `ToolProfileRegistry : IToolProfileRegistry` implementation is registered in the DI container by `DotCraft.App`'s host builder.

### R2.2 — ThreadConfiguration.ToolProfile field

```csharp
/// <summary>
/// When set, the agent uses the tool set registered under this profile name
/// instead of the default tools for the thread's AgentMode.
/// Requires the profile to be registered in IToolProfileRegistry.
/// </summary>
public string? ToolProfile { get; set; }
```

In `BuildAgentForConfigAsync`:
- If `config.ToolProfile` is non-null, resolve the profile from `IToolProfileRegistry`.
- If the profile name is unknown, throw `InvalidOperationException("Tool profile '{name}' is not registered.")`.
- Pass the resolved providers to `AgentFactory` instead of calling `CreateToolsForMode`.
- If `config.ToolProfile` is null, `CreateToolsForMode` is called as today.

### R2.3 — ThreadConfiguration.ApprovalPolicy field

```csharp
public enum ApprovalPolicy
{
    /// <summary>Default process-level behaviour (typically interactive prompt).</summary>
    Default,
    /// <summary>All tool calls are auto-approved; no user prompt is shown.</summary>
    AutoApprove,
    /// <summary>
    /// Tool calls that require approval interrupt the thread (publish an
    /// Interrupted SessionEvent) instead of prompting the user.
    /// </summary>
    Interrupt,
}
```

```csharp
/// <summary>
/// Overrides the process-level approval service for this thread only.
/// </summary>
public ApprovalPolicy ApprovalPolicy { get; set; } = ApprovalPolicy.Default;
```

In `BuildAgentForConfigAsync`:
- `ApprovalPolicy.Default` — use the injected `IApprovalService` (unchanged behaviour).
- `ApprovalPolicy.AutoApprove` — replace the approval service with `AutoApproveApprovalService`.
- `ApprovalPolicy.Interrupt` — replace with a new `InterruptOnApprovalService` that publishes `SessionEvent.Interrupted` and returns `ApprovalResult.Denied`.

### R2.4 — InterruptOnApprovalService

New class `InterruptOnApprovalService : IApprovalService` in `DotCraft.Core.Protocol`:

```csharp
/// <summary>
/// Approval service that interrupts the thread instead of prompting the user.
/// When approval is required, this service publishes a SessionEvent.Interrupted
/// event on the thread's event channel and returns ApprovalResult.Denied,
/// halting the current turn.
/// </summary>
```

The service receives the `ISessionEventChannel` for the thread so it can publish the `Interrupted` event before returning.

### R2.5 — thread/list channelName filter

The `thread/list` Wire Protocol request accepts an optional `channelName` parameter:

```json
{
  "method": "thread/list",
  "params": {
    "workspacePath": "/workspace",
    "channelName": "automations"
  }
}
```

When `channelName` is provided, the handler filters the in-memory thread registry to return only threads whose `identity.channelName` matches the value (case-insensitive). When `channelName` is omitted, all threads for the workspace are returned as today.

`AppServerRequestHandler.HandleThreadList` is updated to apply this filter.

### R2.6 — Wire Protocol DTO updates

`ThreadConfigurationWire` (or equivalent JSON DTO) gains:

| Field | Type | Default |
|-------|------|---------|
| `toolProfile` | `string?` | `null` |
| `approvalPolicy` | `"default" \| "autoApprove" \| "interrupt"` | `"default"` |

`ThreadListRequestWire` gains `channelName?: string`.

## Acceptance Criteria

| # | Criterion |
|---|-----------|
| AC1 | Registering a profile `"local-task"` and starting a thread with `config.toolProfile = "local-task"` causes the agent to be built with those providers, not the default mode tools. |
| AC2 | Starting a thread with `config.toolProfile = "unknown"` throws `InvalidOperationException` before the thread starts. |
| AC3 | A thread with `config.approvalPolicy = "autoApprove"` auto-approves all tool calls without displaying a prompt. |
| AC4 | A thread with `config.approvalPolicy = "interrupt"` publishes a `SessionEvent.Interrupted` event and halts the turn when an approval is required, instead of prompting. |
| AC5 | `thread/list` with `channelName = "automations"` returns only threads where `identity.channelName == "automations"`. |
| AC6 | `thread/list` without `channelName` returns all threads for the workspace (unchanged from today). |
| AC7 | A null `toolProfile` and `approvalPolicy = "default"` produce identical behaviour to a thread created today without these fields. |
| AC8 | `IToolProfileRegistry` is resolvable from the DI container in `DotCraft.App`. |

## Affected Files

| File | Change |
|------|--------|
| `src/DotCraft.Core/Protocol/ThreadConfiguration.cs` | Add `ToolProfile` and `ApprovalPolicy` properties |
| `src/DotCraft.Core/Protocol/SessionService.cs` | Update `BuildAgentForConfigAsync` to branch on new fields |
| `src/DotCraft.Core/Agents/IToolProfileRegistry.cs` | New file: interface |
| `src/DotCraft.Core/Agents/ToolProfileRegistry.cs` | New file: singleton implementation |
| `src/DotCraft.Core/Protocol/InterruptOnApprovalService.cs` | New file |
| `src/DotCraft.Core/Protocol/AppServer/AppServerRequestHandler.cs` | Add `channelName` filter to `HandleThreadList` |
| `src/DotCraft.Core/Protocol/AppServer/AppServerProtocol.cs` | Add `toolProfile`, `approvalPolicy` to wire DTO; add `channelName` to `ThreadListRequest` |
| `src/DotCraft.App/Hosting/HostBuilder.cs` | Register `IToolProfileRegistry` singleton |
| `tests/DotCraft.Core.Tests/` | Add unit tests |
