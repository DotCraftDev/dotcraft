# DotCraft TUI Client Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Living |
| **Date** | 2026-03-19 |
| **Parent Spec** | [AppServer Protocol](appserver-protocol.md) |

Purpose: Define the architecture, UI structure, event mapping, and behavioral contract for `dotcraft-tui`, a Rust-based terminal user interface built on [Ratatui](https://ratatui.rs/) that connects to the DotCraft AppServer via the Wire Protocol. This TUI is a standalone alternative to the built-in C# CLI, offering richer interaction, higher rendering performance, and cross-platform single-binary distribution.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. Technology Selection Rationale](#3-technology-selection-rationale)
- [4. Architecture](#4-architecture)
- [5. Connection Modes](#5-connection-modes)
- [6. Wire Protocol Event Mapping](#6-wire-protocol-event-mapping)
- [7. UI Structure](#7-ui-structure)
- [8. Widget Specifications](#8-widget-specifications)
  - [8.1 WelcomeScreen](#81-welcomescreen)
  - [8.2 FooterLine](#82-footerline)
  - [8.3 ChatView](#83-chatview)
  - [8.4 BottomPane](#84-bottompane)
  - [8.5 Slash Commands](#85-slash-commands)
- [9. Input Handling](#9-input-handling)
- [10. Approval Flow UI](#10-approval-flow-ui)
- [11. Theme System](#11-theme-system)
- [12. Crate Structure](#12-crate-structure)

---

## 1. Scope

### 1.1 What This Spec Defines

- The process architecture: how `dotcraft-tui` launches and communicates with a DotCraft AppServer.
- The UI layout: how the terminal screen is divided into functional zones.
- The event mapping: how Wire Protocol notifications are transformed into UI state updates.
- The widget specifications: what each visual component renders and how it behaves.
- The input model: how keyboard events are captured, routed, and dispatched.
- The approval flow: how the TUI presents approval dialogs and sends decisions back to the server.
- The theme system: how colors and styles are configured and applied.
- The crate structure: how the Rust project is organized.

### 1.2 What This Spec Does Not Define

- **Wire protocol semantics**: Thread, Turn, and Item lifecycle, message formats, and transport rules are defined in [appserver-protocol.md](appserver-protocol.md). This spec references them but does not redefine them.
- **Server-side behavior**: Agent execution, tool invocation, hook execution, and session management are server-internal concerns.
- **Built-in C# CLI behavior**: The existing Spectre.Console CLI remains a separate implementation. This TUI does not replace it; both share the same Wire Protocol surface.
- **Markdown rendering implementation**: The specific markdown-to-terminal rendering algorithm is an implementation detail. This spec defines what content is rendered, not the parsing pipeline.

---

## 2. Goals and Non-Goals

### 2.1 Goals

1. **Feature parity**: Expose the full Wire Protocol surface with UX quality matching or exceeding the built-in C# CLI.
2. **Performance**: Sustain 60fps rendering with sub-millisecond frame times, even during concurrent SubAgent progress updates at 200ms intervals.
3. **Single binary**: Distribute as a statically linked binary with zero runtime dependencies (no Node.js, no .NET).
4. **Cross-platform**: Support Windows (10+), macOS, and Linux terminals via Crossterm.
5. **Bilingual**: Display user-facing strings in both Chinese and English, selectable at startup or via configuration.
6. **Extensible rendering**: Support data-driven UI updates from protocol extension namespaces (`ext/<namespace>/...`) without code changes to the core widget tree.
7. **Themeable**: Allow users to customize colors via TOML configuration files.

### 2.2 Non-Goals

- **GUI**: This spec covers terminal-only rendering. A graphical (Electron/Tauri) client is a separate project.
- **In-process agent**: The TUI is a pure protocol client. It does not embed the DotCraft agent runtime.
- **Plugin UI injection**: Extensions contribute data through the Wire Protocol, not executable UI code. The TUI does not load or execute third-party Rust code at runtime.
- **MCP server hosting**: The TUI does not host MCP servers. MCP is handled by the AppServer.

---

## 3. Technology Selection Rationale

### 3.1 Framework: Ratatui + Crossterm

| Criterion | Ratatui (Rust) | Ink (TypeScript) | Decision Factor |
|-----------|---------------|-----------------|-----------------|
| Rendering overhead | Sub-ms cell-diff | ~10-30ms React reconciliation | SubAgent table at 200ms intervals requires minimal render cost |
| Memory footprint | 20-50 MB | 200-400 MB (V8 runtime) | TUI runs alongside the C# AppServer; low overhead matters |
| Distribution | Single static binary | Requires Node.js 18+ | Matches DotCraft's self-contained distribution model |
| Protocol alignment | JSON-RPC 2.0 over stdio/WS | Different API model | Native fit with DotCraft Wire Protocol; direct event mapping |
| Startup time | <100ms | 2-5 seconds | Perceived responsiveness for CLI workflows |

### 3.2 Async Runtime: Tokio

Tokio provides multi-threaded async I/O for concurrent Wire Protocol communication (reading notifications, sending requests) and terminal event processing (key input, resize). The `tokio::select!` macro drives the main event loop.

### 3.3 Serialization: serde + serde_json

JSON-RPC 2.0 messages are serialized with `serde_json`. Wire DTO types are derived with `serde::Deserialize` and `serde::Serialize`, matching the camelCase field conventions defined in [appserver-protocol.md §2.3](appserver-protocol.md#23-serialization-rules).

### 3.4 Markdown: pulldown-cmark

Agent message text is Markdown. `pulldown-cmark` parses it into events, which are then rendered as styled terminal text with syntax highlighting via `syntect`.

---

## 4. Architecture

### 4.1 Process Model

```
┌─────────────────────────────────────────────────────┐
│  dotcraft-tui  (Rust binary)                        │
│                                                     │
│  ┌───────────┐  ┌───────────┐  ┌─────────────────┐ │
│  │ Wire      │  │ App State │  │ UI Layer        │ │
│  │ Client    │──│           │──│ (Ratatui)       │ │
│  │ (JSON-RPC)│  │           │  │                 │ │
│  └─────┬─────┘  └───────────┘  └─────────────────┘ │
│        │                                            │
│  ┌─────┴─────┐  ┌───────────────────────────────┐  │
│  │ Transport  │  │ Input Handler (Crossterm)     │  │
│  │ stdio / WS │  │                               │  │
│  └─────┬─────┘  └───────────────────────────────┘  │
└────────┼────────────────────────────────────────────┘
         │
         │ JSON-RPC 2.0 (JSONL over stdio / WebSocket text frames)
         │
┌────────┴────────────────────────────────────────────┐
│  DotCraft AppServer  (C# process)                   │
│  Session Core → Agent → Tools → LLM                 │
└─────────────────────────────────────────────────────┘
```

### 4.2 Event Loop

The TUI runs a single `tokio::select!` loop that multiplexes four event sources:

```
loop {
    select! {
        msg = wire_client.next_message()  => handle_wire_message(msg),
        evt = terminal_events.next()      => handle_terminal_event(evt),
        _   = tick_interval.tick()        => handle_tick(),
        _   = shutdown_signal()           => break,
    }
}
```

| Source | Yields | Purpose |
|--------|--------|---------|
| `wire_client` | JSON-RPC messages (notifications, responses, server requests) | Protocol event stream |
| `terminal_events` | `crossterm::event::Event` (Key, Mouse, Resize, Paste) | User input |
| `tick_interval` | Timer tick (~16ms = 60fps) | Frame rendering trigger |
| `shutdown_signal` | Ctrl+C / process signal | Graceful shutdown |

### 4.3 Data Flow

```
Wire Notification ──► EventMapper ──► AppState mutation ──► UI render on next tick
                                         ▲
Terminal KeyEvent ──► InputRouter ────────┘
```

1. **Wire messages** arrive asynchronously. The `EventMapper` converts each JSON-RPC notification/request into an `AppAction` that mutates `AppState`.
2. **Terminal events** are captured by Crossterm's async event stream. The `InputRouter` converts key/paste/resize events into `AppAction`s.
3. **On each tick**, the UI layer reads `AppState` and renders a frame via Ratatui's `Terminal::draw()`. This is an immediate-mode model: the full widget tree is evaluated each frame, but Ratatui's cell-diff engine only writes changed cells to the terminal.

### 4.4 AppState

`AppState` is the single source of truth for all UI state. It is a plain struct — not a reactive store. Mutations happen synchronously in the event loop between frames.

| Category | Key State | Description |
|----------|-----------|-------------|
| Connection | `server_info`, `connected` | Tracks AppServer connection status and capabilities. |
| Thread | `current_thread`, `thread_list` | Active thread and available threads list. |
| Turn | `turn_status`, `turn_started_at`, `history`, `active_streaming` | Turn lifecycle, start timestamp (for elapsed display), committed conversation history, and in-flight streaming content (message buffer, reasoning buffer, active tool calls). |
| SubAgents | `subagent_entries` | Live progress snapshot from `subagent/progress`. |
| Plan | `plan` | Todo list snapshot from `plan/updated`. |
| Tokens | `token_tracker` | Cumulative input/output token counts for the current turn. |
| Approval | `pending_approval` | Active approval request, if any. |
| System | `system_status` | Compaction/consolidation status. |
| UI | `mode`, `input_buffer`, `pending_input`, `scroll_offset`, `focus`, `notification_queue` | Agent/Plan mode, text input, queued follow-up messages, scroll position, focus target, transient notifications. |

`ActiveToolCall` carries `started_at: Instant` (set on `item/started`) and `duration: Option<Duration>` (set on `item/completed`) so each tool call can display its elapsed time.

---

## 5. Connection Modes

The TUI supports two connection modes, matching the transports defined in [appserver-protocol.md §2.2](appserver-protocol.md#22-transports).

### 5.1 Subprocess Mode (Default)

The TUI spawns the DotCraft AppServer as a child process and communicates over stdin/stdout:

```
dotcraft-tui
```

Startup sequence:
1. Locate `dotcraft` binary (configurable via `--server-bin` or `DOTCRAFT_BIN` env var; defaults to `dotcraft` on PATH).
2. Spawn: `dotcraft app-server` with stdout/stderr piped.
3. Read stdout lines as JSONL. Write requests to stdin as JSONL.
4. Send `initialize` request → receive response → send `initialized` notification.
5. Ready for user interaction.

On TUI exit: close stdin → the AppServer receives EOF and shuts down gracefully.

### 5.2 Remote Mode

The TUI connects to an existing AppServer over WebSocket:

```
dotcraft-tui --remote ws://localhost:3000/ws
dotcraft-tui --remote ws://localhost:3000/ws?token=<token>
```

Startup sequence:
1. Open WebSocket connection to the provided URL.
2. Perform `initialize` / `initialized` handshake.
3. Ready for user interaction.

On TUI exit: close WebSocket connection. The server continues running.

Reconnection follows the exponential backoff strategy specified in [appserver-protocol.md §15.7](appserver-protocol.md#157-reconnection).

### 5.3 Initialize Parameters

The TUI sends the following during `initialize`:

```json
{
  "clientInfo": {
    "name": "dotcraft-tui",
    "title": "DotCraft Terminal UI (Ratatui)",
    "version": "0.1.0"
  },
  "capabilities": {
    "approvalSupport": true,
    "streamingSupport": true,
    "optOutNotificationMethods": []
  }
}
```

The TUI subscribes to all notification types by default. No opt-outs.

---

## 6. Wire Protocol Event Mapping

This section defines how Wire Protocol notifications and server-initiated requests are mapped to `AppState` mutations.

### 6.1 Turn Lifecycle

| Wire Method | AppState Mutation |
|-------------|-------------------|
| `turn/started` | Set `turn_status = Running`. Clear `active_streaming`. Reset `subagent_entries`. |
| `turn/completed` | Set `turn_status = Idle`. Finalize `active_streaming` into `history`. Update `token_tracker` from `tokenUsage`. |
| `turn/failed` | Set `turn_status = Idle`. Push error to `history`. Show notification with error message. |
| `turn/cancelled` | Set `turn_status = Idle`. Push cancellation notice to `history`. |

### 6.2 Item Lifecycle

| Wire Method | AppState Mutation |
|-------------|-------------------|
| `item/started` (type: `toolCall`) | Push new `ActiveToolCall { callId, toolName, arguments, started_at: Instant::now(), status: Running }` to `active_streaming.tool_calls`. |
| `item/started` (type: `approvalRequest`) | Pause rendering; handled separately via approval flow (§10). |
| `item/agentMessage/delta` | Append `delta` to `active_streaming.message_buffer`. |
| `item/reasoning/delta` | Append `delta` to `active_streaming.reasoning_buffer`. Set `active_streaming.is_reasoning = true`. |
| `item/completed` (type: `agentMessage`) | Finalize: move `active_streaming.message_buffer` to a `HistoryEntry::AgentMessage`. |
| `item/completed` (type: `toolResult`) | Mark matching `ActiveToolCall` as completed. Set `duration = Instant::now() - started_at`. Store result summary. |
| `item/approval/resolved` | Clear `pending_approval`. Resume normal rendering. |

### 6.3 SubAgent Progress

| Wire Method | AppState Mutation |
|-------------|-------------------|
| `subagent/progress` | **Replace** `subagent_entries` with the `entries` array from the notification. Each entry: `{ label, currentTool, inputTokens, outputTokens, isCompleted }`. |

The SubAgent table widget reads `subagent_entries` directly. Because the protocol sends complete snapshots (not deltas), the TUI simply replaces the entire list on each notification.

### 6.4 Token Usage

| Wire Method | AppState Mutation |
|-------------|-------------------|
| `item/usage/delta` | `token_tracker.add(inputTokens, outputTokens)`. The tracker maintains cumulative totals for the current turn. |

### 6.5 System Events

| Wire Method (`system/event` kind) | AppState Mutation |
|------------------------------------|-------------------|
| `compacting` | Set `system_status = Some(Compacting)`. |
| `compacted` | Clear `system_status`. Push info line to `history`. |
| `compactSkipped` | Clear `system_status`. |
| `consolidating` | Set `system_status = Some(Consolidating)`. |
| `consolidated` | Clear `system_status`. Push info line to `history`. |

### 6.6 Plan Updates

| Wire Method | AppState Mutation |
|-------------|-------------------|
| `plan/updated` | **Replace** `plan` with the complete snapshot: `{ title, overview, todos: [{ id, content, priority, status }] }`. |

### 6.7 Job Results

| Wire Method | AppState Mutation |
|-------------|-------------------|
| `system/jobResult` | Push a `NotificationEntry` to `notification_queue` with source, jobName, result text. The notification auto-dismisses after a configurable timeout (default: 10 seconds). |

### 6.8 Thread Lifecycle

| Wire Method | AppState Mutation |
|-------------|-------------------|
| `thread/started` | Set `current_thread` to the new thread. |
| `thread/resumed` | Set `current_thread` to the resumed thread. |
| `thread/statusChanged` | Update `current_thread.status` or refresh `thread_list`. |

---

## 7. UI Structure

### 7.1 Layout Zones

The terminal screen is divided into two primary zones arranged vertically. The top zone grows to fill all available space; the bottom zone is self-sizing based on its content.

```
┌──────────────────────────────────────────────────┐
│                                                  │
│  Chat View                                  [1]  │
│  (history + streaming + inline plan/subagents)   │
│  (flex = 1, takes all remaining height)          │
│                                                  │
├──────────────────────────────────────────────────┤
│  Bottom Pane                                [2]  │
│  ┌──────────────────────────────────────────┐   │
│  │ Status Indicator  (conditional, 1+ rows) │   │
│  │ Pending Input Preview  (if queued)       │   │
│  │ Input Editor   (self-sizing, max 10 rows)│   │
│  │ Footer Line    (1 row, contextual hints) │   │
│  └──────────────────────────────────────────┘   │
└──────────────────────────────────────────────────┘
```

| Zone | Widget | Content |
|------|--------|---------|
| [1] Chat View | `ChatView` | Scrollable history of turns: user messages, agent messages (markdown), tool calls, errors, inline plan and SubAgent progress blocks |
| [2] Bottom Pane | `BottomPane` | Vertical stack: optional `StatusIndicator` (when turn is running) + optional `PendingInputPreview` (when messages are queued) + `InputEditor` + `FooterLine` |

There is no top status bar and no side panel. All status context moves to the `FooterLine` at the bottom. Plan and SubAgent data always render inline in `ChatView`.

### 7.2 Startup Screen

Before the chat UI is shown, a `WelcomeScreen` fills the entire terminal area. It renders from the moment the terminal is initialized until the Wire Protocol handshake completes and the first user interaction is possible. It auto-dismisses when the connection is ready, or immediately on any key press.

See §8.1 for the full `WelcomeScreen` specification.

### 7.3 Responsive Behavior

- **Compact terminals** (height < 20 rows): `StatusIndicator` is hidden. `FooterLine` is suppressed. `InputEditor` is clamped to 1 row.
- **Resize**: Crossterm `Event::Resize` triggers an immediate re-layout. No content is lost; scroll positions are preserved.

### 7.4 Overlay System

Modal overlays render on top of all zones. Active overlays capture all input until dismissed.

| Overlay | Trigger | Content |
|---------|---------|---------|
| `ApprovalOverlay` | `item/approval/request` | Approval prompt with operation details and decision buttons |
| `ThreadPicker` | `/sessions` command | List of threads with resume/archive/delete actions |
| `HelpOverlay` | `/help` command or `?` key | Key bindings and slash command reference |
| `NotificationToast` | `system/jobResult` | Transient toast in top-right corner |

---

## 8. Widget Specifications

### 8.1 WelcomeScreen

A full-screen startup widget displayed while the Wire Protocol handshake is in progress.

#### Layout

```
┌──────────────────────────────────────────────────┐
│                                                  │
│   ██████╗  ██████╗ ████████╗ ██████╗██████╗ ██╗ │
│   ██╔══██╗██╔═══██╗╚══██╔══╝██╔════╝██╔══██╗██║ │
│   ██║  ██║██║   ██║   ██║   ██║     ██████╔╝██║ │
│   ██║  ██║██║   ██║   ██║   ██║     ██╔══██╗╚═╝ │
│   ██████╔╝╚██████╔╝   ██║   ╚██████╗██║  ██║██╗ │
│   ╚═════╝  ╚═════╝    ╚═╝    ╚═════╝╚═╝  ╚═╝╚═╝ │
│                                                  │
│         Welcome to DotCraft v{version}           │
│         Workspace: {workspace_path}              │
│                                                  │
│   Type a message to start                        │
│   /help for commands · /sessions for history     │
│                                                  │
│                    Connecting...                 │
└──────────────────────────────────────────────────┘
```

#### Behavior

- **Size gating**: The ASCII art logo is shown only when the viewport is at least 60 columns wide and 20 rows tall. On smaller terminals, the logo is omitted and only the text lines are shown.
- **Connection state**: While connecting, the bottom line shows "Connecting…" with an animated spinner. Once the handshake completes, it changes to "Ready — press any key or start typing".
- **Auto-dismiss**: The WelcomeScreen is replaced by the chat UI as soon as the handshake completes. Any key press also dismisses it immediately.
- **Brand styling**: "DotCraft" in the text line uses the brand color. The ASCII logo renders in the brand color when the terminal supports true color, or bright magenta otherwise.

### 8.2 FooterLine

A single-row widget rendered below the `InputEditor`. Replaces the old top `StatusBar` with contextual, state-aware information.

#### Layout

```
  ? for shortcuts · Agent (shift+tab to cycle)        ↑1.2k ↓350 tokens · ● Connected
```

#### Left side (contextual hints, priority order)

The left side shows one of the following depending on the current state (highest priority first):

| State | Left content |
|-------|--------------|
| Quit reminder active | `press ctrl+c again to quit` (styled warning) |
| Input has draft, turn running | `tab to queue message` |
| Input has draft, turn idle | `enter to send · shift+enter newline` |
| Input empty, turn running | `esc to interrupt` |
| Input empty, turn idle | `? for shortcuts · {Mode} (shift+tab to cycle)` |

#### Right side (context)

```
↑{input_tokens} ↓{output_tokens} tokens · ● Connected
```

- Token counts reset between turns and are omitted when zero.
- Connection: `● Connected` (green) or `○ Disconnected` (red/dim).

#### Width-based progressive collapse

As the terminal narrows, elements are dropped in this order:
1. Drop `· ● Connected` / `○ Disconnected`.
2. Drop token counts.
3. Shorten mode cycle hint from `(shift+tab to cycle)` to nothing.
4. Drop the shortcut hint entirely.

The footer is fully suppressed on terminals with height < 20 rows.

### 8.3 ChatView

A scrollable viewport displaying the conversation history and the active streaming content. Plan and SubAgent data always render inline here; there is no side panel.

#### History Entries

Each `HistoryEntry` in `AppState.history` is rendered as a block:

| Entry Type | Rendering |
|------------|-----------|
| `UserMessage` | Prefixed with `❯` in the user message color. Plain text with continuation indent. |
| `AgentMessage` | Prefixed with `•` gutter. Markdown rendered to styled terminal text with syntax-highlighted code blocks. |
| `ToolCall` | See §8.3 Tool Call Rendering below. |
| `Error` | Red-styled error message with `✗` prefix. |
| `SystemInfo` | Dim one-liner (e.g., "Context compacted successfully."). |

#### Tool Call Rendering

Tool calls use a Codex-style invocation format with adaptive layout.

**Active (in-flight):**
```
  ⠋ Calling ReadFile("src/main.rs")
```

**Completed (success):**
```
  • Called ReadFile("src/main.rs") (0.3s)
    └ // file content preview, first 1-2 lines, dimmed
```

**Completed (error):**
```
  • Called RunTests() (2.1s)
    └ Error: 3 tests failed
```

Rules:
- The bullet `•` is green on success, red on error. Active tools show an animated Braille spinner.
- The verb changes: `Calling` (active) → `Called` (completed).
- Invocation format: `ToolName("arg1", "arg2")` with JSON argument values.
- **Adaptive layout**: if the full invocation line fits within the available width, it renders inline on the same line as `Calling`/`Called`. If it overflows, the invocation wraps to the next line prefixed with `  └ `.
- **Elapsed time**: shown in dim parentheses after the completed invocation: `(0.3s)`, `(1.2s)`, `(2m 03s)`.
- **Result summary**: always shown below the completed invocation, dim, truncated to `TOOL_CALL_MAX_LINES` (default 5 lines). There is no global "expand/collapse" toggle; results are always partially visible.
- The `tools_expanded` global toggle is removed in favor of always-visible summaries.

#### Active Streaming

While a turn is running, the active area renders below committed history:

- **Reasoning**: Dim italic text, preceded by "💭 Thinking" indicator. Live-updated as `item/reasoning/delta` arrives.
- **Agent text**: Markdown rendered progressively from `message_buffer`. Prefixed with `•` gutter.
- **Tool spinners**: Rendered inline as they arrive. Format: `⠋ Calling ToolName("args")`.

#### Inline SubAgent Block

When `subagent_entries` is non-empty, a live-updating block renders below the streaming area (during an active turn) or inline in committed history (after the turn).

**Active (at least one SubAgent running):**
```
  ──── SubAgents (3 active, 1 done) ────────────────────
  ⠋  code-explorer   ReadFile src/main.rs    ↑4.5k ↓1.2k
  ⠋  test-runner     RunTests               ↑2.0k ↓0.6k
  ⠙  doc-writer      WriteFile docs.md      ↑1.8k ↓0.3k
  •  reviewer        Done                   ↑3.2k ↓0.9k
```

**All complete (collapsed summary):**
```
  ✓ 4 SubAgents completed (↑11.5k ↓2.8k tokens)
```

Rules:
- Active SubAgents show their `currentTool` or a spinner when `currentTool` is null (thinking).
- Completed SubAgents show `●  Done` in green.
- Token counts use compact format: `4.5k`, `1.2M`.
- When all SubAgents complete, the block collapses to a single summary line.
- Press `s` to toggle detail visibility when at least one SubAgent is done.

#### Inline Plan Block

When `plan` is non-null, a plan block renders below history entries (always inline, not in a side panel).

```
  ──── Plan: Implement auth ────────────────────────────
  ✅  Create User model
  🔄  Implement login endpoint
  ⬜  Add JWT middleware
  ⬜  Write integration tests
```

Status icons: `✅` completed, `🔄` in_progress, `⬜` pending, `🚫` cancelled.

#### Scrolling

- **Auto-scroll**: When the user is at the bottom, new content auto-scrolls. When the user has scrolled up, auto-scroll is disabled until they return to the bottom.
- **Key bindings**: `↑`/`↓` scroll by line (when input is not focused), `PageUp`/`PageDown` scroll by page, `Home`/`End` jump to top/bottom.

### 8.4 BottomPane

The `BottomPane` is the vertically-stacked composite widget at the bottom of the screen. Its height is the sum of its visible children.

#### 8.4.1 StatusIndicator

Appears above the `InputEditor` only while `turn_status == Running`. Disappears as soon as the turn completes.

```
  ⠋ Working  (12s · esc to interrupt)
```

- Braille spinner on the left.
- "Working" label with a shimmer animation: each character's brightness oscillates based on `(char_index + tick_count) % period`, creating a traveling wave effect.
- Elapsed time in dim parentheses, updated every second.
- Interrupt hint: `esc to interrupt`.
- If a system status event is active (`compacting` / `consolidating`), the label changes to the system status description.
- On terminals with height < 20 rows, the `StatusIndicator` is hidden.

#### 8.4.2 PendingInputPreview

Appears between `StatusIndicator` and `InputEditor` when the user has queued follow-up messages (via `Tab` while a turn is running).

```
  ┄ Queued: "run the tests again"
```

- Shows the queued message text truncated to one line.
- Dim styling. Hidden when `pending_input` is empty.

#### 8.4.3 InputEditor

A multi-line text input.

```
❯ Type a message or /help...
```

Features:
- **Mode gutter**: `❯` (Agent mode, green) or `✎` (Plan mode, blue) on the left of the first text line. Continuation lines have `  ` (two spaces).
- **Placeholder**: When the input is empty, dim placeholder text "Type a message or /help..." is shown in the gutter area.
- **No top separator**: The old horizontal separator line above the input is removed. The FooterLine below replaces its hint content.
- **Self-sizing height**: Grows with content (1 row per input line). Maximum 10 rows. On terminals with height < 20 rows, clamped to 1 row.
- **History**: `↑`/`↓` (when input is empty) cycles through previous user messages.
- **Tab completion**: `Tab` triggers slash command completion popup when input starts with `/`. When a turn is running, `Tab` queues the current input as a pending message instead of cycling slash commands.
- **Submit**: `Enter` submits. `Shift+Enter` inserts a newline.
- **Interrupt**: `Ctrl+C` while a turn is running sends `turn/interrupt`. Double `Ctrl+C` within 1 second exits the TUI.

#### 8.4.4 FooterLine

See §8.2.

### 8.5 Slash Commands

Slash commands are typed in the InputEditor and processed locally (not sent to the agent).

| Command | Action |
|---------|--------|
| `/help` | Show HelpOverlay with all commands and key bindings. |
| `/sessions` | Open ThreadPicker overlay. List threads via `thread/list`. |
| `/new` | Start a new thread via `thread/start`. |
| `/load <id>` | Resume a thread via `thread/resume`. |
| `/plan` | Switch to Plan mode via `thread/mode/set`. |
| `/agent` | Switch to Agent mode via `thread/mode/set`. |
| `/clear` | Clear the chat history display (does not affect server state). |
| `/cron` | List cron jobs via `cron/list`. Display in chat as a formatted table. |
| `/heartbeat` | Trigger heartbeat via `heartbeat/trigger`. |
| `/quit` | Exit the TUI. |

---

## 9. Input Handling

### 9.1 Focus Model

The TUI has two focus targets:

| Focus | Receives key events | Behavior |
|-------|---------------------|----------|
| `InputEditor` | Default focus. All printable keys, Enter, Backspace, arrow keys for cursor movement. | Text editing. |
| `ChatView` | Focus acquired when user presses `Esc` from InputEditor or scrolls. | Scroll navigation. `Enter` or `i` returns focus to InputEditor. |

When an overlay is active, it captures all input.

### 9.2 Global Key Bindings

These bindings are active regardless of focus:

| Key | Action |
|-----|--------|
| `Ctrl+C` | Interrupt running turn, or exit (double-press). |
| `Ctrl+D` | Exit the TUI (equivalent to `/quit`). |
| `Ctrl+L` | Redraw the terminal. |
| `Tab` (in InputEditor) | Toggle between Agent and Plan mode. |
| `F1` or `?` (in ChatView) | Show HelpOverlay. |

### 9.3 Terminal Compatibility

Crossterm handles cross-platform differences. The TUI detects terminal capabilities at startup:

- **Bracketed paste**: Enabled when supported. Allows multi-line paste without triggering Submit.
- **Kitty keyboard protocol**: Used when available for unambiguous modifier detection.
- **Mouse support**: Disabled in v1. May be enabled in future for click-to-expand on tool calls.

---

## 10. Approval Flow UI

When the server sends an `item/approval/request`, the TUI must:

1. Set `pending_approval` in `AppState`.
2. Show the `ApprovalOverlay` modal.
3. Block normal input until the user makes a decision.
4. Send the decision as a JSON-RPC response.

### 10.1 ApprovalOverlay Layout

```
┌─ Approval Required ──────────────────────────────────┐
│                                                       │
│  🔧 Shell Command                                    │
│  Command:  npm test                                   │
│  Directory: /home/dev/myproject                       │
│                                                       │
│  Reason: Agent wants to execute a shell command       │
│                                                       │
│  ► [a] Accept           Accept this operation         │
│    [s] Accept Session   Accept similar operations     │
│    [!] Accept Always    Remember permanently          │
│    [d] Decline          Reject this operation         │
│    [c] Cancel           Cancel the entire turn        │
│                                                       │
└───────────────────────────────────────────────────────┘
```

### 10.2 Decision Key Bindings

| Key | Decision | Wire Value |
|-----|----------|------------|
| `a` or `Enter` | Accept | `"accept"` |
| `s` | Accept for session | `"acceptForSession"` |
| `!` | Accept always | `"acceptAlways"` |
| `d` | Decline | `"decline"` |
| `c` or `Esc` | Cancel turn | `"cancel"` |

Arrow keys move the selection highlight. `Enter` confirms the highlighted option.

### 10.3 Approval Type Display

| `approvalType` | Icon | Label |
|----------------|------|-------|
| `"shell"` | `🔧` | "Shell Command" |
| `"file"` | `📄` | "File Operation" |

The `operation` field is displayed as the detail line. For shell approvals, this is the command text. For file approvals, this is the operation type and file path.

---

## 11. Theme System

### 11.1 Theme Configuration

Themes are defined in TOML files. The TUI loads the theme from (in priority order):

1. `--theme <path>` CLI argument.
2. `.craft/tui-theme.toml` in the workspace.
3. `~/.config/dotcraft/tui-theme.toml` (user-global).
4. Built-in default theme.

### 11.2 Theme Schema

```toml
[colors]
brand = "#7C3AED"         # DotCraft brand color (used in FooterLine, WelcomeScreen)
user_message = "white"
agent_message = "white"
reasoning = "cyan"
tool_active = "yellow"
tool_completed = "gray"
tool_error = "red"
error = "red"
success = "green"
dim = "dark_gray"
mode_agent = "green"
mode_plan = "blue"
approval_border = "yellow"
status_indicator = "yellow"   # "Working" label and spinner color

[footer]
foreground = "dark_gray"      # Footer line hint text
context_color = "dark_gray"   # Token counts and connection status
```

The `[status_bar]` and `[side_panel]` sections from v0.1.0 are removed. They are replaced by `[footer]` and `status_indicator`.

Colors accept Ratatui color names (`"red"`, `"cyan"`, `"dark_gray"`) or hex codes (`"#7C3AED"`). Hex colors require a terminal that supports 24-bit true color; the TUI falls back gracefully to the nearest 256-color equivalent.

---

## 12. Crate Structure

### 12.1 Module Organization

The TUI crate is organized into five top-level modules:

| Module | Responsibility |
|--------|---------------|
| `wire` | Wire Protocol client layer: JSON-RPC 2.0 client with request/response correlation, transport abstraction (stdio and WebSocket), Wire DTO types, and error handling. |
| `app` | Application state and event handling: the `AppState` struct and its mutations, Wire notification → state mapping (§6), terminal event → state mapping, slash command parsing/dispatch, and token accounting. |
| `ui` | Ratatui widget implementations: screen layout computation (§7.1), all widgets (`WelcomeScreen`, `ChatView`, `InputEditor`, `StatusIndicator`, `FooterLine`), inline panel renderers (`InlineSubAgentBlock`, `InlinePlanBlock`), modal overlays (approval, thread picker, help, notification), and markdown-to-styled-text rendering. The old `StatusBar`, `SidePanel`, `SubAgentTable`, and `PlanPanel` widgets are removed or merged into inline renderers. |
| `theme` | Theme loading and application: TOML configuration deserialization and the built-in default theme. |
| `i18n` | Internationalization: bilingual string tables for English and Chinese. |

### 12.2 Key Dependencies

| Crate | Version | Purpose |
|-------|---------|---------|
| `ratatui` | 0.29+ | Terminal UI framework |
| `crossterm` | 0.28+ | Cross-platform terminal backend |
| `tokio` | 1.x | Async runtime (`rt-multi-thread`, `io-std`, `signal`, `time`) |
| `serde` | 1.x | Serialization (`derive`) |
| `serde_json` | 1.x | JSON-RPC message parsing (`preserve_order`) |
| `toml` | 0.8+ | Theme configuration parsing |
| `pulldown-cmark` | 0.12+ | Markdown parsing |
| `syntect` | 5.x | Code syntax highlighting |
| `clap` | 4.x | CLI argument parsing (`derive`) |
| `tokio-tungstenite` | 0.24+ | WebSocket client (for remote mode) |
| `chrono` | 0.4+ | Timestamp formatting |
| `unicode-width` | 0.2+ | Correct CJK character width calculation |

### 12.3 Feature Flags

| Feature | Default | Description |
|---------|---------|-------------|
| `websocket` | yes | Enable WebSocket transport (pulls in `tokio-tungstenite`). |
| `clipboard` | no | Enable system clipboard integration for copy/paste. |