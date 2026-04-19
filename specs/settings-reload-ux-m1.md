# DotCraft Settings Reload UX — M1: Field Behavior Contract

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-19 |
| **Parent Spec** | [Settings Reload UX Design](settings-reload-ux-design.md) |

Purpose: define the `ReloadBehavior` metadata that each configuration field carries, the way it is surfaced through the existing schema pipeline, and the Desktop-side rendering contract that consumes it. M1 establishes the shared vocabulary before any consumer or UI is changed.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. The `ReloadBehavior` Contract](#3-the-reloadbehavior-contract)
- [4. Schema Surfacing](#4-schema-surfacing)
- [5. Field Annotations for This Series](#5-field-annotations-for-this-series)
- [6. Desktop Rendering Contract](#6-desktop-rendering-contract)
- [7. Proxy-Aware UI Override](#7-proxy-aware-ui-override)
- [8. Constraints and Compatibility Notes](#8-constraints-and-compatibility-notes)
- [9. Acceptance Checklist](#9-acceptance-checklist)
- [10. Open Questions](#10-open-questions)

---

## 1. Scope

### 1.1 What This Spec Defines

- The `ReloadBehavior` classification expressed as metadata on configuration fields (and, where applicable, on entire sections).
- How that metadata flows through the existing config schema pipeline so that Desktop and any other client can consume it.
- The annotations applied to the fields in scope for this series.
- A Desktop-side rendering contract that derives editing affordances (live-apply vs. restart flow) from the schema.
- The Proxy-aware override rule that invalidates the nominal classification for `ApiKey` and `EndPoint` when Desktop's managed proxy is active.

### 1.2 What This Spec Does Not Define

- Any subsystem behavior change. Consumers are not refactored to read configuration differently in M1.
- The `workspace/configChanged` notification or the `IAppConfigMonitor` abstraction. Those belong to M2.
- The concrete Desktop UI components, layout, or copy. Those belong to M3.
- How Dashboard consumes `ReloadBehavior`. Dashboard continues to use its existing form renderer.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. **Establish a single source of truth.** Every in-scope field has a declared `ReloadBehavior`. Desktop derives its UX decisions from that declaration instead of hard-coding behavior per tab.
2. **Make the default conservative.** Fields without an explicit annotation are treated as `ProcessRestart` by consumers. This avoids misleading users when a module author forgets to annotate.
3. **Preserve existing behavior.** No configuration value, parsing path, or RPC method changes in M1. Consumers that currently capture their configuration at construction continue to do so.
4. **Keep the surface small and additive.** Annotations are optional on existing fields; unannotated code compiles and runs as before.

### 2.2 Non-Goals

- Turning any Tier C field into Tier A. That requires subsystem work beyond the scope of this series.
- Adding a runtime API for subsystems to react to metadata (for example, "auto-reload anything tagged `Hot`"). M2 introduces the notification but not reflection-driven reloading.
- Generating UI labels or translations from the metadata. Copy is defined in M3.

---

## 3. The `ReloadBehavior` Contract

### 3.1 Enumeration

`ReloadBehavior` is a closed enumeration with three values:

| Value | Meaning |
|-------|---------|
| `Hot` | The field is honored at runtime without any restart. A subsystem either re-reads it on each use or subscribes to change events. |
| `SubsystemRestart` | The field takes effect after a user-triggered restart of a named subsystem. The subsystem key identifies which restart action applies. |
| `ProcessRestart` | The field takes effect only after the AppServer (or, for Desktop-local fields, Electron main) process restarts. |

`SubsystemRestart` carries a **subsystem key** — a short string identifier such as `"proxy"` or `"lsp"`. The key is referenced by Desktop to map the field to the appropriate restart control. Subsystem keys are intentionally free-form strings so that new subsystems can introduce their own without coordinating a central enum.

### 3.2 Defaults

- A field without an explicit `ReloadBehavior` annotation is treated as `ProcessRestart`.
- A section (a type annotated with `[ConfigSection]`) may declare a section-wide default. Individual fields within the section may override it. If no default is declared and the field lacks its own annotation, `ProcessRestart` applies.

### 3.3 Attribute Surface

The classification is expressed by extending the existing configuration metadata attributes. The exact attribute names and shapes are implementation details deferred to the implementation plan; the contract is:

- A field-level annotation can set `ReloadBehavior` and, when the value is `SubsystemRestart`, a subsystem key.
- A section-level annotation can set a default `ReloadBehavior` for fields inside the section.
- The annotation is additive: omitting it preserves current behavior.

### 3.4 Semantics of Overrides and Ambiguity

When a section declares a default and a field declares its own value, the field wins. When two sections with different defaults are merged (not possible today but plausible if hub-architecture is extended), the consumer must pick one deterministically; M1 does not take a position beyond requiring that the choice be documented by whichever subsystem introduces the hybrid.

---

## 4. Schema Surfacing

### 4.1 Output Channel

The existing `ConfigSchemaBuilder` produces the JSON schema consumed by Dashboard and (starting in M3) Desktop. `ReloadBehavior` is surfaced through that same pipeline. The exact JSON shape is an implementation detail; the contract is that every field descriptor carries the effective `ReloadBehavior` (after applying section-level defaults) and, when applicable, the subsystem key.

### 4.2 Backward Compatibility

- Existing clients that do not understand `ReloadBehavior` must continue to receive a schema they can parse. New fields in the schema are additive.
- The schema produced in M1 is identical to the current schema for any field that has no annotation. Dashboard, which does not yet consume the new metadata, is unaffected.

### 4.3 Discovery by Desktop

Desktop obtains the schema through its normal AppServer handshake. In M1 the schema data flows but is not yet consumed — M3 is where Desktop starts reading `ReloadBehavior` to drive UX decisions. The schema must therefore include every in-scope field by the end of M1 so that M3 can rely on it without a further backend change.

---

## 5. Field Annotations for This Series

M1 annotates the following fields. Everything else remains unannotated and therefore `ProcessRestart`.

### 5.1 Tier A — `Hot`

- `Skills.DisabledSkills` (the collection itself, because entries are toggled live).
- `McpServers` (toggled via `mcp/upsert`; the field is annotated so Desktop can derive the hot behavior from the schema even though MCP has its own RPC surface).

### 5.2 Tier B — `SubsystemRestart`

Fields inside the Desktop managed proxy configuration (which currently live in Electron userData, not in `AppConfig`) are annotated with subsystem key `"proxy"` where they are surfaced. The annotation mechanism on the Desktop side mirrors the backend attribute but does not require a round-trip through `AppConfig`.

### 5.3 Tier C — `ProcessRestart`

The following `AppConfig` fields are explicitly annotated `ProcessRestart` so that the contract is recorded (even though it matches the default):

- `ApiKey`
- `EndPoint`
- `Model`

Annotating them explicitly makes the future upgrade to `Hot` a metadata-only diff in the consumer subsystem, and prevents a future contributor from assuming they are unannotated by omission.

### 5.4 Tier D — Desktop-Local

Desktop-local fields (theme, locale, visible channels) are not part of the AppConfig schema and are not annotated in M1. M3 specifies how Desktop renders them.

---

## 6. Desktop Rendering Contract

### 6.1 Derived Affordance Matrix

Given a field's effective `ReloadBehavior`, Desktop must derive its editing affordance according to the following matrix.

| ReloadBehavior | Default UX |
|----------------|------------|
| `Hot` | Live edit. Persist on change (debounced). Small "Saved" indicator on success. No restart prompt. |
| `SubsystemRestart(key)` | Edit with the existing subsystem restart control visible. Indicate that a restart is needed by either a per-field hint or a group-level banner tied to the subsystem. |
| `ProcessRestart` | Edit is staged locally. An explicit "Apply & Restart" action (either per-group or a global pending-changes banner) is required to persist and restart. Autosave is forbidden. |

### 6.2 Discovery Path

Desktop reads the schema on startup and on reconnect. It keeps the `ReloadBehavior` for each field available to any component that renders that field. Components do not hard-code affordance logic; they consult the contract.

### 6.3 Unknown Values

If Desktop receives a `ReloadBehavior` value it does not recognize (forward compatibility), it treats the field as `ProcessRestart`.

---

## 7. Proxy-Aware UI Override

### 7.1 Trigger

The override applies to `ApiKey` and `EndPoint` only, and it is driven by Electron main's runtime proxy status. The proxy being "active" means Electron main has started the managed proxy process for the current workspace, not merely that `settings.proxy.enabled` is true in Desktop settings.

### 7.2 Effective Behavior

When the proxy is active, Desktop must:

- Render `ApiKey` and `EndPoint` as read-only. The displayed value is the current proxy value so that the user can see what is actually reaching AppServer.
- Display a group-level indicator explaining that these fields are managed by the local proxy.
- Provide an inline link or button that navigates to the Proxy tab.
- Disable any save or apply action for these two fields. If the user has staged local edits before the proxy became active, those edits are discarded with a non-blocking notice.

When the proxy is inactive, the fields revert to their normal Tier C UX.

### 7.3 Key Display Policy

The proxy-generated `ApiKey` displayed in the locked state is masked using the same secret rendering used elsewhere in Desktop (`SecretInput`-style). Administrators who need to see the raw proxy key continue to use the Proxy tab, which is the existing source for that value.

### 7.4 Rationale

This override is not a generic feature; it specifically prevents the user's edits from being overwritten by `applyWorkspaceProxyOverrides` or silently treated as the "original" value by `cleanupWorkspaceProxyOverrides`. The override lives in Desktop because the behavior originates in Desktop; AppServer has no knowledge of the managed proxy.

### 7.5 Future Generalization

If other subsystems ever introduce a similar "Desktop is temporarily owning this field" concept, the rendering contract should be generalized to a named lock mechanism. M1 deliberately keeps the concept narrow to avoid pre-emptive design.

---

## 8. Constraints and Compatibility Notes

- No subsystem behavior changes in M1. Consumers that currently capture their configuration at construction continue to do so. The fact that `Skills.DisabledSkills` is annotated `Hot` does not mean any additional live reload is introduced in M1 — the Skills subsystem is already closed-loop via `skills/setEnabled`.
- No RPC method is added, removed, or renamed in M1.
- The schema emitted by `ConfigSchemaBuilder` is additive. Dashboard continues to work without modification.
- Modules that define their own `[ConfigSection]` types are encouraged but not required to annotate their fields in M1. Unannotated module fields are treated as `ProcessRestart`, which matches their current de facto behavior.
- The Proxy-aware override is not driven by the schema. It is a Desktop-specific rule tied to Electron main's proxy state.

---

## 9. Acceptance Checklist

- [ ] A `ReloadBehavior` enumeration with values `Hot`, `SubsystemRestart`, `ProcessRestart` exists in the core configuration module.
- [ ] Field and section annotations can carry a `ReloadBehavior` (with a subsystem key for `SubsystemRestart`), without breaking compilation of unannotated types.
- [ ] `ConfigSchemaBuilder` surfaces the effective `ReloadBehavior` for every field in the emitted schema, applying section-level defaults where appropriate.
- [ ] The fields listed in §5.1 and §5.3 carry their declared annotations and appear with the expected values in the schema.
- [ ] Fields with no annotation continue to produce identical schema output to the pre-M1 baseline except for the new optional `ReloadBehavior` element.
- [ ] Desktop exposes a helper (component or hook, exact shape up to the implementation plan) that maps a schema field descriptor to an affordance matching §6.1.
- [ ] Desktop's helper honors the Proxy-aware override for `ApiKey` and `EndPoint` per §7, driven by Electron main's runtime proxy status.
- [ ] Existing tests continue to pass. A new test verifies schema output for one annotated and one unannotated field.

---

## 10. Open Questions

1. Should the section-level default cascade through nested sections (e.g., from `Tools` into `Tools.File`, `Tools.Shell`, etc.), or should each nested section declare its own default? (Preference: each nested section declares its own; cascading is easy to add later if needed.)
2. Should Dashboard also start surfacing "needs restart" badges based on the new schema, or is that strictly out of scope for this series? (Preference: strictly out of scope; Dashboard continues with its existing form model.)
3. Is there a need for a fourth tier between `SubsystemRestart` and `ProcessRestart` — e.g., "needs reconnect" for fields affecting only the AppServer's transport? (Preference: no; `ProcessRestart` covers those cases because a transport change effectively forces a restart from the client's perspective.)
4. Should modules be able to upgrade their fields at runtime (for example, a plugin declaring itself hot-reloadable only after it has registered its change listener)? (Preference: no; static metadata only.)
