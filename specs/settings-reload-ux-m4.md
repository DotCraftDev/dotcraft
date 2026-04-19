# DotCraft Settings Reload UX — M4: Documentation and Tests

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-19 |
| **Parent Spec** | [Settings Reload UX Design](settings-reload-ux-design.md), [AppServer Protocol](appserver-protocol.md), [Desktop Client](desktop-client.md) |

Purpose: close the series by updating protocol and user documentation to reflect the three-tier model and the new change notification, and by adding the conformance and behavioral tests that prevent future regressions. M4 does not add new behavior beyond what M2 and M3 specify; it certifies what they shipped.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. AppServer Protocol Spec Updates](#3-appserver-protocol-spec-updates)
- [4. Desktop Client Spec Updates](#4-desktop-client-spec-updates)
- [5. Bilingual User Documentation](#5-bilingual-user-documentation)
- [6. Module Author Guidance](#6-module-author-guidance)
- [7. Test Coverage](#7-test-coverage)
- [8. Release Notes and Migration](#8-release-notes-and-migration)
- [9. Constraints and Compatibility Notes](#9-constraints-and-compatibility-notes)
- [10. Acceptance Checklist](#10-acceptance-checklist)
- [11. Open Questions](#11-open-questions)

---

## 1. Scope

### 1.1 What This Spec Defines

- Which existing specs gain new sections, and what content those sections must cover.
- The bilingual user-facing documentation added or updated under `docs/`.
- The guidance added for module authors so that future `[ConfigSection]` contributions correctly declare `ReloadBehavior`.
- The test coverage that certifies the M2 and M3 behavior.
- The release-notes and migration guidance for users upgrading from a pre-series Desktop.

### 1.2 What This Spec Does Not Define

- New runtime behavior. M4 is documentation and tests over M2/M3.
- Visual design or UI copy beyond what the documentation needs to convey.
- Changes to Dashboard documentation beyond a noted limitation about missing notifications.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. Make the three-tier model discoverable from the user documentation index, in both Chinese and English.
2. Make `workspace/configChanged` a first-class part of the AppServer Protocol spec, not a footnote in a milestone doc.
3. Give module authors a clear, copy-pasteable pattern for annotating `ReloadBehavior` on their own `[ConfigSection]` types.
4. Establish conformance tests for the RPC handlers that must emit `workspace/configChanged`, so that a future refactor does not silently drop the notification.
5. Establish Desktop-side tests for the Proxy-aware lock and Tier C edit staging, so that an accidental reversion does not re-enable the edit race this series was designed to eliminate.

### 2.2 Non-Goals

- Rewriting sections of `session-core.md` or other specs unaffected by this series.
- Translating Dashboard documentation to cover the three-tier model.
- Adding end-to-end UI tests beyond what can be driven by unit or component tests.

---

## 3. AppServer Protocol Spec Updates

### 3.1 New Section: `workspace/configChanged`

[`specs/appserver-protocol.md`](appserver-protocol.md) gains a section under the "Notifications" chapter describing `workspace/configChanged`:

- Purpose and relationship to `mcp/statusChanged` (which remains the authoritative per-server health signal).
- Params shape and region taxonomy (copied from [M2 §4.2](settings-reload-ux-m2.md#42-params) and [§4.3](settings-reload-ux-m2.md#43-region-taxonomy)).
- Delivery semantics and ordering guarantees (from [M2 §4.5](settings-reload-ux-m2.md#45-delivery-semantics) and [§8.2](settings-reload-ux-m2.md#82-notification-ordering)).
- The client capability declaration on initialize (from [M2 §4.4](settings-reload-ux-m2.md#44-capability)).
- A non-normative example payload.

### 3.2 Updates to Existing Method Documentation

The spec entries for `workspace/config/update`, `skills/setEnabled`, `mcp/upsert`, `mcp/remove`, `externalChannel/upsert`, and `externalChannel/remove` are each updated to state that the method emits `workspace/configChanged` after a successful write, referencing the new section.

### 3.3 Initialize Handshake

The initialize section of the protocol spec gains a bullet for the new `configChange` client capability: how it is declared, what the server does if absent, and the recommendation that modern clients opt in.

### 3.4 Backward Compatibility Statement

A short paragraph in the protocol spec explicitly documents that:

- Clients without the `configChange` capability are supported indefinitely; they simply do not receive the notification.
- Servers without M2 support (pre-M2 builds) do not emit the notification; clients must tolerate its absence.

---

## 4. Desktop Client Spec Updates

### 4.1 Settings Surface Section

[`specs/desktop-client.md`](desktop-client.md) gains or revises a subsection describing Settings behavior at the UX level:

- The three-tier model (without re-specifying tiers; it cites [M3](settings-reload-ux-m3.md)).
- The Proxy-aware lock on `ApiKey` and `EndPoint`.
- The retirement of the shared footer Save/Cancel.
- The edit-race policy for Tier A and Tier C.

The text stays at the UX-behavior layer; implementation details remain out of scope consistent with the existing Desktop Client spec style.

### 4.2 Capability Declaration

The section describing Desktop's capabilities on initialize is updated to list `configChange` as one of the capabilities Desktop declares.

---

## 5. Bilingual User Documentation

### 5.1 New Document

A new user-facing page is added under `docs/` covering:

- What the three-tier model is and how to recognize each tier in Settings.
- Which settings take effect immediately, which require a subsystem restart, and which require an AppServer restart.
- The behavior of `ApiKey` and `EndPoint` when the managed proxy is active.
- How users can tell from Desktop that a change has been applied vs. is pending.

The page lives in both `docs/settings-lifecycle.md` (Chinese) and `docs/en/settings-lifecycle.md` (English), linked from the respective `index.md` pages, following the bilingual rules in [CLAUDE.md](../CLAUDE.md).

### 5.2 Updates to Existing Pages

If existing pages document Settings behavior that contradicts the new model (for example, older screenshots with the footer Save button), those pages are updated or annotated with a pointer to the new page.

---

## 6. Module Author Guidance

### 6.1 Location

A short authoring guide lives alongside the existing module-development spec referenced from [CLAUDE.md](../CLAUDE.md) and [.cursor/skills/dev-guide/SKILL.md](../.cursor/skills/dev-guide/SKILL.md). Its placement is either a new section in `references/module-development-spec.md` (if present in the repository) or a new page under `docs/` with a link from the dev-guide skill.

### 6.2 Content

The guide answers:

- How to declare a section-level default `ReloadBehavior` on a `[ConfigSection]`.
- How to override the default on individual fields.
- What the `SubsystemRestart` subsystem key means and how to choose one.
- Why unannotated fields default to `ProcessRestart` and why that is the safer choice.
- How to wire a subsystem so that a `Hot` annotation is truthful (i.e., the subsystem actually honors mutations at runtime). M4 does not require existing modules to migrate; it gives future modules the pattern to follow.

### 6.3 Example Patterns

The guide includes at least two worked examples:

1. A section that is entirely `ProcessRestart` (the unannotated default, shown for reference).
2. A section that is `Hot` for one field and `ProcessRestart` for the rest, mirroring how `Skills.DisabledSkills` is handled in this series.

---

## 7. Test Coverage

### 7.1 Backend Protocol Tests

New tests in the protocol test project (e.g., under `tests/DotCraft.Core.Tests/Protocol/`) verify:

- `HandleWorkspaceConfigUpdateAsync`, `HandleSkillsSetEnabledAsync`, `HandleMcpUpsertAsync`, `HandleMcpRemoveAsync`, `HandleExternalChannelUpsertAsync`, and `HandleExternalChannelRemoveAsync` each emit exactly one `workspace/configChanged` notification per successful invocation, with the expected `source` and `regions`.
- A handler that fails validation (invalid params, nonexistent target) does not emit the notification.
- A handler that succeeds partway and then fails during persistence does not emit the notification. (If no such path exists in the current code, this point is omitted.)
- `workspace/configChanged` is not emitted for read-only methods (`mcp/list`, `skills/list`, etc.).
- A client that does not declare the `configChange` capability does not receive the notification; a client that declares it does.

### 7.2 `IAppConfigMonitor` Unit Tests

Unit tests verify:

- The monitor's `Current` returns the singleton `AppConfig` instance.
- Invoking the monitor's notification entrypoint fires the `Changed` event synchronously to in-process subscribers.
- Multiple subscribers all receive the event.
- A subscriber that throws does not prevent other subscribers from receiving the event (or alternatively, the documented policy is enforced, to be decided by the implementation plan; the test pins whichever policy is chosen).

### 7.3 Schema Output Tests

A test verifies `ConfigSchemaBuilder` output for:

- A field annotated `Hot` emits the expected `ReloadBehavior` in the schema.
- A field with no annotation defaults to `ProcessRestart`.
- A section-level default cascades to its fields unless overridden.
- A `SubsystemRestart` annotation preserves its subsystem key in the output.

### 7.4 Desktop Tests

Desktop's existing test infrastructure (see [`desktop/src/main/tests/`](../desktop/src/main/tests/)) is extended with:

- A test verifying that the LLM group renders locked when the Electron main proxy status reports "running" and editable when it reports "stopped".
- A test verifying that staged Tier C edits are discarded with a notice when the proxy becomes active.
- A test verifying that a `workspace/configChanged` notification with `regions: ["skills"]` triggers a `skills/list` re-fetch.
- A test verifying that a Tier A edit in flight is not overwritten by an incoming `workspace/configChanged` echo of that same edit.

Where component-level rendering tests are impractical, unit tests over the state helpers or IPC handlers are acceptable substitutes, provided they exercise the decision logic named in the relevant acceptance criteria.

### 7.5 Regression Suite

All existing tests pass without modification. The existing [`desktop/src/main/tests/proxyWorkspaceConfig.test.ts`](../desktop/src/main/tests/proxyWorkspaceConfig.test.ts) remains authoritative for proxy override behavior; M4 does not alter the behavior it tests.

### 7.6 Pre-Commit Full Test

Consistent with [CLAUDE.md](../CLAUDE.md) and the dev-guide skill, the full test suite (backend `dotnet test` and Desktop test command) passes before the M4 change lands.

---

## 8. Release Notes and Migration

### 8.1 Release Notes Content

Release notes for the version that lands this series state:

- Settings now distinguish fields that apply immediately, fields that need a subsystem restart, and fields that need an AppServer restart.
- New LLM entries for `ApiKey`, `EndPoint`, and `Model` are surfaced in Desktop Settings.
- When the managed proxy is active, `ApiKey` and `EndPoint` are locked to the proxy's values.
- A new Skills panel lets users enable and disable skills live.
- `workspace/configChanged` is a new AppServer notification; details in the protocol spec.

### 8.2 Migration Guidance

Desktop users do not have to migrate. Configuration files on disk are unchanged. Users whose workflows rely on the old footer Save button receive a brief mention of where the equivalent action lives now.

Module authors migrating a custom module are pointed at the new authoring guide (§6) and told that doing nothing is acceptable; their fields will continue to behave as `ProcessRestart`.

---

## 9. Constraints and Compatibility Notes

- M4 introduces no new RPC methods, field annotations, or UI behavior.
- Documentation changes follow the bilingual rule in [CLAUDE.md](../CLAUDE.md): Chinese primary under `docs/`, English under `docs/en/`.
- Tests respect the protocol-conformance requirement stated in the dev-guide skill: any code whose correctness depends on the JSON-RPC wire must have tests asserting the wire behavior.
- New documentation files are added via the existing index-linking pattern; no new site structure is introduced.

---

## 10. Acceptance Checklist

- [ ] [`specs/appserver-protocol.md`](appserver-protocol.md) has a new section describing `workspace/configChanged` with params, regions, delivery semantics, and the client capability.
- [ ] The six RPC methods listed in §3.2 each document their emission of `workspace/configChanged`.
- [ ] [`specs/desktop-client.md`](desktop-client.md) describes the three-tier Settings UX and the Proxy-aware lock at the UX-behavior layer.
- [ ] Bilingual user documentation is added: `docs/settings-lifecycle.md` and `docs/en/settings-lifecycle.md`, linked from the respective indexes.
- [ ] Module authoring guidance for `ReloadBehavior` is added to the dev-guide materials.
- [ ] Protocol tests cover emission and non-emission of `workspace/configChanged` for the methods listed in §7.1.
- [ ] `IAppConfigMonitor` unit tests cover current snapshot access, event delivery, and subscriber error policy.
- [ ] Schema output tests cover annotated and unannotated fields and section-level defaults.
- [ ] Desktop tests cover Proxy-aware lock behavior, staged Tier C discard on proxy activation, Skills re-fetch on notification, and Tier A echo handling.
- [ ] Full backend and Desktop test suites pass before merge.
- [ ] Release notes describe the user-visible changes and explicitly call out the Proxy-aware lock.

---

## 11. Open Questions

1. Should module authoring guidance live in `docs/` (bilingual) or stay alongside the existing `references/module-development-spec.md`? (Preference: wherever the existing guidance lives; mirror the current convention.)
2. Should the release notes section include a short FAQ about "I changed my Model and nothing happened until I restarted"? (Preference: yes, because this is the most likely user-reported confusion with the `Model` field remaining Tier C in this series.)
3. Should Dashboard documentation gain a "known limitation" note about not receiving `workspace/configChanged`? (Preference: yes, to set expectations until Dashboard is updated.)
4. Should the Desktop tests in §7.4 be expanded to include automated screenshot regression for the Proxy-lock banner? (Preference: no, screenshot tests are out of scope.)
