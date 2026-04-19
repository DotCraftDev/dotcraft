# Module Development Specification

| Field | Value |
|-------|-------|
| **Version** | 0.0.1.0 |
| **Status** | Release |
| **Last modified** | 2025-03-15 |

Purpose: Define normative rules for adding and implementing DotCraft modules. Architecture and file layout are out of scope; implementors discover them from the repository.

## 1. Problem Statement

DotCraft uses a modular architecture where each interaction mode (CLI, API, QQ, WeCom, AG-UI, GitHub Tracker, etc.) is a module. To keep the system consistent as new modules are added and the codebase evolves, the following problems must be addressed:

- **Host vs Channel**: A process runs at most one *Host* (entry point). Some modules provide a Host; others provide only a *Channel* (an entry point that runs only when the Gateway Host is active). A new module must be explicit about whether it is a Host, a Channel, or neither (e.g. tool-only).

- **Human-in-the-loop (HITL)**: Channels and Hosts that run the agent with sensitive tools (file, shell, etc.) must support approval—either a full approval flow or documented use of auto-approve where acceptable.

- **Optional tools and config**: A module may contribute zero or more tool providers; tools are gated by module enablement. Module configuration uses a single root config at runtime; each module defines and reads its own section(s) in its assembly (no need to add module sections to Core). New config must follow the existing placement and validation rules.

This specification defines the contracts and rules so that new modules behave consistently and the spec remains valid as the product evolves.

## 2. Goals and Non-Goals

### 2.1 Goals

- Enforce at most one Host per process, selected by the primary (highest-priority enabled) module that provides a Host.
- Define Channel lifecycle: Channels run only when the Gateway Host is active; Gateway collects Channel services from all enabled non-gateway modules that implement a Channel.
- Require HITL (approval) for any Channel or Host that runs the agent with sensitive tools, unless use of AutoApprove is documented and acceptable.
- Treat tool providers as optional; tools are included only from enabled modules.
- Define configuration placement: a single root config at runtime; each module defines its section type(s) in its own assembly and reads via `AppConfig.GetSection<T>(key)`. Required fields or constraints are validated via the module's `ValidateConfig`.

### 2.2 Non-Goals

- Prescribing folder layout, namespaces, or "where to add code" (implementors discover these from the repository).
- Step-by-step tutorials or file-by-file guides (the repository is the reference).
- Code style, naming conventions, or documentation format (those belong in the dev-guide skill, not this spec).

## 3. Module Types and Contracts

### 3.1 Host

- **Definition**: A Host is the single process entry point. At most one Host runs per process.
- **Selection**: The primary module is the highest-priority enabled module. If that module has an associated Host factory, that Host is created and run; otherwise startup fails (no Host).
- **Contract**: A module that provides a Host implements (or is associated with) an `IHostFactory` that creates an `IDotCraftHost`. The Host owns the process lifecycle and may run one or more Channels (only Gateway does the latter).
- **Rule**: A new module that is a standalone entry point must provide an `IHostFactory` and understand that it will run only when it is the primary module.

### 3.2 Channel

- **Definition**: A Channel is an interaction entry (API, chat bot, AG-UI server, tracker orchestrator, etc.) that is managed by the Gateway when Gateway is the active Host.
- **Lifecycle**: Channels run only when the Gateway module is the primary module. Gateway collects `IChannelService` from every enabled non-gateway module whose `CreateChannelService` returns a non-null instance.
- **Contract**: A module that provides a Channel implements `CreateChannelService` to return a non-null `IChannelService`. The method signature returns `IChannelService?`; a null return means the module does not provide a Channel. The Channel implements the interface required by Gateway (name, start/stop, optional heartbeat/cron, approval service, optional channel client, message delivery).
- **Rule**: A new module that is an entry point intended to run concurrently with others (e.g. API + QQ) must implement a Channel and not a Host; only Gateway provides the Host that runs multiple Channels.

### 3.3 Tool-Only Module

- **Definition**: A module that contributes only tool providers: no Host, no Channel. It is enabled when its configuration is enabled; its tools are then included in the tool set for whichever Host/Channel runs the agent (e.g. Unity tools when ACP is active).
- **Contract**: The module returns one or more tool providers from `GetToolProviders()` and may return an empty list. Tools are collected only from enabled modules.
- **Rule**: A new module that only adds capabilities (tools) to an existing Host/Channel must not implement Host or Channel; it only implements `GetToolProviders` and configuration.

## 4. Human-in-the-Loop (Approval)

### 4.1 When HITL Is Required

- Any Channel or Host that runs the agent with sensitive tools (e.g. file access, shell execution) must support Human-in-the-Loop approval for those operations, unless the spec or module documentation explicitly allows AutoApprove for that context.

### 4.2 Requirements

- **Full approval**: The Channel or Host exposes an `IApprovalService` used by the agent runner. Approval requests are delivered through the channel (e.g. chat message, API endpoint, IDE UI), and the user's decision is returned to the runner. This satisfies HITL.
- **AutoApprove**: Using a no-op or auto-approve implementation is acceptable only when documented (e.g. "AG-UI delegates approval to the frontend"; "GitHubTracker runner runs unattended with auto-approve"). The module or its documentation must state when and why AutoApprove is used.

### 4.3 Gateway Behavior

- When Gateway is the Host, it builds a routing approval service that delegates to each Channel's `IChannelService.ApprovalService` based on approval context (e.g. source channel). Channels that do not run sensitive tools, or that document AutoApprove, may expose a null or auto-approve implementation.

## 5. Tool Providers

### 5.1 Optional

- Tool providers are optional. A module may return an empty enumerable from `GetToolProviders()`. There is no requirement to provide tools.

### 5.2 Enablement Gating

- Tools are collected only from modules that are enabled (according to application configuration). A module's tools are therefore gated by that module's enablement; disabling the module excludes its tools.

### 5.3 Scope

- The specification does not define where or how tool providers are registered in code; implementors discover that from the repository. The normative rule is: tools are optional and gated by module enablement.

## 6. Configuration

### 6.1 Placement and modular shape

- At runtime there is a **single** configuration root (one config file, one `AppConfig` instance). Core owns the root type and a fixed set of sections (e.g. Tools, Security, Heartbeat) as properties.
- **Module config is modular**: section data for modules is not added as properties on Core's `AppConfig`. Unknown top-level keys in the config file are stored in the root's extension data. Each module defines its own section type(s) in **its own assembly**, annotated with `[ConfigSection("SectionKey")]` (and optionally `DisplayName`, `Order`). The module reads its section via `AppConfig.GetSection<T>("SectionKey")`; the value is deserialized from extension data on first access and cached. Core does not reference module config types or add properties for them.
- **Adding config for a new module**: In the module project, define a type (or types) with `[ConfigSection("SectionKey")]` and use `config.GetSection<YourType>("SectionKey")` where the config is needed. No change to Core's `AppConfig` is required. For Dashboard schema and validation discovery, ensure the module assembly is referenced by the host app so that the source generator can include the section type in the config schema.

### 6.2 Validation

- A module that has required configuration fields or constraints must implement `ValidateConfig` and return a list of validation errors (empty if valid). Startup or Gateway enablement validation uses these errors to fail fast when configuration is invalid.

### 6.3 Independence

- A module's configuration should be independent of other modules in the sense that its section can be understood and validated without coupling to another module's section. Cross-module constraints (e.g. "at least one channel must be enabled when Gateway is enabled") may be expressed in the Gateway module's validator.

### 6.4 ReloadBehavior annotations

- The safe default for module fields is `ProcessRestart`. If you do not annotate reload behavior, schema generation treats the field as process-restart.
- Only mark a field `Hot` when the owning subsystem truly applies mutations at runtime without restart.
- Use section-level defaults for consistent behavior across many fields:
  - `[ConfigSection("SectionKey", DefaultReload = ReloadBehavior.ProcessRestart, HasDefaultReload = true)]`
- Override per field only where necessary:
  - `[ConfigField(Reload = ReloadBehavior.Hot, HasReload = true)]`
- `SubsystemRestart` requires a non-empty subsystem key:
  - `[ConfigField(Reload = ReloadBehavior.SubsystemRestart, HasReload = true, SubsystemKey = "proxy")]`
  - Missing `SubsystemKey` is invalid and should fail schema validation.

#### 6.4.1 Example A: Section defaulting to ProcessRestart

```csharp
[ConfigSection("ExampleChannel", DefaultReload = ReloadBehavior.ProcessRestart, HasDefaultReload = true)]
public sealed class ExampleChannelConfig
{
    public bool Enabled { get; set; } = true;
    public string Token { get; set; } = string.Empty;
}
```

This pattern is equivalent to leaving fields unannotated and is recommended when all changes require process restart.

#### 6.4.2 Example B: One Hot field, rest ProcessRestart

```csharp
[ConfigSection("ExampleSkills", DefaultReload = ReloadBehavior.ProcessRestart, HasDefaultReload = true)]
public sealed class ExampleSkillsConfig
{
    [ConfigField(Reload = ReloadBehavior.Hot, HasReload = true)]
    public string[] DisabledSkills { get; set; } = [];

    public bool VerboseLogging { get; set; }
}
```

This mirrors the `Skills.DisabledSkills` pattern in the settings reload series: one field is hot-reloadable while other fields stay process-restart by default.

## 7. Checklist (Normative)

When adding a new module, verify:

1. **Host vs Channel**
   - The module is explicitly either: (a) a Host (standalone entry; provides `IHostFactory`), (b) a Channel (entry under Gateway; implements `CreateChannelService` returning non-null), or (c) tool-only (no Host, no Channel; only `GetToolProviders`).
   - At most one Host runs per process; only Gateway Host runs multiple Channels.

2. **HITL**
   - If the module is a Channel or Host that runs the agent with sensitive tools: either it provides or routes a full `IApprovalService` flow, or it documents the use of AutoApprove and when that is acceptable.

3. **ToolProvider and Config**
   - If the module provides tools, they are optional from the spec's perspective (returning empty is valid). Tool inclusion is gated by module enablement.
   - New module configuration is defined in the module assembly (type(s) with `[ConfigSection("SectionKey")]`) and read via `AppConfig.GetSection<T>(key)`; no change to Core's `AppConfig` is required. Required fields or constraints are validated via `ValidateConfig`.

## 8. Maintenance

- When adding a new module (e.g. a new channel or host), update this spec if the new module introduces a new pattern or exception. Keep the checklist in Section 7 in sync with the norms above.
