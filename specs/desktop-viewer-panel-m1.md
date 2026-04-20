# DotCraft Desktop Viewer Panel — M1: Panel Foundation and Native File Viewer

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Draft |
| **Date** | 2026-04-20 |
| **Parent Spec** | [Desktop Client](desktop-client.md) |

Purpose: Evolve the desktop right-side panel from a fixed three-tab surface into a hybrid panel that keeps the existing system tabs (changes, plan, terminal) while supporting user-added dynamic **viewer tabs**. Ship a native file viewer covering plain text and source files, images, and PDFs, all scoped to the currently opened workspace. M1 establishes the panel, the tab model, the file-open workflow, and the tab identity rules that later milestones (browser tab, markdown deep-linking) build on.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. User Experience Contract](#3-user-experience-contract)
- [4. Panel Model](#4-panel-model)
- [5. Tab Model](#5-tab-model)
- [6. Open-File Workflow](#6-open-file-workflow)
- [7. File Viewer Behavior](#7-file-viewer-behavior)
- [8. File Resolution and Safety Constraints](#8-file-resolution-and-safety-constraints)
- [9. Interaction with Existing Panel Behavior](#9-interaction-with-existing-panel-behavior)
- [10. Localization, Accessibility, and Performance](#10-localization-accessibility-and-performance)
- [11. Acceptance Checklist](#11-acceptance-checklist)
- [12. Open Questions](#12-open-questions)

---

## 1. Scope

### 1.1 What This Spec Defines

- The user-visible contract of the right-side panel after M1, including the distinction between **system tabs** and **viewer tabs**.
- The lifecycle and identity rules for viewer tabs (create, focus, close, reorder).
- The `+ ` add-tab affordance in the panel header and its popup menu for M1.
- The "Open File" workflow, including the quick-open style file search dialog and workspace-scoped listing.
- The native file viewer behavior for three content classes: text / source, image, PDF.
- The safety rules that restrict the Open-File workflow to files inside the active workspace root. These rules apply to M1 Open-File only; other entry points introduced in later milestones (e.g. markdown deep-link click) are governed by their own specs and are not subject to this workspace boundary.
- The rule that viewer tabs are scoped to the **currently active thread**, not to the window session: switching threads saves the outgoing thread's viewer tabs and restores the incoming thread's viewer tabs.
- How the new panel coexists with the existing `detailPanel` visibility and responsive-layout rules already defined for the desktop client.

### 1.2 What This Spec Does Not Define

- Native browser tab behavior, the `+` popup option "New Browser Tab", or any Electron `<webview>` policy. Those are defined in the M2 spec.
- Markdown link deep-linking, image-strip click integration, global `Ctrl/Cmd+P`, and the refined panel open/close toggle icon state. Those are defined in the M3 spec.
- File editing, saving, formatting, diff generation, or any form of write-back. M1 viewers are read-only.
- Arbitrary-path file browsing through a system file picker, even inside or outside the workspace. Only the workspace-scoped Quick-Open finder is an M1 entry point.
- Opening files **from sources other than the Open-File workflow**. Any future entry point (link click, drop, etc.) is out of scope for this spec.
- Concrete framework or renderer choices (e.g. Monaco vs. another text viewer, Chromium PDF vs. pdfjs). This spec is behavioral.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. Give users a first-class "look at a file without leaving the app" surface inside the right panel, without disturbing the existing changes / plan / terminal workflow.
2. Establish a stable tab model that can host additional viewer kinds (browser in M2) without structural rework.
3. Make file discovery fast and familiar via a quick-open style search that is scoped to the active workspace.
4. Make the Open-File workflow safe by construction: the search surface lists only files inside the active workspace root, and selecting a result cannot escape that root.
5. Make viewer tabs belong to the **active thread** so a user's on-screen working set (which files or pages they were inspecting) follows the conversation they belong to, rather than being an ambient property of the window.
6. Keep the panel's existing responsive and manual-toggle semantics intact.

### 2.2 Non-Goals

- Replacing or restyling the existing changes, plan, or terminal tabs.
- Providing a general-purpose file manager or tree explorer in the panel.
- Delivering an editor or code-intelligence experience.
- Rendering content types beyond text / source, image, and PDF in M1.
- Persisting open tabs across application restarts or workspace switches.

---

## 3. User Experience Contract

### 3.1 Panel Header

- The right-side panel header continues to expose the existing system tabs (changes, plan, terminal) on the left.
- The header gains a `+` button at the right of the tab strip, always visible when the panel is open, that opens the add-tab popup.
- The header retains a close control (current `×`) that hides the panel. M1 does not change the meaning or position of this control.

### 3.2 Add-Tab Popup (M1 surface)

- Clicking `+` opens a compact popup anchored to the `+` button.
- In M1, the popup contains at least one entry: **Open File**. It may render additional entries that M2 will enable, but those entries must be visibly disabled or marked as "available later" until M2 ships.
- Selecting **Open File** closes the popup and opens the Quick-Open file finder described in §6.
- Pressing `Escape` or clicking outside the popup dismisses it without side effects.

### 3.3 Opening a File

- Selecting a file result in the Quick-Open finder opens a new viewer tab, makes it the active tab, and ensures the panel is visible.
- If the chosen file is already open as a viewer tab, the existing tab is focused instead of creating a duplicate (see §5.2 tab identity).

### 3.4 Viewing

- The body of the panel renders the content class appropriate to the file (text, image, or PDF) per §7.
- The user must be able to close a viewer tab at any time via an explicit control in the tab itself (and, where platform convention matches, middle-click).

### 3.5 Closing and Lifecycle

- Closing the last viewer tab does not close the panel and does not change the currently active system tab.
- If the user switches back to a system tab (changes, plan, terminal), the viewer tab set is preserved and remains re-selectable.
- If the user closes the entire panel via the header close control, reopening the panel restores the previously active tab (system or viewer) inside the current session.

---

## 4. Panel Model

### 4.1 Two Tab Classes

- **System tabs** are fixed, deterministic, and correspond to the existing `changes`, `plan`, and `terminal` surfaces. Their set cannot be modified by the user.
- **Viewer tabs** are dynamic and user-controlled. They are created by explicit user actions (M1: Open File), can be individually closed, and do not reappear on their own.

### 4.2 Active Tab

- At most one tab (system or viewer) is active in the panel at a time.
- The active tab determines what renders in the panel body.
- Activating any tab must make the panel visible, using the same "preferred-visible" reconciliation already applied by `setActiveDetailTab` in existing behavior (see §9).

### 4.3 Ordering

- System tabs always render before viewer tabs in the tab strip.
- Viewer tabs render in insertion order. M1 does not require drag-reordering; if implemented, it must not be able to place a viewer tab before any system tab.

### 4.4 Thread Scope

- The viewer-tab list is owned by the **currently active thread**, not by the window session. Each thread has its own independent set of viewer tabs.
- Switching away from a thread must save the outgoing thread's viewer-tab state (the ordered list of tabs and the currently active tab within that list, if any).
- Switching into a thread must restore that thread's previously saved viewer-tab state if one exists; otherwise the thread starts with an empty viewer-tab list.
- A newly created thread starts with an empty viewer-tab list. A deleted thread discards its viewer-tab state.
- The per-thread viewer-tab state must survive closing and reopening the same thread during the current application session. The spec does not mandate on-disk persistence across application restarts, but it must not rule it out; implementations may persist this state as part of thread state.
- Workspace switching is separate from thread switching. See §8.3 for workspace-switch behavior.

---

## 5. Tab Model

### 5.1 Tab Descriptors

Every viewer tab has a stable descriptor composed of:

- a **kind** — in M1, the only value is `file`; M2 will add `browser`.
- a **target** — the normalized, absolute path of the file being viewed. For M1 Open-File, the path is always inside the active workspace root; later milestones may introduce paths outside the workspace root through non-Open-File entry points, in which case the tab is still a valid `file` viewer tab and this descriptor still applies.
- a **display label** — derived from the target (e.g. workspace-relative path when the target is inside the workspace, or file name otherwise), with collision disambiguation when multiple tabs would share the same base name.
- an **icon class** — derived from the resolved content class (text / image / pdf).

Tab descriptors are always resolved in the context of a specific thread. The same `(kind, target)` pair may concurrently exist as a tab under different threads; each thread owns its own descriptor instance.

### 5.2 Tab Identity and Deduplication

- Within the same thread, two viewer tabs are considered the same when they share the same `(kind, target)` pair.
- Attempting to open a file whose `(file, absolutePath)` already matches an existing viewer tab **in the current thread** must focus the existing tab instead of creating a new one.
- Tab identity is not shared across threads: the same file may be open as a tab in multiple threads simultaneously without deduplication.
- Tab identity rules must be stable so M3 deep-linking can reuse them without redefinition.

### 5.3 Close Semantics

- Closing a viewer tab discards its local view state (scroll position, zoom, page). It does not move the file, mutate the file, or emit a protocol event.
- Closing the active viewer tab activates the nearest remaining tab to its left, falling back to the rightmost remaining tab, falling back to the last-active system tab, falling back to the default system tab.

### 5.4 Label Collision Rule

- If two viewer tabs would otherwise display the same base-name label, each tab's label must expand to include enough leading path segments to disambiguate them.
- The disambiguation must remain stable while both tabs are open and must not flicker when unrelated tabs are opened or closed.

---

## 6. Open-File Workflow

### 6.1 Entry Points (M1)

- The `+ → Open File` popup entry is the only M1 entry point into the file-open workflow.
- M3 may add additional entry points. Two categories must be distinguished:
  - Additional **active-query** entry points that open the Quick-Open finder (e.g. `Ctrl/Cmd+P`) must route through the workflow defined here, including its workspace-scoping rules.
  - **Deep-link** entry points (e.g. clicking a markdown link whose target is a file path) are not covered by this §6 workflow and are not required to obey the workspace boundary rules in §8. Those entry points are governed by the M3 spec.

### 6.2 Quick-Open File Finder

- The finder appears as a modal-style dialog anchored to the desktop window.
- The finder offers a single text input for fuzzy-matching file paths and a scrollable result list.
- The result list shows matches scoped to the active workspace root only. Paths outside the workspace must never appear.
- Results should indicate file name prominently and show the workspace-relative path for context.
- The finder must prefer paths that a human would consider relevant for the current workspace: it should hide VCS metadata, common dependency directories, and build artifacts by default, while still listing user files that are otherwise unignored.
- Exact behavioral rules for "relevant" may reuse the same defaults already applied by other desktop file-search surfaces in the client.

### 6.3 Selection and Submission

- Arrow keys move the selection. `Enter` opens the selected result. `Escape` closes the finder with no side effect.
- Clicking a result opens it and closes the finder.
- If the user submits with no selection, the finder must not error silently; it should either keep focus on the input or show an explanatory empty state.

### 6.4 Empty and Error States

- If the workspace file index is not yet ready, the finder shows a lightweight loading state rather than an empty result list, and retries automatically when the index is ready.
- If listing fails, the finder shows a retry affordance and preserves the query.
- If the user selects a result that has meanwhile been deleted or moved outside the workspace, the viewer tab creation must fail with a clear in-panel explanation; no tab is created.

---

## 7. File Viewer Behavior

### 7.1 Content Classification

- The panel classifies each opened file into exactly one of three classes: **text/source**, **image**, or **pdf**.
- Classification must be deterministic for a given file and must be based on a combination of filename extension and content sniffing where helpful.
- If a file cannot be classified into one of the three supported classes, the viewer tab opens in a graceful "unsupported in M1" state that names the file and explains that its type is not previewable yet. No silent failure, no raw-binary dump.

### 7.2 Text and Source Files

- Text and source files render inside a read-only text viewer with:
  - soft or hard line wrapping according to a stable default for the content type,
  - horizontal and vertical scrolling,
  - syntax highlighting derived from file type when meaningful,
  - visible line numbers.
- The viewer must handle very large files without freezing the window; if a file exceeds a safe rendering threshold, the viewer must fall back to a simplified read-only mode rather than refusing to open the file.
- Line endings and common encodings in use across the project (at least UTF-8) must render correctly. Unknown or invalid encodings must not crash the viewer; a visible warning is acceptable.
- Copying text from the viewer must be supported using standard OS shortcuts.

### 7.3 Images

- The image viewer renders the image centered in the tab body with proportional scaling to fit the panel.
- At least the following raster and vector formats must be supported: PNG, JPEG, GIF (static or animated), WebP, SVG.
- The viewer must expose enough interaction to inspect the image: zoom to fit, zoom to 100%, and basic pan when zoomed. The exact control surface is implementation-defined.
- The viewer must show the image's pixel dimensions and byte size in an unobtrusive location inside the tab.

### 7.4 PDFs

- PDFs render inside the viewer tab as a scrollable, read-only document.
- The viewer must expose at minimum: page navigation (next/previous or direct page number), zoom, and scroll.
- Broken or unreadable PDFs must show a legible error inside the tab; they must not crash the window or degrade other tabs.

### 7.5 External-Change Policy (M1)

- The viewer is read-only and does not need to hot-reload when the file changes on disk in M1.
- If the user re-opens the same file through the Open-File workflow, the viewer may refresh its content, but M1 does not require background file watching or automatic refresh.

---

## 8. File Resolution and Safety Constraints

This section governs the **Open-File workflow** defined in §6. Entry points introduced by later milestones that do not go through the Open-File workflow (for example, markdown deep-link clicks defined in M3) are not subject to the workspace-boundary rules in §8.1; they are governed by their own specs. The §8.2–§8.4 rules about permitted target kinds, side effects, and workspace switching apply to **all** viewer tabs regardless of entry point.

### 8.1 Workspace Boundary (Open-File Only)

- The Quick-Open finder must list only files whose resolved absolute path is inside the active workspace root at the moment of listing.
- Selecting a result through the Quick-Open finder must resolve the target path in the desktop process and confirm it still points inside the active workspace root before any viewer tab is created. Paths that escape the workspace root after resolution must be rejected with a visible notice.
- Symbolic links and junctions whose resolved target escapes the workspace root must not appear in finder results and must be rejected if somehow supplied to the Open-File workflow.
- Relative path components (`..`) are resolved before the boundary check.
- This boundary applies **only** to the Open-File workflow. Other entry points may open files outside the workspace root.

### 8.2 Permitted Targets (All Entry Points)

- Only files are openable. Directories, devices, sockets, and other non-file objects must not create viewer tabs.
- The file must be readable by the current desktop process; permission errors must surface as a visible tab-level error rather than an opaque failure.

### 8.3 Workspace Switching (All Viewer Tabs)

- On workspace switch, **all** currently open viewer tabs must be discarded across all threads of the previous workspace, because viewer tab content references paths that belong to the previous workspace scope. Per-thread saved viewer-tab state tied to threads of the previous workspace is cleared from the active window; if per-thread state is persisted with thread data, its validity on return must be re-evaluated against the new workspace.
- Workspace switching must not leave behind half-resolved tabs or stale file handles.

### 8.4 No Side Effects on Disk (All Viewer Tabs)

- Opening a file in the viewer must not modify the file, its timestamps in any user-visible way, or any surrounding metadata.
- The viewer must not register itself as the OS default handler for any file type.

---

## 9. Interaction with Existing Panel Behavior

### 9.1 Visibility Preference

- Activating any viewer tab must apply the same "open the panel and remember the user preferred it visible" rule that `setActiveDetailTab` currently applies for system tabs.
- Hiding the panel via the close control does not discard open viewer tabs; reopening the panel restores them.

### 9.2 Responsive Layout

- Viewer tabs must obey the existing responsive-layout rules (`collapsed`, `no-detail`, `full`) defined for the desktop client without introducing new layout states.
- In `collapsed` and `no-detail` modes, viewer tabs remain preserved in memory even though the panel body is not visible.

### 9.3 Coexistence with System Tabs

- Adding a viewer tab must never change which system tab is remembered as the "most recently used system tab".
- Closing all viewer tabs must restore the previously active system tab, not force the default `changes` tab.

### 9.4 No Impact on Conversation Behavior

- The conversation area, composer, and approval flow are unaffected by viewer-tab state.
- Viewer tabs must not consume or intercept conversation-level keyboard shortcuts.

### 9.5 Thread Switching and Restore

- Thread switching must be treated as the unit boundary for viewer-tab state, per §4.4:
  - Leaving a thread saves that thread's viewer-tab list and which tab was active inside the viewer-tab list (if any).
  - Entering a thread restores its saved viewer-tab list, including which viewer tab was previously active inside that thread.
- The "previously active surface" restored on thread entry must follow the same priority as M1 §5.3: if a viewer tab was previously active, it is re-activated; otherwise the previously active system tab is re-activated; otherwise the default system tab is activated.
- Thread switching must not cause tabs from the outgoing thread to flash into the incoming thread's panel.
- If restoring a previously saved viewer tab fails (e.g. the file no longer exists), the tab must surface a visible in-tab error state rather than being silently omitted, so the user understands that a previously open file is now missing.

---

## 10. Localization, Accessibility, and Performance

### 10.1 Localization

- All user-facing labels introduced by M1 (popup entries, finder placeholder, empty states, error messages, viewer status text) must be localizable through the desktop client's existing localization mechanism.
- File paths and names render as-is and must not be translated.

### 10.2 Accessibility

- The `+` button, popup entries, finder input, result list, tab buttons, and tab close buttons must each be reachable and operable by keyboard alone.
- Focus order must be predictable when the popup and finder open and close; closing either must return focus to the control that opened it.
- The viewer must not rely on color alone to communicate tab-level state (active, error, loading).

### 10.3 Performance

- Opening the popup and opening the finder must feel instantaneous for typical workspaces.
- Initial viewer render for a file under a reasonable size (e.g. a few megabytes) must not block the main conversation surface.
- Very large files must degrade gracefully (see §7.2) rather than stalling the window.

---

## 11. Acceptance Checklist

- [ ] The right-side panel retains existing `changes`, `plan`, and `terminal` tabs with unchanged behavior.
- [ ] A `+` control is visible in the panel header when the panel is open and opens a popup that contains an "Open File" entry.
- [ ] Selecting "Open File" opens a workspace-scoped Quick-Open file finder whose results never include paths outside the workspace root.
- [ ] Choosing a result from the finder cannot produce a viewer tab whose resolved path escapes the workspace root, including via symbolic links or `..` traversal.
- [ ] Choosing a result opens a new viewer tab in the active thread, focuses it, and ensures the panel is visible.
- [ ] Opening the same file twice in the same thread focuses the existing viewer tab instead of creating a duplicate (same `(kind, target)` identity).
- [ ] The same file may be open as independent viewer tabs in two different threads simultaneously; identity deduplication does not cross thread boundaries.
- [ ] Text, source, image, and PDF files render using the rules in §7; unsupported types show a graceful in-tab explanation.
- [ ] Closing a viewer tab follows the §5.3 nearest-neighbor reactivation rule and never reopens system tabs unintentionally.
- [ ] Leaving a thread saves its viewer-tab state, and returning to the thread restores that state (tab list and previously active tab).
- [ ] A newly created thread starts with no viewer tabs; a deleted thread's viewer-tab state is discarded.
- [ ] A restored viewer tab whose file no longer exists surfaces a visible in-tab error instead of being silently dropped.
- [ ] Switching workspaces discards viewer tabs across all threads of the previous workspace.
- [ ] Very large files do not stall the conversation surface; a simplified fallback is used instead.
- [ ] All new user-facing strings are localizable.
- [ ] Popup, finder, and tab controls are fully keyboard-operable and do not introduce color-only state.

---

## 12. Open Questions

- Should M1 ship with basic tab drag-reordering for viewer tabs, or defer it to a later milestone? Current text does not require it but permits it.
- Should very large text files use line-virtualization or a fixed "load first N MB" fallback? The spec requires graceful degradation but not the exact policy.
- Does the Quick-Open finder reuse the existing file-search popover backend or introduce a new workspace-scoped index? This is an implementation decision, not a behavioral one, but should be decided before the M1 implementation plan.
- Should image files over a certain pixel size use progressive or down-scaled rendering by default? Spec currently leaves this to implementation.
- Should per-thread viewer-tab state persist across application restarts (on-disk, alongside thread state) or survive only the current application session? The spec requires survival across thread switches within the same session and permits durable persistence, but does not mandate it.
