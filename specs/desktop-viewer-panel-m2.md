# DotCraft Desktop Viewer Panel — M2: Native Browser Tab

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-20 |
| **Parent Spec** | [Desktop Client](desktop-client.md), [Desktop Viewer Panel M1](desktop-viewer-panel-m1.md) |

Purpose: Extend the viewer-panel tab model established in M1 with a second viewer kind — a native in-app browser tab — so users can open and navigate web pages without leaving DotCraft Desktop. M2 defines the browser-tab behavior, the add-tab popup's "New Browser Tab" entry, and the security and lifecycle rules that govern the embedded browser.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. User Experience Contract](#3-user-experience-contract)
- [4. Browser Tab Model](#4-browser-tab-model)
- [5. Navigation Workflow](#5-navigation-workflow)
- [6. Security and Isolation Constraints](#6-security-and-isolation-constraints)
- [7. Interaction with Viewer Panel Rules](#7-interaction-with-viewer-panel-rules)
- [8. Failure Modes and Recovery](#8-failure-modes-and-recovery)
- [9. Localization, Accessibility, and Performance](#9-localization-accessibility-and-performance)
- [10. Acceptance Checklist](#10-acceptance-checklist)
- [11. Open Questions](#11-open-questions)

---

## 1. Scope

### 1.1 What This Spec Defines

- The second viewer-tab kind: `browser`, coexisting with `file` tabs defined in M1.
- The behavioral contract for the in-panel browser: URL entry, navigation controls, page rendering, favicon and title propagation into the tab strip.
- The add-tab popup's "New Browser Tab" entry and its lifecycle.
- The security envelope surrounding the embedded browser: permitted schemes, blocked schemes, session isolation, popup and new-window routing, and download handling.
- How browser tabs interact with the system tabs, the responsive layout rules, and the existing M1 open-file workflow.

### 1.2 What This Spec Does Not Define

- Markdown or image-strip deep-linking into browser tabs. That is M3.
- Cookie import, profile synchronization with an external browser, screenshot capture, screenshot-to-agent hand-off, or any automation / scraping surface.
- A bookmarks system, a multi-window tear-off surface, a persistent browser history log, or session-level devtools UX beyond what is strictly needed to recover from crashes.
- Content-blocking, ad-blocking, tracking protection, or extension loading.
- Cross-session persistence of open browser tabs. Browser tabs follow the M1 session-only rule.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. Let users open a web page as a viewer tab in the right panel and navigate it without losing their conversation context.
2. Keep the browser strictly scoped: DotCraft-branded, session-isolated, and limited to normal web schemes.
3. Preserve the M1 tab model: browser tabs are viewer tabs, obey the same panel visibility rules, and never displace system tabs.
4. Prepare a stable browser-tab identity so M3 can focus or reuse an existing browser tab when the user clicks an `http(s)` link in the conversation.

### 2.2 Non-Goals

- Achieving feature parity with orca's browser surface (cookie import, grab mode, session registry, screenshot toolbar) is explicitly not a goal.
- Running browser tabs in separate OS windows.
- Providing an API or tool that lets the agent drive the browser.
- Offering any form of local-file inspection through the browser; local files go through the M1 file viewer.

---

## 3. User Experience Contract

### 3.1 Add-Tab Popup Update

- The `+` popup introduced in M1 gains a new top-level entry: **New Browser Tab**.
- The entry is enabled in M2 and above; no workspace condition is required to enable it.
- Selecting **New Browser Tab** creates a new browser viewer tab, focuses it, and ensures the panel is visible.
- The popup entry order should keep the two M2 entries together (Open File and New Browser Tab), with "New Browser Tab" never hidden when the panel is functional.

### 3.2 A Fresh Browser Tab

- A newly created browser tab starts on a neutral "start" page provided by the desktop client. The start page must not load any remote content implicitly and must not auto-navigate.
- The start page must present at least a URL input prominent enough for the user to begin navigation.
- A fresh browser tab takes a default label (for example "New Tab") until navigation produces a real page title.

### 3.3 Navigation UI

Each browser tab exposes a minimum navigation chrome inside the panel body:

- **Back** and **Forward** controls, enabled only when history allows the action.
- A **Reload** control that becomes a **Stop** control while a navigation is in flight.
- An editable **URL field** that shows the current document URL and accepts user input.
- A visible **loading indicator** while a navigation is in flight.
- A visible **page title** (or URL host if no title is available) reflected into the tab strip for the tab.
- A visible **favicon** for the tab, when the site provides one. When no favicon is available, a default browser icon is used.

M2 does not mandate additional controls (bookmarks, zoom, print). It must not introduce controls that imply unsupported capability, e.g. a "Save Password" affordance.

### 3.4 Focus and Interaction

- Keyboard focus inside the browser tab belongs to the embedded page while the user interacts with it. `Escape` in the URL field must return focus to the page without submitting navigation.
- The URL field must support select-all, copy, paste, and common edit shortcuts.
- Standard page shortcuts (`Ctrl/Cmd+L` for address bar focus, `Ctrl/Cmd+R` for reload, `Alt+Left` / `Alt+Right` for back/forward) are optional in M2 but, if implemented, must follow platform convention.

---

## 4. Browser Tab Model

### 4.1 Tab Descriptor

A browser viewer tab extends the §5.1 descriptor from M1 with:

- **kind** set to `browser`.
- **target** set to a browser-tab identifier (see §4.2), not to the current URL.
- **display label** derived from the page title, with a fallback to host, with a fallback to a localized "New Tab".
- **icon** derived from the current page favicon when available.

### 4.2 Browser-Tab Identity

- Every browser tab has a stable identifier created at tab creation time.
- The identifier is the `target` in the tab descriptor. It does **not** change when the user navigates within the tab.
- Opening "New Browser Tab" multiple times must create multiple independent tabs, each with its own identifier, even if the user happens to navigate two of them to the same URL.
- The identifier rule must be stable enough for M3 deep-linking to refer to an existing browser tab that is already pointed at a target URL.

### 4.3 Ordering and Thread Scope

- Browser tabs render in the viewer-tab region defined in M1, after system tabs.
- Browser tabs obey the same **thread scope** rules as file tabs (M1 §4.4): each thread owns its own independent set of browser tabs; switching threads saves the outgoing thread's browser tabs and restores the incoming thread's browser tabs.
- The saved per-thread state for a browser tab must include at minimum the tab's identifier (M2 §4.2) and the tab's **last-known navigation URL** at the time the thread was left. It may include additional convenience state (title, favicon) as a rendering hint.
- On thread re-entry, the restored browser tab must navigate to its saved URL, using the same security envelope defined in §6 (scheme allowlist, etc.). If the saved URL is no longer valid or permitted, the tab must fall back to the neutral start page with a visible notice; it must not silently close.
- In-tab navigation history (back / forward stack) is not required to survive a thread switch. If the implementation cannot preserve it, Back and Forward become disabled on restore until the user navigates again.

### 4.4 Close Semantics

- Closing a browser tab terminates the embedded page, releases its resources, and removes the tab from the strip.
- Closing an active browser tab follows the same neighbor-reactivation rule as §5.3 in M1 (nearest left, then nearest right, then last-active system tab, then default system tab).

---

## 5. Navigation Workflow

### 5.1 Starting Navigation

- Submitting the URL field navigates the embedded page to the entered target, after a normalization step that:
  - accepts absolute URLs with supported schemes,
  - promotes scheme-less input to a reasonable default (for example an `https://` prefix) when the input looks like a host or URL,
  - treats obviously invalid input (empty, whitespace-only) as a no-op.
- The URL field content must reflect the currently loaded document once navigation completes. If a navigation is in flight, the field may still show the pending URL as an editable draft.

### 5.2 In-Page Navigation

- Ordinary same-document navigation (link clicks, form submits) happens inside the same browser tab.
- Back and Forward operate on the in-tab history and must become disabled when no further history is available in the given direction.
- Reload re-requests the current document. Stop aborts an in-flight navigation.

### 5.3 New-Window and Popup Routing

- Any `target="_blank"` link, `window.open` call, or other action that would normally open a new OS window must be routed to a new browser tab in the viewer panel instead.
- The newly created tab must become the focused viewer tab, consistent with the "Activate any tab ensures the panel is visible" rule in M1 §9.1.
- Automatic popups (pages that open new windows without a user gesture) must be suppressed or heavily throttled; no automatic popup may become the foreground tab without a user gesture.

### 5.4 External Handoff

- Clicking a link with a scheme that the browser tab is not allowed to handle (see §6.2) must either:
  - be silently ignored, if it is clearly unsafe (e.g. `file://`, `chrome://`), or
  - be routed to the operating-system default handler, when that is a well-known, safe handoff (for example `mailto:` or `tel:`).
- M2 must not attempt to render content for schemes it does not support.

### 5.5 Downloads

- M2 does not need to provide a download manager.
- If a navigation triggers a download, the browser tab must cancel the download with a visible, non-fatal notice inside the tab. The browser tab itself must remain usable after the cancellation.
- The browser tab must never automatically save files to disk in M2.

---

## 6. Security and Isolation Constraints

### 6.1 Process and Session Isolation

- All embedded browser tabs in the desktop window must use a dedicated, DotCraft-owned session partition that is separate from the desktop renderer's own session.
- The embedded pages must not be able to read desktop renderer state, desktop IPC, or Electron preload APIs. The boundary is one-way: the desktop renderer can instruct the browser container; embedded pages cannot reach back through it.
- Multiple browser tabs in the same session may share the browsing session (cookies, storage) but must not share state with the DotCraft renderer or with the host OS browser.

### 6.2 Permitted Schemes

- Embedded navigations may load `http://` and `https://` URLs.
- Embedded navigations must block at minimum: `file://`, `chrome://`, `devtools://`, `javascript:` (as a navigation target), and any browser-internal schemes.
- Blocking must occur in the main process, not only in the renderer UI, to preserve the boundary regardless of how navigation was triggered.
- A blocked navigation must surface a legible in-tab explanation rather than silently failing or crashing the tab.

### 6.3 Permissions

- By default, embedded pages must be denied capabilities that would let them affect the user or the machine without clear consent, including at minimum: camera, microphone, geolocation, MIDI, persistent notifications, and clipboard read. Clipboard write may be allowed on user gesture.
- Requesting permission must not auto-grant.
- M2 may choose to show no permission UI at all (deny-by-default) as long as the behavior is documented.

### 6.4 Credentials and Auto-fill

- M2 must not integrate with OS or browser password managers.
- M2 must not persist form auto-fill data across sessions.

### 6.5 Desktop Integration

- Embedded pages must not be able to open desktop file dialogs for the purpose of local-file inspection. Local-file access still goes through M1's explicit Open-File workflow.
- Embedded pages must not be able to navigate the desktop window itself or replace desktop UI.

### 6.6 Crash Isolation

- A crash or hang in one browser tab must not crash other tabs, the desktop renderer, the conversation surface, or the AppServer connection.
- A crashed tab must display a recoverable error state with at least a reload affordance.

---

## 7. Interaction with Viewer Panel Rules

### 7.1 Coexistence with File Tabs

- Browser tabs and file tabs are both viewer tabs; they appear in the same viewer-tab region after system tabs.
- The `+` popup's ordering must keep entries grouped predictably, but is not required to separate browser tabs and file tabs visually in the tab strip.

### 7.2 Panel Visibility Rules

- Activating a browser tab follows the same visibility-preference rule from M1 §9.1.
- Closing the panel hides all tabs including browser tabs; reopening the panel restores the previously active tab, including browser tabs.

### 7.3 Thread Switch and Workspace Switch

- **Thread switch**: switching threads within the same workspace must preserve the outgoing thread's browser tabs via the M1 §4.4 / M2 §4.3 thread-scope rules, and must restore the incoming thread's browser tabs. Thread switching must not close a browser tab from the outgoing thread.
- **Workspace switch**: switching workspaces closes all browser tabs across all threads of the previous workspace, following the M1 §8.3 rule. No browser tab may survive a workspace switch.
- The shared browser session partition (cookies and storage) may or may not be cleared on workspace switch; M2 must commit to a behavior and apply it consistently, and must not leak user identity across workspaces unless the user explicitly establishes it inside the session.
- Multiple threads within the same workspace share the same browser session partition. That is, a user signed into a site inside thread A's browser tab will still be signed in when they open the same site inside thread B's browser tab. This is acceptable because both threads belong to the same user and the same workspace; finer-grained per-thread isolation is out of scope for M2.

### 7.4 Responsive Layout

- Browser tabs obey the existing `collapsed`, `no-detail`, and `full` layout modes. No new layout mode is introduced.
- In `collapsed` and `no-detail` modes, a running browser tab must be paused or rate-limited such that it does not burn CPU while invisible. Exact throttling policy is implementation-defined.

---

## 8. Failure Modes and Recovery

### 8.1 Navigation Failure

- Network errors, DNS failures, TLS failures, and HTTP error pages are rendered by the browser tab as user-readable error pages.
- The URL field must remain operable after a failed navigation so the user can retry or edit the URL.

### 8.2 Renderer Crash

- If the embedded renderer crashes, the tab must switch to a clear "this tab crashed" state with a reload control.
- Reloading must recreate the embedded renderer and resume navigation to the previous URL.

### 8.3 Boundary Violations

- Any attempt by an embedded page to step outside the allowed boundary (denied scheme, forbidden API, permission request) must be logged at a level suitable for diagnostics and must not interrupt the conversation workflow.

### 8.4 Resource Exhaustion

- The browser surface must not consume so much memory that the desktop renderer becomes unresponsive. Implementations should apply a reasonable cap on concurrent browser tabs and warn the user before creating a tab beyond the cap, rather than silently failing.

---

## 9. Localization, Accessibility, and Performance

### 9.1 Localization

- All user-facing strings introduced by M2 (popup entry label, error pages, blocked-scheme messages, crash recovery prompts) must be localizable through the desktop client's existing localization mechanism.
- Page content, URLs, and page titles are not translated.

### 9.2 Accessibility

- URL field, navigation controls, and close control must be reachable by keyboard alone.
- Focus must move predictably between the URL field and the embedded page.
- The tab strip must remain operable by keyboard after M2's additions; browser tabs must not introduce focus traps.

### 9.3 Performance

- Creating a browser tab must not block the main conversation surface.
- Rendering a page should not delay conversation streaming updates beyond the acceptable latency already defined in [Desktop Client](desktop-client.md) §9.1.
- Tabs that are not the active tab should continue to run but must not slow down the active conversation surface.

---

## 10. Acceptance Checklist

- [ ] The `+` popup shows a "New Browser Tab" entry that is enabled whenever the panel is functional.
- [ ] Creating a new browser tab focuses the new tab and makes the panel visible.
- [ ] Each browser tab exposes the required navigation chrome (back, forward, reload/stop, URL field, loading indicator, title, favicon).
- [ ] Back and Forward are enabled only when history permits.
- [ ] Typing a URL and submitting navigates the embedded page; scheme-less inputs are normalized.
- [ ] `target="_blank"` and `window.open` navigations are routed into a new in-panel browser tab, not the OS browser.
- [ ] `file://`, `chrome://`, `devtools://`, `javascript:`, and other browser-internal schemes are blocked with a visible in-tab message.
- [ ] Safe external schemes (e.g. `mailto:`) are handed off to the OS default handler, not rendered in-tab.
- [ ] Embedded pages cannot access desktop IPC, preload APIs, or the Electron renderer's session.
- [ ] Permission requests (camera, microphone, geolocation, notifications, clipboard read) are denied by default.
- [ ] A tab crash does not affect other tabs, the conversation surface, or the AppServer connection, and the crashed tab offers a reload recovery path.
- [ ] Thread switch saves the outgoing thread's browser tabs (including their last-known URLs) and restores the incoming thread's browser tabs; tabs do not flash across thread boundaries.
- [ ] A restored browser tab navigates to its saved URL using the same security envelope as a fresh navigation; an invalid or disallowed saved URL falls back to the neutral start page with a visible notice rather than silently closing.
- [ ] Workspace switch closes all browser tabs across all threads of the previous workspace.
- [ ] Collapsed and no-detail layout modes throttle invisible browser tabs without discarding their state.
- [ ] All new user-facing strings are localizable.
- [ ] Tab identity remains stable across navigations inside the same tab (i.e. M3 can reference the tab by its identifier).

---

## 11. Open Questions

- Should the shared browser session partition be cleared on workspace switch, or preserved across workspace switches within the same app process? Both are safe; the spec requires a committed answer before M2 implementation.
- Should M2 expose any form of "open current URL in OS browser" affordance? This would be convenient but has no behavioral requirement in M2.
- Should multiple browser tabs in the same window share storage with each other, or should each tab have its own storage silo? Spec currently allows sharing inside the same window session but leaves the decision open.
- Does M2 need a visible "security indicator" near the URL field (HTTPS padlock) beyond what the embedded page's chromeless rendering normally shows? Recommended but not required.
- Should per-thread browser-tab state persist across application restarts, or only survive thread switches within the same application session? The spec requires the latter; the former is allowed as long as the security envelope is re-applied on restore.
- Should the in-tab history stack (back / forward) be preserved across thread switches? The spec currently permits losing it on thread re-entry for simplicity; preserving it is a valid extension.
