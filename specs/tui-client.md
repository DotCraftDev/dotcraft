# DotCraft TUI Client Specification

| Field | Value |
|-------|-------|
| **Version** | 0.2.0 |
| **Status** | Living |
| **Date** | 2026-03-19 |
| **Parent Spec** | [AppServer Protocol](appserver-protocol.md) |

Purpose: Define the architecture, interaction model, event mapping, and behavioral contract for `dotcraft-tui`, a terminal client that connects to the DotCraft AppServer via the Wire Protocol.

## Credits

The DotCraft TUI interface design is inspired by [OpenAI Codex CLI](https://github.com/openai/codex), an excellent open-source terminal AI agent by OpenAI. 

We thank the Codex team for their pioneering work in terminal AI agent UX.

---

## Table of Contents

- [1. Scope](#1-scope)
- [2. Goals and Non-Goals](#2-goals-and-non-goals)
- [3. Technology Selection Rationale](#3-technology-selection-rationale)
- [4. Architecture](#4-architecture)
- [5. Connection Modes](#5-connection-modes)
- [6. Wire Protocol Event Mapping](#6-wire-protocol-event-mapping)
- [7. Interaction Surface](#7-interaction-surface)
- [8. Interaction Contracts](#8-interaction-contracts)
  - [8.1 Startup and Ready States](#81-startup-and-ready-states)
  - [8.2 Context Hint Contract](#82-context-hint-contract)
  - [8.3 Transcript Contract](#83-transcript-contract)
  - [8.4 Composer Contract](#84-composer-contract)
  - [8.5 Slash Commands](#85-slash-commands)
- [9. Input Handling](#9-input-handling)
- [10. Approval Flow](#10-approval-flow)
- [11. Presentation Customization](#11-presentation-customization)
- [12. Crate Structure](#12-crate-structure)

---

## 1. Scope

### 1.1 What This Spec Defines

- The process architecture: how `dotcraft-tui` launches and communicates with a DotCraft AppServer.
- The interaction surface: what information areas exist and their responsibilities.
- The event mapping: how Wire Protocol notifications are transformed into UI state updates.
- The interaction contracts: behavioral rules for startup, transcript, composer, hints, and overlays.
- The input model: how keyboard events are captured, routed, and dispatched.
- The approval flow: how the TUI presents approval dialogs and sends decisions back to the server.
- Presentation customization boundaries (high-level).
- The crate structure: how the Rust project is organized.

### 1.2 What This Spec Does Not Define

- **Wire protocol semantics**: Thread, Turn, and Item lifecycle, message formats, and transport rules are defined in [appserver-protocol.md](appserver-protocol.md). This spec references them but does not redefine them.
- **Server-side behavior**: Agent execution, tool invocation, hook execution, and session management are server-internal concerns.
- **Built-in C# CLI behavior**: The existing Spectre.Console CLI remains a separate implementation. This TUI does not replace it; both share the same Wire Protocol surface.
- **Frontend visual design details**: Specific glyphs, colors, spacing, and exact layout rendering belong to implementation and can evolve without changing this spec.

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

## 7. Interaction Surface

### 7.1 Information Regions

The terminal interaction model contains two logical regions:

| Region | Responsibility |
|--------|----------------|
| Transcript region | Shows turn history plus in-flight turn updates (agent output, tool activity, plan snapshots, and subagent progress). |
| Composer region | Handles drafting and submission, context hints, queued follow-up input, and global run/system status. |

This spec defines behavior and state transitions, not fixed pixel/row layout or specific widget implementation.

### 7.2 Startup to Ready Transition

- Startup state is shown immediately after terminal initialization.
- Startup state exits when initialization handshake is complete, or on first user interaction.
- After transition, transcript and composer interactions become available.

### 7.3 Resize and Compact Constraints

- Terminal resize must preserve draft input, transcript position, and active overlay state.
- On compact terminals, clients may reduce non-essential context rows, but must preserve:
  - input submission and editing;
  - interrupt discoverability during active turns;
  - transcript browsing controls.

### 7.4 Overlay Precedence

Overlays have strict input priority when present.

| Overlay type | Trigger |
|--------------|---------|
| Approval | `item/approval/request` |
| Thread/session picker | Slash commands that manage threads/sessions |
| Help | `/help`, `F1`, or `?` (scope per keybinding rules) |
| Notification | server-side job/result notifications |

When an overlay is active, base transcript/composer input must not be mutated unless the overlay explicitly delegates it.

---

## 8. Interaction Contracts

### 8.1 Startup and Ready States

During startup, the client must communicate:
- current connection progress;
- minimal onboarding hints (for example, how to access help and sessions).

Visual assets (logos, glyphs, color emphasis, animation style) are implementation details.

### 8.2 Context Hint Contract

The composer-adjacent hint area exposes state-sensitive guidance with deterministic priority:

| Priority (high → low) | Condition | Required hint intent |
|-----------------------|-----------|----------------------|
| 1 | Quit confirmation window active | Explain second-step exit action |
| 2 | Active turn + draft exists | Explain queue behavior |
| 3 | Idle + draft exists | Explain submit/newline behavior |
| 4 | Active turn + empty draft | Explain interrupt behavior |
| 5 | Idle + empty draft | Show help/mode-switch discoverability |

Connection and token context may be displayed when space allows, but behavior must not rely on their visibility.

### 8.3 Transcript Contract

Transcript must preserve chronological turn semantics and include:
- user messages;
- agent messages (markdown-capable content);
- tool activity and completion summaries;
- errors and system info;
- plan snapshots and subagent progress snapshots.

Behavioral requirements:
- a single authoritative busy signal for running/system state (no competing animated indicators);
- tool/subagent progress should be readable without requiring side panels;
- auto-scroll follows new content only while user is at bottom;
- explicit browse actions (`PageUp/PageDown/Home/End`, mouse wheel, browse-mode line keys) must consistently mutate the same scroll state.

### 8.4 Composer Contract

Composer must support:
- multi-line editing and submission;
- mode switching between Agent and Plan;
- input history recall semantics (`↑/↓` remain input-local under defined conditions);
- slash command entry and completion/dispatch;
- queued follow-up input during active turns;
- interrupt controls during active turns.

If both turn status and system status exist, status messaging may be merged, but interrupt discoverability must remain explicit.

### 8.5 Slash Commands

### 8.5 Slash Commands

Slash commands are typed in the InputEditor and processed locally (not sent to the agent).

| Command | Action |
|---------|--------|
| `/help` | Show help overlay with all commands and key bindings. |
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

### 9.1 Interaction State Model

The TUI has three interaction states:

| State | Receives key events | Behavior |
|-------|---------------------|----------|
| `InputEditor` | Default state. All printable keys, Enter, Backspace, and input-editing keys. | Draft editing and submission. Input context remains available in both `Idle` and active turn states. |
| `TranscriptBrowse` | Entered by explicit browse actions (`Esc`, `PageUp`, `PageDown`, `Home`, `End`) when no overlay is active. | Transcript navigation. `Enter`, `i`, or any printable input returns to `InputEditor`. |
| `Overlay` | Any modal overlay (`ApprovalOverlay`, `ThreadPicker`, `HelpOverlay`, etc.). | Overlay owns all input until dismissed/resolved. |

Additional constraints:

- `Running` is not a focus state. It must not implicitly remove input editing context.
- Caret visibility follows focus: when `InputEditor` is active and no overlay is shown, caret remains visible in both `Idle` and `Running`.
- Rendering note: clients may hide the terminal cursor while painting a frame and only re-show/reposition it at frame end to avoid visible cursor sweep/flicker.
- `Esc` behavior is stateful: interrupt active turn when applicable; otherwise it enters/exits transcript browsing.

### 9.1.1 Interaction State Matrix

| Key | `InputEditor` | `TranscriptBrowse` | `Overlay` |
|-----|---------------|--------------------|-----------|
| `Enter` | Submit draft (or newline with modifiers) | Return to `InputEditor` | Overlay-specific confirm |
| Printable char | Edit draft | Return to `InputEditor` and apply to draft | Overlay-specific input |
| `Esc` | Interrupt active turn if active; otherwise enter `TranscriptBrowse` | Exit browse to `InputEditor` | Overlay-specific cancel/close |
| `PageUp` / `PageDown` / `Home` / `End` | Enter/continue transcript browsing | Navigate transcript | Overlay-local or ignored |
| Mouse wheel | Scroll transcript and enter/continue browse | Scroll transcript | Overlay-local or ignored |
| `↑` / `↓` | Input-local behavior (history/caret rules) | Scroll transcript by line | Overlay-local navigation |
| `Tab` / `Shift+Tab` | Completion/queue and mode toggle | No state switch side effects | Overlay-local or ignored |

### 9.2 Global Key Bindings

These bindings are active regardless of focus:

| Key | Action |
|-----|--------|
| `Ctrl+C` | Interrupt running turn, or exit (double-press). |
| `Ctrl+D` | Exit the TUI (equivalent to `/quit`). |
| `Ctrl+L` | Redraw the terminal. |
| `Shift+Tab` | Toggle between Agent and Plan mode. |
| `PageUp` / `PageDown` / `Home` / `End` | Enter/continue transcript browsing when no overlay is active. |
| `F1` | Show HelpOverlay (global). |
| `?` (in transcript browse context) | Show HelpOverlay. |

`Tab` behavior in `InputEditor` is reserved for slash completion or queueing follow-up input during active turns (§8.4).

### 9.3 Terminal Compatibility

Crossterm handles cross-platform differences. The TUI detects terminal capabilities at startup:

- **Bracketed paste**: Enabled when supported. Allows multi-line paste without triggering Submit.
- **Kitty keyboard protocol**: Used when available for unambiguous modifier detection.
- **Mouse support**: Wheel scrolling is enabled for transcript navigation. Other mouse interactions remain out of scope for v1.

---

## 10. Approval Flow

When the server sends an `item/approval/request`, the TUI must:

1. Set `pending_approval` in `AppState`.
2. Show the `ApprovalOverlay` modal.
3. Block normal input until the user makes a decision.
4. Send the decision as a JSON-RPC response.

### 10.1 Approval Interaction Contract

The approval UI must show:
- approval type;
- operation summary;
- rationale/reason text when available;
- all currently permitted decisions.

Exact visual arrangement, icons, and highlighting style are implementation details.

### 10.2 Decision Key Bindings

| Key | Decision | Wire Value |
|-----|----------|------------|
| `a` or `Enter` | Accept | `"accept"` |
| `s` | Accept for session | `"acceptForSession"` |
| `!` | Accept always | `"acceptAlways"` |
| `d` | Decline | `"decline"` |
| `c` or `Esc` | Cancel turn | `"cancel"` |

Arrow keys move the selection highlight. `Enter` confirms the highlighted option.

### 10.3 Approval Type Mapping

| `approvalType` | Required label intent |
|----------------|-----------------------|
| `"shell"` | Shell command execution |
| `"file"` | File operation |

The `operation` field must be presented as human-readable details.

---

## 11. Presentation Customization

### 11.1 Configuration Sources

Presentation customization is loaded from (in priority order):

1. `--theme <path>` CLI argument.
2. `.craft/tui-theme.toml` in the workspace.
3. `~/.config/dotcraft/tui-theme.toml` (user-global).
4. Built-in default theme.

### 11.2 Customization Boundaries

This spec does not standardize concrete color tokens, icon sets, or typography choices.

Customization must not alter protocol or interaction semantics. In particular:
- keybinding behavior and interaction state transitions remain unchanged;
- warning/error/success states remain distinguishable;
- compact-mode fallback keeps essential interaction affordances available.

---

## 12. Crate Structure

### 12.1 Module Organization

The TUI crate is organized into five top-level modules:

| Module | Responsibility |
|--------|---------------|
| `wire` | Wire Protocol client layer: JSON-RPC 2.0 client with request/response correlation, transport abstraction (stdio and WebSocket), Wire DTO types, and error handling. |
| `app` | Application state and event handling: the `AppState` struct and its mutations, Wire notification → state mapping (§6), terminal event → state mapping, slash command parsing/dispatch, and token accounting. |
| `ui` | Rendering and interaction surface implementation: transcript/composer regions, overlays, and markdown-capable message presentation. |
| `theme` | Presentation customization loading and application. |
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