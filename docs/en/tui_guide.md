# DotCraft TUI Guide

TUI is DotCraft's Rust-native terminal interface for users who want a full interaction experience in the terminal. It connects through the AppServer Wire Protocol and reuses the same workspace, session, and approval capabilities.

## Quick Start

### Build

```bash
cd tui
cargo build --release
```

The output binary is `target/release/dotcraft-tui`, or `dotcraft-tui.exe` on Windows.

### Local Subprocess Mode

Start from a project directory:

```bash
dotcraft-tui
```

TUI starts `dotcraft` as an AppServer child process and communicates over stdio.

### Specify Workspace or Binary

```bash
dotcraft-tui --workspace /path/to/project
dotcraft-tui --server-bin /usr/local/bin/dotcraft
DOTCRAFT_BIN=/usr/local/bin/dotcraft dotcraft-tui
```

## Configuration

| Argument | Description |
|----------|-------------|
| `--workspace` | Workspace directory |
| `--server-bin` | `dotcraft` / AppServer binary path |
| `--remote` | Connect to an existing WebSocket AppServer |
| `--lang zh|en` | UI language |
| `--theme` | Custom TOML theme |

Remote WebSocket example:

```bash
dotcraft app-server --listen ws://127.0.0.1:9100
dotcraft-tui --remote ws://127.0.0.1:9100/ws
```

## Usage Examples

| Scenario | Command |
|----------|---------|
| Use the current project | `dotcraft-tui` |
| Specify a project folder | `dotcraft-tui --workspace /path/to/project` |
| Connect to a remote service | `dotcraft-tui --remote ws://host:9100/ws` |
| English UI | `dotcraft-tui --lang en` |

Common slash commands include `/new`, `/compact`, `/clear`, and `/quit`. See `tui/README.md` in the repository for full key bindings and theme details.

## Advanced Topics

- Default mode is best for local use; remote mode is best for shared AppServer or multiple clients.
- Enable logs with `DOTCRAFT_TUI_LOG=debug dotcraft-tui 2>tui.log`.
- Build system clipboard support with `cargo build --release --features clipboard`.

## Troubleshooting

### TUI cannot find `dotcraft`

Put `dotcraft` on `PATH`, or use `--server-bin` / `DOTCRAFT_BIN` to specify the binary path.

### Remote connection fails

Confirm AppServer is running in WebSocket mode and the client URL includes `/ws`. Authenticated services also need a token.

### Terminal rendering looks wrong

Use a modern terminal with Unicode and color support, and make sure the terminal size is large enough. Test with the default theme first.
