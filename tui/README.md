# DotCraft TUI

**[中文](./README_ZH.md) | English**

Rust-native terminal interface for DotCraft, built on [Ratatui](https://ratatui.rs/). Connects to the DotCraft AppServer over the Wire Protocol (JSON-RPC) and provides a full-featured AI Agent interaction experience in the terminal.

> **Design Inspiration**: The TUI interface design is inspired by [OpenAI Codex CLI](https://github.com/openai/codex), an excellent open-source terminal AI agent.

## Features

| Feature | Description |
|---------|-------------|
| **Streaming output** | Agent messages render incrementally with Markdown support (syntax-highlighted code blocks, tables, headings) |
| **Tool call display** | `• Called ReadFile("src/main.rs") (0.3s)` format with elapsed time and result preview |
| **StatusIndicator** | Shows `⠋ Working (Ns · esc to interrupt)` with shimmer animation while a turn is running |
| **FooterLine** | Single contextual row: hints, mode indicator, token usage, connection status |
| **WelcomeScreen** | Startup screen with ASCII logo (size-gated), workspace path, and connection state |
| **Inline SubAgent progress** | Live SubAgent status rendered inline; collapses to a summary when all complete |
| **Inline Plan view** | Agent todo list rendered inline in the chat flow |
| **Session management** | `/sessions` opens the session picker (resume / archive / delete) |
| **Approval flow** | `ApprovalOverlay` for tool calls that require human approval |
| **i18n** | Built-in Chinese / English (`--lang zh` / `--lang en`) |
| **Theme customization** | TOML-based color overrides |
| **Clipboard** | `y` key copies the last agent message (requires `clipboard` feature) |
| **WebSocket mode** | Connect to a remote AppServer (requires `websocket` feature) |

## Building

**Prerequisites**: Rust stable toolchain — install via [rustup](https://rustup.rs/).

```bash
# Enter the tui directory
cd tui

# Standard build (includes WebSocket support)
cargo build --release

# Without WebSocket (stdio-only mode)
cargo build --release --no-default-features

# With system clipboard support
cargo build --release --features clipboard
```

Output binary: `target/release/dotcraft-tui` (Windows: `dotcraft-tui.exe`).

## Launching

### Mode 1: Subprocess mode (default)

The TUI spawns `dotcraft` (or the binary given by `--server-bin`) as an AppServer child process and communicates over stdio.

```bash
# Launch in the current project directory (dotcraft must be on PATH)
dotcraft-tui

# Specify workspace path
dotcraft-tui --workspace /path/to/project

# Specify the dotcraft binary path
dotcraft-tui --server-bin /usr/local/bin/dotcraft

# Via environment variable
DOTCRAFT_BIN=/usr/local/bin/dotcraft dotcraft-tui
```

### Mode 2: Remote WebSocket mode

Connect to a running AppServer (requires `websocket` feature).

```bash
# Connect to a local AppServer
dotcraft-tui --remote ws://localhost:3000/ws

# Connect to a remote AppServer with authentication
dotcraft-tui --remote "ws://host:3000/ws?token=your-secret"

# With explicit workspace path
dotcraft-tui --remote ws://host:3000/ws --workspace /path/to/project
```

Starting the AppServer for remote mode:

```bash
# Start AppServer in WebSocket mode
dotcraft app-server --listen ws://0.0.0.0:3000
```

### Language and Theme

```bash
# Chinese UI (default)
dotcraft-tui --lang zh

# English UI
dotcraft-tui --lang en

# Custom theme
dotcraft-tui --theme /path/to/theme.toml
```

### CLI Reference

| Flag | Description | Default |
|------|-------------|---------|
| `--remote <URL>` | Connect to a remote AppServer (WebSocket URL) | — |
| `--server-bin <PATH>` | AppServer binary path | `dotcraft` |
| `--workspace <PATH>` | Workspace directory path | current directory |
| `--theme <PATH>` | Custom theme TOML file path | built-in dark theme |
| `--lang <LANG>` | UI language (`zh` / `en`) | `zh` |

## Key Bindings

| Key | Action |
|-----|--------|
| `Enter` | Send message |
| `Shift+Enter` | Insert newline in input |
| `Tab` | While running: queue message; idle: slash command completion |
| `Ctrl+C` | While running: interrupt agent; idle: first press flags quit, second press exits |
| `Shift+Tab` | Toggle Agent / Plan mode |
| `↑` / `↓` | Empty input: navigate history; with content: scroll chat |
| `PageUp` / `PageDown` | Scroll chat by page |
| `Home` / `End` | Jump to top / bottom of chat |
| `?` or `/help` | Open key binding help overlay |
| `y` | Copy last agent message to clipboard (requires `clipboard` feature) |
| `s` | When SubAgents are done: toggle detail / collapsed view |
| `Ctrl+L` | Force terminal redraw |

## Slash Commands

| Command | Description |
|---------|-------------|
| `/help` | Show key binding help overlay |
| `/sessions` | Open session manager |
| `/new` | Start a new session |
| `/clear` | Clear current conversation history |
| `/load <thread-id>` | Load a specific session by ID |
| `/agent` | Switch to Agent mode |
| `/plan` | Switch to Plan mode |
| `/cron` | List cron jobs |
| `/quit` | Exit the TUI |

## Theme Configuration

In the TOML file passed to `--theme` (colors accept Ratatui color names or `#RRGGBB`):

```toml
[colors]
brand = "#7C3AED"           # brand color (logo, mode indicator)
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
status_indicator = "yellow" # "Working" label and spinner color

[footer]
foreground = "dark_gray"    # footer hint text color
context_color = "dark_gray" # token counts and connection status color

[code]
syntect_theme = "base16-ocean.dark"  # code block syntax highlight theme
```

## Logging

Set `DOTCRAFT_TUI_LOG` to enable log output (logs are written to stderr):

```bash
DOTCRAFT_TUI_LOG=debug dotcraft-tui 2>tui.log
```
