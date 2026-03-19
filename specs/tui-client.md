# DotCraft TUI Client Specification

| Field | Value |
|-------|-------|
| **Version** | 0.1.0 |
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
| Turn | `turn_status`, `history`, `active_streaming` | Turn lifecycle, committed conversation history, and in-flight streaming content (message buffer, reasoning buffer, active tool calls). |
| SubAgents | `subagent_entries` | Live progress snapshot from `subagent/progress`. |
| Plan | `plan` | Todo list snapshot from `plan/updated`. |
| Tokens | `token_tracker` | Cumulative input/output token counts for the current turn. |
| Approval | `pending_approval` | Active approval request, if any. |
| System | `system_status` | Compaction/consolidation status. |
| UI | `mode`, `input_buffer`, `scroll_offset`, `focus`, `notification_queue` | Agent/Plan mode, text input, scroll position, focus target, transient notifications. |

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
| `item/started` (type: `toolCall`) | Push new `ActiveToolCall { callId, toolName, arguments, status: Running }` to `active_streaming.tool_calls`. |
| `item/started` (type: `approvalRequest`) | Pause rendering; handled separately via approval flow (§10). |
| `item/agentMessage/delta` | Append `delta` to `active_streaming.message_buffer`. |
| `item/reasoning/delta` | Append `delta` to `active_streaming.reasoning_buffer`. Set `active_streaming.is_reasoning = true`. |
| `item/completed` (type: `agentMessage`) | Finalize: move `active_streaming.message_buffer` to a `HistoryEntry::AgentMessage`. |
| `item/completed` (type: `toolResult`) | Mark matching `ActiveToolCall` as completed. Store result summary. |
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

The terminal screen is divided into three primary zones arranged vertically:

```
┌──────────────────────────────────────────────────┐
│  Status Bar                                [1]   │
├──────────────────────────┬───────────────────────┤
│                          │                       │
│  Chat View               │  Side Panel       [3] │
│  (history + streaming)   │  (Plan / SubAgents)   │
│                      [2] │                       │
│                          │                       │
├──────────────────────────┴───────────────────────┤
│  Input Editor                                [4] │
│  (multi-line, with mode indicator)               │
└──────────────────────────────────────────────────┘
```

| Zone | Widget | Content |
|------|--------|---------|
| [1] Status Bar | `StatusBar` | Thread name, agent mode, token counter, connection status |
| [2] Chat View | `ChatView` | Scrollable history of turns: user messages, agent messages (markdown), tool calls, errors |
| [3] Side Panel | `SidePanel` | Conditionally shown. Contains `PlanPanel` or `SubAgentTable` depending on context. Hidden when no plan and no active SubAgents, giving full width to Chat View. |
| [4] Input Editor | `InputEditor` | Multi-line text input with history, tab completion, and mode indicator |

### 7.2 Responsive Behavior

- **Narrow terminals** (width < 100 columns): Side Panel is hidden. Plan and SubAgent progress are rendered inline in Chat View as collapsible sections.
- **Short terminals** (height < 20 rows): Status Bar is minimized to a single line. Input Editor is fixed at 1 row (expand on Enter for multi-line).
- **Resize**: Crossterm `Event::Resize` triggers an immediate re-layout. No content is lost; scroll positions are preserved.

### 7.3 Overlay System

Modal overlays render on top of all zones. Active overlays capture all input until dismissed.

| Overlay | Trigger | Content |
|---------|---------|---------|
| `ApprovalOverlay` | `item/approval/request` | Approval prompt with operation details and decision buttons |
| `ThreadPicker` | `/sessions` command | List of threads with resume/archive/delete actions |
| `HelpOverlay` | `/help` command or `?` key | Key bindings and slash command reference |
| `NotificationToast` | `system/jobResult` | Transient toast in top-right corner |

---

## 8. Widget Specifications

### 8.1 StatusBar

A single-line widget at the top of the screen.

```
 DotCraft ─ thread_20260319_a1b2c3 ─ Agent Mode ─ ↑1.2k ↓350 tokens ─ ● Connected
```

Segments (left to right):
1. **Brand**: "DotCraft" (bold).
2. **Thread**: Current thread display name or ID. Omitted before thread creation.
3. **Mode**: "Agent Mode" or "Plan Mode". Styled differently per mode.
4. **Tokens**: Cumulative input/output token count for the current turn. Resets between turns. Format: `↑{input} ↓{output} tokens`.
5. **Connection**: "Connected" (green dot) or "Disconnected" (red dot).

### 8.2 ChatView

A scrollable viewport displaying the conversation history and the active streaming content.

#### History Entries

Each `HistoryEntry` in `AppState.history` is rendered as a block:

| Entry Type | Rendering |
|------------|-----------|
| `UserMessage` | Right-aligned or prefixed with `>` in a distinct color. Plain text. |
| `AgentMessage` | Left-aligned. Markdown rendered to styled terminal text with syntax-highlighted code blocks. |
| `ToolCall` | Compact single-line: icon + tool name + formatted arguments. Collapsed by default; expandable to show full arguments and result. |
| `ToolResult` | Shown inline with its parent `ToolCall` when expanded. |
| `Error` | Red-styled error message. |
| `SystemInfo` | Dim grey one-liner (e.g., "Context compacted successfully."). |

#### Active Streaming

While a turn is running, the active cell renders below committed history:

- **Reasoning**: Dim italic text, preceded by a "Thinking" indicator. Live-updated as `item/reasoning/delta` arrives.
- **Agent text**: Markdown rendered progressively. Each delta triggers a re-render of the current paragraph.
- **Tool spinners**: Animated spinner (Braille or dots) next to the active tool name. Multiple tools may spin concurrently. Format: `⠋ ReadFile src/main.rs`.

#### Scrolling

- **Auto-scroll**: When the user is at the bottom, new content auto-scrolls. When the user has scrolled up, auto-scroll is disabled until they return to the bottom.
- **Key bindings**: `↑`/`↓` scroll by line (when input is not focused), `PageUp`/`PageDown` scroll by page, `Home`/`End` jump to top/bottom.

### 8.3 SidePanel

A vertical panel to the right of ChatView, visible when there is active content to show.

#### PlanPanel

Renders the `plan/updated` snapshot as a checkbox list:

```
┌─ Plan: Implement auth ────────────┐
│ ✅ Create User model              │
│ 🔄 Implement login endpoint       │
│ ⬜ Add JWT middleware              │
│ ⬜ Write integration tests         │
└───────────────────────────────────┘
```

Status icons: `✅` completed, `🔄` in_progress, `⬜` pending, `🚫` cancelled.

#### SubAgentTable

Renders the `subagent/progress` snapshot as a live table:

```
┌─ SubAgents ───────────────────────┐
│ Label        Tool       ↑In  ↓Out │
│ code-explorer ReadFile  4.5k 1.2k │
│ test-runner   ● Done    2.0k 0.6k │
└───────────────────────────────────┘
```

- Active SubAgents show their `currentTool`. When `currentTool` is null (thinking), show a spinner.
- Completed SubAgents show "Done" in green.
- Token counts are formatted as compact numbers (e.g., `4.5k`).

### 8.4 InputEditor

A multi-line text input at the bottom of the screen.

Features:
- **Single-line default**: Expands to multi-line when the user presses `Shift+Enter` or when content exceeds one line.
- **History**: `↑`/`↓` (when input is empty) cycles through previous user messages.
- **Tab completion**: Tab triggers completion for slash commands. No file completion in v1.
- **Mode indicator**: Left gutter shows current mode: `[Agent]` or `[Plan]` with distinct colors.
- **Submit**: `Enter` submits the input (calls `turn/start`). `Shift+Enter` inserts a newline.
- **Interrupt**: `Ctrl+C` while a turn is running sends `turn/interrupt`. Double `Ctrl+C` within 1 second exits the TUI.

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
brand = "#7C3AED"         # DotCraft brand color
user_message = "white"
agent_message = "white"
reasoning = "cyan"
tool_active = "yellow"
tool_completed = "gray"
error = "red"
success = "green"
dim = "dark_gray"
mode_agent = "green"
mode_plan = "blue"
approval_border = "yellow"

[status_bar]
background = "dark_gray"
foreground = "white"

[side_panel]
border = "gray"
title = "bold white"
```

Colors accept Ratatui color names (`"red"`, `"cyan"`, `"dark_gray"`) or hex codes (`"#7C3AED"`). Hex colors require a terminal that supports 24-bit true color; the TUI falls back gracefully to the nearest 256-color equivalent.

---

## 12. Crate Structure

### 12.1 Module Organization

The TUI crate is organized into five top-level modules:

| Module | Responsibility |
|--------|---------------|
| `wire` | Wire Protocol client layer: JSON-RPC 2.0 client with request/response correlation, transport abstraction (stdio and WebSocket), Wire DTO types, and error handling. |
| `app` | Application state and event handling: the `AppState` struct and its mutations, Wire notification → state mapping (§6), terminal event → state mapping, slash command parsing/dispatch, and token accounting. |
| `ui` | Ratatui widget implementations: screen layout computation (§7.1), all widgets (StatusBar, ChatView, SidePanel, PlanPanel, SubAgentTable, InputEditor), modal overlays (approval, thread picker, help, notification), and markdown-to-styled-text rendering. |
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