# DotCraft Settings Reload UX — M3: Desktop Settings Rewrite

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-19 |
| **Parent Spec** | [Settings Reload UX Design](settings-reload-ux-design.md), [Desktop Client](desktop-client.md) |

Purpose: specify the Desktop Settings user experience that consumes the M1 field classification and the M2 change notification. M3 replaces the monolithic footer "Save / Cancel" model with a tier-aware interaction set: live-apply for hot fields, explicit restart actions for subsystem-restart fields, and staged "Apply & Restart" flows for process-restart fields. M3 also introduces the Desktop LLM entry and its Proxy-aware lock.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. Information Architecture Changes](#3-information-architecture-changes)
- [4. Footer Save/Cancel Retirement](#4-footer-savecancel-retirement)
- [5. Tier A UX](#5-tier-a-ux)
- [6. Tier B UX](#6-tier-b-ux)
- [7. Tier C UX](#7-tier-c-ux)
- [8. LLM Entry and Proxy-Aware Lock](#8-llm-entry-and-proxy-aware-lock)
- [9. Skills Panel](#9-skills-panel)
- [10. MCP Tab Cleanup](#10-mcp-tab-cleanup)
- [11. Reacting to `workspace/configChanged`](#11-reacting-to-workspaceconfigchanged)
- [12. Edit-Race Policy](#12-edit-race-policy)
- [13. Accessibility, Keyboard, and Localization](#13-accessibility-keyboard-and-localization)
- [14. Constraints and Compatibility Notes](#14-constraints-and-compatibility-notes)
- [15. Acceptance Checklist](#15-acceptance-checklist)
- [16. Open Questions](#16-open-questions)

---

## 1. Scope

### 1.1 What This Spec Defines

- The behavior contract for the Desktop Settings surface once M1 and M2 are in place.
- How each of the three tiers is presented to the user (live-apply, subsystem restart, process restart).
- The new LLM entry (`ApiKey`, `EndPoint`, `Model`) and the Proxy-aware lock that guards it.
- The new Skills panel.
- The retirement of the shared footer "Save" and "Cancel" buttons and their per-tier replacements.
- How the Settings UI reacts to `workspace/configChanged` notifications from the server.
- The edit-race policy when a user's local edits collide with incoming change notifications.

### 1.2 What This Spec Does Not Define

- Visual design: colors, spacing, icons, typography, specific copy beyond behavioral constraints.
- Component frameworks or state store shape.
- Archived Threads and Usage tabs, which are unchanged by this series.
- Conversation-scoped Model switching in [`ConversationWelcome.tsx`](../desktop/src/renderer/components/conversation/ConversationWelcome.tsx). That flow continues to work as it does today and is not part of Settings.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. Tell the user, at the moment of editing, whether the change is live or needs a restart.
2. Eliminate the "Saved" toast that actually means "stored on disk, may or may not take effect."
3. Introduce Desktop entries for the three most-requested-but-currently-hidden fields (`ApiKey`, `EndPoint`, `Model`) without letting them silently collide with the managed proxy.
4. Surface Skills as a first-class setting so users can reason about them from Settings instead of spelunking the filesystem.
5. Keep every existing administrative action (Restart AppServer, Restart proxy, OAuth login, Refresh usage) reachable with no regression in reachability or discoverability.

### 2.2 Non-Goals

- Adding new AppServer RPC methods. The interactions use the methods in place after M2.
- Auto-restarting AppServer on behalf of the user without explicit confirmation.
- Offering an "Apply without restart" escape hatch for Tier C fields.
- Introducing settings import/export or profile sharing. Out of scope.

---

## 3. Information Architecture Changes

### 3.1 Existing Tabs

`General`, `Connection`, `Proxy`, `Usage`, `Channels`, `Archived Threads`, `MCP` remain as the top-level tabs. Their internal structure is reshaped by this spec where indicated below.

### 3.2 New Content

- A **LLM group** is added under General (preferred) or hosted as a dedicated tab. The decision between "group within General" and "dedicated tab" is an implementation-plan choice; the behavioral contract is identical.
- A **Skills panel** is added. It can be hosted as a new top-level tab or as a sub-section under General. Placement is an implementation-plan choice; the behavioral contract is identical.

### 3.3 Unchanged Tabs

`Usage` and `Archived Threads` behavior is unchanged. They are listed here to make the scope explicit.

---

## 4. Footer Save/Cancel Retirement

### 4.1 Removal

The single pair of footer buttons that currently drives batch Save for Connection and Proxy tabs is removed. No tab retains a shared footer Save.

### 4.2 Replacement per Tier

- **Tier A** controls autosave on change and never render a Save button.
- **Tier B** controls retain their per-subsystem restart button (e.g., "Restart proxy") as the action that applies grouped changes.
- **Tier C** controls render an explicit composite action at the group level: **"Apply & Restart"** (for groups whose only path to effect is a full AppServer restart) or **"Apply"** followed by a restart prompt. Individual fields may also expose a per-field "Revert" action for staged local edits.

### 4.3 Cancel Semantics

The generic "Cancel" button that previously navigated away without restoring state is removed. Leaving the Settings surface does not silently persist Tier C edits; unsaved staged edits are either shown as pending (see §7.3) or discarded with a confirmation.

---

## 5. Tier A UX

### 5.1 Interaction

- The control is always directly interactive.
- On change, Desktop persists the edit through the appropriate RPC (see §9.3 for Skills, §10 for MCP, and §8 for LLM — note §8 is Tier C, not A).
- While the RPC is in flight, a compact spinner or fade state is acceptable; a modal or blocking overlay is forbidden.
- On success, a momentary "Saved" acknowledgment (inline, next to the control) confirms the write.
- On failure, the edit is reverted visually and an error message appears inline. A toast may also be used for the first failure in a session to aid discoverability.

### 5.2 Debouncing

- Text inputs and sliders debounce at an interval short enough to feel responsive yet long enough to avoid excessive RPCs. The exact value is an implementation choice; the constraint is that a single keystroke burst produces at most one successful RPC per destination value.
- Toggles and selects persist on change without debouncing.

### 5.3 No Restart Prompts

Tier A edits never produce a restart prompt or a "requires restart" banner. If the underlying subsystem later fails to honor the edit, that is a bug — not a UX outcome.

---

## 6. Tier B UX

### 6.1 Interaction

- The control is editable in place.
- A visible subsystem-level banner indicates the subsystem's current status (e.g., "Proxy: running", "Proxy: stopped").
- Changing a Tier B field marks the subsystem as having pending changes. A "Restart {subsystem}" button, grouped with the subsystem's controls, becomes the primary call to action.
- The restart button remains the existing action it is today (for proxy, this is `window.api.proxy.restartManaged()`).

### 6.2 Status Feedback

- While the subsystem is restarting, the control group displays a non-blocking indicator.
- On restart success, the group reflects the new status.
- On restart failure, the previous status is restored and the error is surfaced inline.

### 6.3 Scope in M3

In M3 the only Tier B subsystem exposed through Settings is the Desktop-managed proxy. Other subsystems (LSP, etc.) may adopt Tier B in future features; their UX should follow the same contract.

---

## 7. Tier C UX

### 7.1 Staging

- Tier C controls are editable but **staged**: the edit is held locally until the user explicitly applies it.
- Each Tier C group displays a compact pending-changes summary (for example, "2 changes pending").
- Individual fields offer a "Revert" action to discard a staged edit.

### 7.2 Apply Action

- Each Tier C group has one of:
  - **"Apply & Restart"** — a composite button that persists the staged edits and initiates an AppServer restart.
  - **"Apply"** followed by a persistent banner offering "Restart now" or "Restart later."
- The choice between the two patterns per group is an implementation-plan decision based on the group's semantics. For groups whose values are actively dangerous to apply without an immediate restart (for example, the Connection tab's port and mode fields, where the current AppServer's transport is about to change), the composite button pattern is preferred to prevent accidental misconfiguration.

### 7.3 Cross-Tab Pending Summary

If staged Tier C edits exist in one or more tabs, a top-of-surface summary banner appears regardless of which tab the user is currently viewing. It lists which tabs have pending changes and exposes a single "Apply & Restart all" action. Leaving Settings while staged edits exist prompts the user with a lightweight confirmation: apply now, discard, or keep staged (keep staged is lost when Desktop restarts).

### 7.4 Restart Trigger

- Restarting AppServer is triggered via `window.api.appServer.restartManaged()` (the existing action).
- The UI must visibly distinguish **"AppServer is restarting"** from **"Disconnected"**: the former is an expected transient state; the latter is an error state.
- During the restart, editing is disabled for Tier C controls. Tier A and Tier B controls remain visible but may be grayed out for fields whose RPCs are temporarily unreachable.

---

## 8. LLM Entry and Proxy-Aware Lock

### 8.1 Fields

Three controls are surfaced:

- `ApiKey` — sensitive string, rendered via a masked input consistent with other sensitive fields in Desktop.
- `EndPoint` — URL string.
- `Model` — free-form string. Model suggestions or presets are not part of M3.

All three are Tier C. Any staged edit in this group is subject to the Tier C UX defined in §7.

### 8.2 Persistence Path

Staged edits persist via `workspace/config/update` when the user applies them. The exact payload extension (if `workspace/config/update` must be extended to carry `apiKey` and `endPoint` in addition to `model`) is an implementation-plan decision. Whichever path is chosen must remain compatible with [M2 §5](settings-reload-ux-m2.md#5-trigger-points-in-existing-rpc-handlers) so that applying the edits emits a `workspace/configChanged` notification.

### 8.3 Proxy-Aware Lock

When Electron main reports the managed proxy as active, the LLM group enters a **locked state**:

- `ApiKey` and `EndPoint` become non-editable and display the value currently in use by the proxy (the raw `EndPoint` and a masked `ApiKey`).
- A group-level info banner explains that the values are managed by the local proxy and offers a navigation action to the Proxy tab.
- The "Apply & Restart" action for this group is disabled. If the user had staged edits before the proxy became active, those edits are discarded with a non-blocking notice.
- `Model` remains editable because it is not subject to the proxy override.

When the proxy becomes inactive, the group returns to its Tier C editable state.

### 8.4 Source of Truth

The lock is driven by Electron main's runtime proxy status (e.g., the existing proxy-manager status), not by the content of `ApiKey` / `EndPoint` in configuration. A user whose personal API key happens to equal the proxy-generated key must still receive the locked behavior when the proxy is running.

### 8.5 Key Display

In the locked state, the displayed proxy `ApiKey` is masked identically to other sensitive fields. A "copy" affordance is optional; if provided, it copies the plaintext to clipboard and emits a transient confirmation.

---

## 9. Skills Panel

### 9.1 Data Source

- On load, Desktop fetches the current skills list via `skills/list`.
- The panel shows each skill's name, description (if any), and enabled state.

### 9.2 Interaction

- Each skill has a Tier A toggle.
- Toggling a skill invokes `skills/setEnabled` with the skill's name and the new state.
- On success, the toggle reflects the new state and the inline acknowledgment of §5.1 is shown.
- On failure, the toggle reverts and an inline error is surfaced.

### 9.3 External Changes

When `workspace/configChanged` with `regions: ["skills"]` is received, Desktop re-fetches `skills/list` and reconciles the UI against staged edits per §12.

### 9.4 Scope

M3 does not add skill installation, removal, or editing. Those actions remain out of scope.

---

## 10. MCP Tab Cleanup

### 10.1 Current Behavior

The MCP tab currently exposes per-entry Test / Save / Delete buttons and is outside the footer Save flow. The behavior is already Tier A but visually inconsistent with the rest of Settings.

### 10.2 M3 Changes

- Individual save / test / delete actions remain, but their visual treatment aligns with the new Tier A UX (inline acknowledgments instead of generic toasts).
- An editing entry that has not yet been saved is visibly staged; abandoning the edit (navigating away or clicking "Cancel" within the row) discards it.
- The tab reacts to `workspace/configChanged` with `regions: ["mcp"]` by re-fetching `mcp/list`. `mcp/statusChanged` continues to drive per-server health indicators as today.

### 10.3 No Footer

The MCP tab no longer participates in a shared footer. Its own row-level actions are the only persistence path.

---

## 11. Reacting to `workspace/configChanged`

### 11.1 Subscription

Desktop declares the `configChange` client capability during initialize (see [M2 §4.4](settings-reload-ux-m2.md#44-capability)).

### 11.2 Dispatch

When a `workspace/configChanged` notification arrives, Desktop maps each `region` to a reload action:

| Region | Action |
|--------|--------|
| `workspace.model` | Refresh cached workspace model value used by the LLM group and by any UI that displays it. |
| `skills` | Re-fetch `skills/list`. |
| `mcp` | Re-fetch `mcp/list`. |
| `externalChannel` | Re-fetch `externalChannel/list` for the Channels view. |
| Unknown | Ignore. |

### 11.3 Quiet Reconciliation

Reconciliation never produces a toast unless a user edit is displaced (see §12). Normal server-driven updates are silent: the relevant list simply shows the new data.

---

## 12. Edit-Race Policy

### 12.1 Scenario

A user is editing a Tier A or Tier C field when a `workspace/configChanged` notification arrives for the same region.

### 12.2 Rule for Tier A

- If the user has a pending change not yet sent to the server, local edits win. Desktop defers applying the server's newer values to the edited control until the user has either saved or reverted. Non-edited controls in the same region are updated in place.
- If the user's own RPC has completed and the incoming notification is the echo of it, Desktop de-duplicates by matching the `changedAt` and `source` where possible. If de-duplication is not possible, an idempotent re-read is acceptable.

### 12.3 Rule for Tier C

- Staged local edits are preserved. The incoming server state updates the baseline used by the "Revert" action but does not overwrite the user's staged values.
- A non-blocking toast informs the user: "Another client changed {region}. Your staged edits are preserved; reverting will now restore the new baseline."

### 12.4 Tie-Break

When two edits conflict semantically (for example, two clients each set a different Model), last write wins at the server. Desktop's UI simply reflects whatever the server returns; this is consistent with the rest of DotCraft's collaborative semantics.

---

## 13. Accessibility, Keyboard, and Localization

- All new controls meet the keyboard-navigation and focus-ring expectations defined in [Desktop Client §7](desktop-client.md).
- The Proxy-lock banner and pending-changes summary are reachable in the natural tab order and are announced by screen readers when they appear.
- All user-visible strings added or changed by M3 are bilingual (Chinese and English) and live in [`desktop/src/shared/locales/catalog.ts`](../desktop/src/shared/locales/catalog.ts) alongside existing entries.

---

## 14. Constraints and Compatibility Notes

- Existing Desktop IPC methods (`window.api.settings.*`, `window.api.appServer.*`, `window.api.proxy.*`) are preserved. M3 does not rename or remove any of them.
- Theme, locale, and visible-channels behavior is unchanged.
- Archived Threads and Usage tabs are unchanged.
- The ConversationWelcome Model switcher remains and continues to operate on the per-thread model via `thread/config/update`. The LLM group in Settings governs only the workspace default.
- When Desktop is connected to a remote AppServer in remote-only mode, the Proxy tab still applies only to Electron main's local proxy. The Proxy lock's source of truth remains Electron main; the remote AppServer is not consulted.
- If the server does not send the `configChange` capability or does not emit `workspace/configChanged` (e.g., pre-M2 server), Desktop falls back to eager re-fetch on user action and does not display "external change" toasts.

---

## 15. Acceptance Checklist

- [ ] The shared footer Save/Cancel is removed from Settings; no tab retains a shared footer.
- [ ] Tier A, Tier B, Tier C edit affordances are implemented per §5–§7 and are driven by the schema published in M1.
- [ ] The LLM group exposes `ApiKey`, `EndPoint`, `Model` as Tier C with staged edits and an explicit Apply & Restart or Apply path.
- [ ] When Electron main's managed proxy is active, `ApiKey` and `EndPoint` are locked per §8.3; staged edits are discarded with a non-blocking notice.
- [ ] The Skills panel lists skills, toggles via `skills/setEnabled`, and reacts to `workspace/configChanged` with `regions: ["skills"]`.
- [ ] The MCP tab UX is consistent with Tier A and no longer relies on a shared footer.
- [ ] Desktop declares the `configChange` client capability on initialize and honors notifications per §11.
- [ ] Edit-race policy per §12 is implemented for at least Tier A (Skills, MCP) and Tier C (LLM group).
- [ ] A user who leaves Settings with pending Tier C edits receives the confirmation defined in §7.3.
- [ ] Restart transitions display "AppServer is restarting" distinct from "Disconnected" and restore editing automatically after reconnect.
- [ ] All new strings exist in both Chinese and English in the Desktop locale catalog.

---

## 16. Open Questions

1. Should the LLM group live in a dedicated tab or under General? (Preference deferred to the implementation plan.)
2. Should the Skills panel live as a dedicated tab, a General sub-section, or embedded into an existing panel? (Preference deferred to the implementation plan.)
3. Should the Proxy-lock banner offer a "Disable proxy and edit" shortcut that stops the proxy before returning to Tier C mode? (Preference: no in M3; the existing Proxy tab is reachable in one click.)
4. For Tier C groups whose fields are heterogeneous (e.g., Connection tab mixing "connection mode" with "WebSocket port"), should the Apply action be per-field, per-group, or per-tab? (Preference: per-group; the implementation plan picks group boundaries based on current tab layout.)
5. Should Desktop cache `skills/list` and `mcp/list` across sessions to avoid the initial fetch delay? (Preference: no in M3; refresh is fast and caches add invalidation complexity.)
