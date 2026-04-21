# DotCraft Desktop Viewer Panel — M3: Conversation Deep-Linking and UX Polish

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
| **Status** | Implemented |
| **Date** | 2026-04-20 |
| **Parent Spec** | [Desktop Client](desktop-client.md), [Desktop Viewer Panel M1](desktop-viewer-panel-m1.md), [Desktop Viewer Panel M2](desktop-viewer-panel-m2.md) |

Purpose: Wire the conversation surface to the viewer panel so that `[label](target)` links and image thumbnails become "open inside DotCraft" actions. Define the toggle-button state that reflects whether the panel is visible, reconcile auto-open with the user's manual visibility preference, and introduce a global Quick-Open shortcut. M3 is the integration and polish milestone that makes the M1 file viewer and the M2 browser tab feel like first-class parts of the conversation workflow.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. Link Resolution Contract](#3-link-resolution-contract)
- [4. Deep-Link Click Behavior](#4-deep-link-click-behavior)
- [5. Image Attachment Click Behavior](#5-image-attachment-click-behavior)
- [6. Panel Toggle Button and Icon State](#6-panel-toggle-button-and-icon-state)
- [7. Auto-Open and Manual-Hide Reconciliation](#7-auto-open-and-manual-hide-reconciliation)
- [8. Global Quick-Open Shortcut](#8-global-quick-open-shortcut)
- [9. Interaction with M1 and M2 Rules](#9-interaction-with-m1-and-m2-rules)
- [10. Accessibility and Localization](#10-accessibility-and-localization)
- [11. Acceptance Checklist](#11-acceptance-checklist)
- [12. Open Questions](#12-open-questions)

---

## 1. Scope

### 1.1 What This Spec Defines

- The resolver that maps a clicked link in the conversation to a viewer-tab descriptor.
- The click behavior for markdown links, including workspace-relative paths, absolute file paths, `file://` URLs, and `http(s)` URLs.
- The click behavior for image thumbnails in the conversation (attachment strip, image messages).
- The header toggle button that shows and reflects the current panel visibility, distinct from the in-panel close control.
- The reconciliation rule between automatic panel-open events and the user's previous manual hide.
- A global `Ctrl/Cmd+P` shortcut that opens the Quick-Open file finder defined in M1.

### 1.2 What This Spec Does Not Define

- Authoring, editing, or previewing links inside the composer while typing.
- Link unfurls, rich previews on hover, or fetched metadata cards.
- Any protocol-level change to how the server sends agent messages or tool results.
- New viewer-tab kinds beyond `file` (M1) and `browser` (M2).
- A general-purpose command palette. The M3 shortcut is scoped to file Quick-Open only.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. Make links in the conversation actionable inside the app: workspace files open as file viewer tabs, `http(s)` URLs open as browser tabs.
2. Make the right panel's visibility state obvious via a persistent header toggle button whose icon reflects open/closed state.
3. Honor the user's manual-hide gesture: auto-open must not fight a user who just closed the panel for the same cause.
4. Preserve the existing changes and plan auto-show rules exactly; M3 is additive, not a rewrite.
5. Offer a single, predictable keyboard path (`Ctrl/Cmd+P`) to open a file anywhere in the desktop window.

### 2.2 Non-Goals

- Introducing link previews, tooltips, or inline metadata fetches.
- Auto-classifying arbitrary text in messages as links.
- Providing editor-style "go to definition" or "open symbol" navigation.
- Changing how images are stored, attached, or transported by the protocol.

---

## 3. Link Resolution Contract

### 3.1 Inputs

- A link target string from a markdown `[label](target)` construct rendered in a conversation message.
- The active workspace root.
- Optionally, the source context of the link (agent message, user message, tool output), used only for logging and diagnostics.

### 3.2 Outputs

The resolver returns exactly one of:

- **File Viewer** — open or focus a file viewer tab pointing at an absolute path under the active workspace root.
- **Browser** — open or focus a browser viewer tab pointing at an `http://` or `https://` URL.
- **External Handoff** — hand the target off to the operating-system default handler (e.g. `mailto:`).
- **Reject** — refuse to act, with a user-visible reason.

### 3.3 Classification Rules

The resolver classifies targets using the following precedence. The first matching rule wins.

1. **Relative path** — the target is a path (no scheme, or a `./` / `../` prefix) that, when resolved against the source context (e.g. the active workspace root, or a context provided by the originating message), points at a local filesystem path. Result: File Viewer.
2. **Absolute local path or `file://` URL** — the target resolves to a concrete filesystem path. Result: File Viewer.
3. **`http://` or `https://` URL** — Result: Browser.
4. **Well-known safe external scheme** — for example `mailto:` or `tel:`. Result: External Handoff.
5. **Unknown, unsafe, or malformed target** — Result: Reject with a legible diagnostic.

Deep-link classification is **not** gated by the M1 Open-File workspace-boundary check. A markdown link whose target resolves outside the active workspace is still a valid `File Viewer` result. File readability and existence are enforced at tab creation time (see §4.2 and M1 §6.4 / §8.2), not at classification time. This is an intentional asymmetry: the Open-File workflow (M1 §6) stays workspace-scoped because it is a discovery surface, while deep-linking lets authored content reference any file the process can read.

### 3.4 Determinism

- The classification must be pure with respect to its inputs. It must not perform network calls, filesystem writes, or any stateful action.
- The classification must not mutate the document or the message.
- The resolver must tolerate links that point at files which do not currently exist; such links resolve to File Viewer, and the M1 tab creation is responsible for surfacing the "file not found" state (per M1 §6.4).

### 3.5 Fragment, Query, and Line Hints

- A trailing fragment (`#...`) or query (`?...`) on a workspace-path target is preserved as a hint passed to the viewer tab. M3 does not require the viewer to honor the hint, but it must not refuse the link because the hint is present.
- Any `:<line>` or `:<line>:<col>` suffix commonly used by tool output is preserved as a navigation hint in the same way.

---

## 4. Deep-Link Click Behavior

### 4.1 Where This Applies

- Any markdown link rendered inside a conversation message (agent message, user message, or tool output that renders markdown) is subject to §4 behavior.
- Code blocks, raw text, and non-markdown surfaces are not affected.

### 4.2 Click Action

On primary click of a link in a conversation markdown surface:

1. The resolver (§3) classifies the target.
2. **File Viewer** — if a viewer tab with the same `(kind=file, target=absolutePath)` already exists **in the current thread**, focus it; otherwise create a new file viewer tab in the current thread using the M1 rendering rules. File existence and readability are enforced here: if the file is missing, unreadable, or is not a file, the tab (or an equivalent inline notice) surfaces a clear in-place error per M1 §6.4 / §7.1. The resolved target is **not** required to be inside the active workspace root — deep-links may reference any readable local file. Make the panel visible using the auto-open rule in §7.
3. **Browser** — find an existing browser tab **in the current thread** whose current URL matches the target (after normalization) and focus it; otherwise create a new browser viewer tab in the current thread whose initial navigation is the target URL. Make the panel visible using the auto-open rule in §7.
4. **External Handoff** — hand off to the OS default handler. The panel visibility must not change.
5. **Reject** — show a non-blocking notice inline at the click site or as a transient indicator; do not navigate, do not open the panel.

### 4.3 Modifier Clicks

- `Ctrl/Cmd + click` on a link in a conversation markdown surface must always create a new viewer tab (file or browser, per classification), even if a matching tab already exists. The newly created tab becomes the focused tab.
- `Shift + click` is not assigned a special behavior in M3; it acts as primary click.
- Middle-click on a link is not required in M3 but, if implemented, must follow the same "always-new-tab" semantics as `Ctrl/Cmd + click`.

### 4.4 Legacy Behavior Removal

- Conversation markdown links must no longer unconditionally invoke the OS browser (the current `window.open(..., '_blank')` path for all links). After M3, only External Handoff and `http(s)` targets that specifically cannot be rendered in-panel fall back to the OS browser, and in both cases the spec §3 rules apply.

---

## 5. Image Attachment Click Behavior

### 5.1 Scope

- Applies to image thumbnails rendered by the conversation view, including but not limited to the user attachment strip and any image rendered as part of agent output.
- Does not apply to images inside rendered web pages inside a browser tab.

### 5.2 Click Behavior

- Primary click on an image thumbnail opens an image viewer tab for the image. Tab identity follows M1 §5.2: `(kind=file, target=absolutePath)` where the path is the image's location on disk (if the image is a workspace file).
- Images that do not have a stable on-disk path inside the workspace must fall back to the existing in-conversation lightbox behavior. M3 does not require persisting non-workspace images to disk just to open them in a viewer tab.

### 5.3 Coexistence with Existing Lightbox

- The existing lightbox remains available as a fallback for non-workspace images and as a low-friction preview path.
- If a workspace-backed image is clicked, the viewer tab path takes priority over the lightbox.

---

## 6. Panel Toggle Button and Icon State

### 6.1 Location

- The desktop window exposes a dedicated **panel toggle button** placed in the window chrome where the user can reach it regardless of the current panel state. A typical location is the top-right of the workspace view.
- This button is distinct from the in-panel `×` close control introduced in M1. The `×` control only hides the panel; the toggle button can show or hide.

### 6.2 Icon State

- When the panel is visible, the toggle button displays an "panel open" icon variant.
- When the panel is hidden (user preference or responsive layout), the toggle button displays an "panel closed" icon variant.
- The icon state must remain in sync with the user's preferred-visible state; transient responsive-layout hides that force the panel closed must not change the user's preference, and the icon must reflect the user's preference rather than the transient visibility when they differ.

### 6.3 Activation

- Clicking the toggle flips the user's preferred-visible state, which then combines with the responsive-layout rule (per existing `resolveResponsivePanels` behavior) to determine whether the panel is actually visible.
- The toggle must be keyboard-activatable with a stable shortcut (platform-appropriate) and surfaced with an accessible label.

### 6.4 No Conflict with In-Panel Close

- The in-panel `×` close control continues to hide the panel, consistent with M1.
- Both controls ultimately drive the same "preferred visible" preference; using one must correctly update the icon state shown by the other.

---

## 7. Auto-Open and Manual-Hide Reconciliation

### 7.1 Problem Statement

- M1 already introduced implicit "activating a viewer tab makes the panel visible" behavior, and the existing client already auto-shows the panel for file changes. Without a rule, an automatic open could repeatedly override a user who just closed the panel on purpose.

### 7.2 Rule

- Each auto-open trigger (deep-link click, image click, tab created by a protocol event, etc.) must record a one-shot reason such that once it has attempted an auto-open, it does not re-attempt the same auto-open for the same reason after the user manually hides the panel.
- This extends the `autoShowTriggeredForTurn` pattern used by the existing changes auto-show to cover link-driven opens. M3 does not redefine the existing change/plan triggers; it specifies that link-driven opens must follow the same conceptual pattern.
- A fresh, explicit user gesture (click on a link, click on the toggle button, choosing Open File) always re-enables panel visibility; manual-hide memory applies only to repeated automatic attempts.

### 7.3 Applicability

- Direct user actions always open the panel:
  - clicking a link,
  - clicking an image,
  - `+` → Open File or New Browser Tab,
  - `Ctrl/Cmd+P`.
- Background or derived triggers (for example an automatic viewer-tab creation in response to a server event, if ever added in the future) must honor §7.2.

---

## 8. Global Quick-Open Shortcut

### 8.1 Shortcut

- `Ctrl+P` on Windows and Linux, `Cmd+P` on macOS, opens the M1 Quick-Open file finder.

### 8.2 Precedence

- The shortcut is window-scoped. It takes precedence over arbitrary text input only when the input does not have its own binding for `Ctrl/Cmd+P`. Concretely:
  - The composer and other editable text areas may intercept `Ctrl/Cmd+P` if they define their own use; otherwise the shortcut opens Quick-Open.
  - The shortcut must not fire while a modal dialog already has focus.
- When Quick-Open opens via shortcut, it behaves exactly as the M1 Open-File finder: same result source, same workspace scoping, same submission behavior (§M1.6).

### 8.3 Discoverability

- The shortcut must be discoverable either through the `+ → Open File` menu entry (showing the shortcut next to the label) or through an equivalent hint.

---

## 9. Interaction with M1 and M2 Rules

### 9.1 Reuse of Tab Identity

- Deep-link focus / create decisions operate against the **currently active thread's** viewer-tab set, per M1 §5.2. A deep-link click in thread A never reuses a tab that belongs to thread B.
- File-viewer tab identity continues to be `(kind=file, absolutePath)` per M1 §5.2.
- Browser-tab identity continues to be `(kind=browser, tabId)` per M2 §4.2. A deep-link click to an `http(s)` URL that matches an existing browser tab's current URL in the active thread must focus that tab rather than create a new one; however, because browser tabs are not keyed by URL, this match is a current-state check, not an identity check, and the existing tab's URL may later diverge.

### 9.2 Respect for Existing Auto-Show

- M3 must not regress the existing auto-show rules for changes and plan tabs. Link-driven auto-open uses §7 reconciliation and must not interfere with those system-tab triggers.

### 9.3 Security Envelope (M2)

- Browser tabs opened by deep-link click must honor the same scheme allowlist and new-window routing rules defined by M2 §6. The resolver's output in §3 must already have enforced the scheme filter; browser tabs never receive targets that M2 would have blocked.

### 9.4 Workspace Boundary and Entry-Point Asymmetry

- The M1 workspace-boundary rules in §8.1 apply only to the Open-File workflow (including `Ctrl/Cmd+P` in §8), because that workflow is a discovery surface whose result list is inherently workspace-scoped.
- Deep-link clicks (§4) are **not** subject to the M1 workspace-boundary rule: a link whose target resolves outside the active workspace root may still produce a file viewer tab. This is an intentional asymmetry, reflecting the fact that link targets are produced by agents, tools, and users — not by the desktop's own discovery UI — and it is common for those producers to reference files outside the workspace (logs, caches, system paths).
- Regardless of entry point, the M1 §8.2 rules still apply: the target must be a readable file; directories, devices, and other non-file objects never produce viewer tabs. Workspace switch (M1 §8.3) still discards all viewer tabs across all threads of the previous workspace.

---

## 10. Accessibility and Localization

### 10.1 Accessibility

- Links in the conversation must remain indistinguishable from current behavior for assistive technologies: they are anchors with a label, and activation with keyboard (Enter) must trigger the same click action as a mouse click.
- The panel toggle button must expose an accessible label that conveys both the action and the current state (e.g. "Hide viewer panel" vs. "Show viewer panel").
- Opening Quick-Open via shortcut must move focus into the search input, and closing it must restore focus to the prior focused element.

### 10.2 Localization

- All new user-facing strings introduced by M3 (toggle button aria label, reject-notice text, shortcut hint, external-handoff notice) must be localizable through the desktop client's existing localization mechanism.

---

## 11. Acceptance Checklist

- [ ] Clicking a conversation markdown link whose target is a workspace-relative or workspace-internal file opens or focuses a file viewer tab in the current thread and makes the panel visible.
- [ ] Clicking a conversation markdown link whose target is an absolute local path or `file://` URL **outside the active workspace root** still opens a file viewer tab (provided the path points at a readable file); the M1 workspace-boundary rule does not apply to deep-links.
- [ ] Clicking a conversation markdown link whose target is a path referring to a directory, device, or non-existent file surfaces a visible error (either inline at the click site or in a created tab) rather than silently succeeding.
- [ ] Clicking a conversation markdown link whose target is an `http(s)` URL opens or focuses a browser viewer tab in the current thread and makes the panel visible; no OS browser is invoked.
- [ ] Clicking a conversation markdown link whose target is `mailto:` or similar safe external scheme hands off to the OS default handler without changing panel visibility.
- [ ] Clicking a conversation markdown link whose scheme is unsupported or malformed is rejected with a legible user-visible notice and creates no tab.
- [ ] `Ctrl/Cmd + click` on a link always creates a new viewer tab in the current thread, even when an equivalent tab already exists, and focuses the new tab.
- [ ] Clicking a workspace-backed image thumbnail opens an image viewer tab in the current thread; non-workspace images fall back to the existing lightbox.
- [ ] Deep-link focus / create decisions operate only on tabs belonging to the currently active thread; switching threads does not leak tab state across threads.
- [ ] The desktop window exposes a panel toggle button whose icon reflects the current panel visibility preference and stays in sync when the in-panel `×` close control is used.
- [ ] Auto-open triggers honor one-shot reasons and do not fight a user who has manually hidden the panel for the same reason.
- [ ] Direct user gestures (link click, image click, `+` menu, toggle button, `Ctrl/Cmd+P`) always produce panel visibility when appropriate.
- [ ] `Ctrl/Cmd+P` opens the M1 Quick-Open file finder when no higher-precedence text input is consuming the shortcut, and its label or menu entry reflects the shortcut.
- [ ] `Ctrl/Cmd+P` remains workspace-scoped (it is an Open-File entry point) and must not list files outside the workspace root even though deep-links may resolve such files.
- [ ] Browser-tab deep-links never receive targets that M2's scheme allowlist would reject.
- [ ] Existing changes and plan auto-show behavior is unchanged.
- [ ] All new user-facing strings are localizable and all new interactive elements are keyboard-reachable with predictable focus behavior.

---

## 12. Open Questions

- Should browser-tab matching for deep-links compare on full-URL equality or on normalized URL (strip tracking parameters, normalize trailing slashes)? The spec currently requires a match on the current URL after normalization but leaves the normalization policy open.
- Should `Shift + click` be reserved for a future "open in OS browser" gesture? M3 leaves it unassigned; assigning it later is a non-breaking change.
- Should the panel toggle button live inside the conversation area header, inside the OS-level window chrome, or in both locations? The spec requires a persistent, reachable location but does not mandate exactly one.
- Should image viewer tabs created from image-strip clicks reuse the same identity space as `(kind=file, absolutePath)`, or should they form their own subclass? Currently specified as the former, but open for review.
