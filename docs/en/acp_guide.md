# DotCraft ACP Mode Guide

[Agent Client Protocol (ACP)](https://agentclientprotocol.com/) is an open protocol that standardizes how coding agents communicate with editors and IDEs — the same idea as LSP, but for AI agents. Any editor that implements ACP can host any ACP-compatible agent. DotCraft speaks ACP natively, which means it can run as a first-class coding assistant inside your editor without requiring a cloud subscription, a proprietary plugin, or any vendor-specific setup.

From the editor's perspective, communication happens over **stdio (standard input/output) using JSON-RPC 2.0**: the editor launches DotCraft as a subprocess and exchanges messages through its standard streams. Internally, the DotCraft ACP process acts as a **protocol bridge** between the editor (ACP protocol) and an AppServer instance (wire protocol). All session state, agent execution, and tool invocation are handled by AppServer — the same backend used by CLI, TUI, and Desktop clients. The bridge either starts a local AppServer subprocess automatically, or connects to a remote AppServer you specify.

## Supported Editors

ACP is an open standard and its ecosystem is growing. DotCraft has been verified to work in:

| Editor | Plugin / Integration |
|--------|----------------------|
| **JetBrains Rider** (and other JetBrains IDEs) | Built-in AI Assistant agent support |
| **Obsidian** | [obsidian-agent-client](https://github.com/RAIT-09/obsidian-agent-client) |

Any other editor or tool with ACP support can integrate DotCraft using the same configuration pattern.

## Quick Start

### 1. Initialize the DotCraft workspace

Before connecting an editor, run DotCraft once in the project directory from a terminal. This creates the `.craft/` folder with default configuration files and built-in commands:

```bash
cd <your-project-directory>
dotcraft
```

DotCraft will initialize the workspace and enter CLI mode. You can exit immediately — the workspace is ready. See the [Configuration Guide](./config_guide.md) for details on configuring the model and other options.

### 2. Configure ACP in your editor

In your editor's agent settings, set the **command** to `dotcraft` and add `-acp` as an **argument**. DotCraft automatically activates ACP mode when launched with the `-acp` flag — no changes to the config file are required.

The **working directory** should be set to the project root you initialized in step 1.

#### Remote workspace (optional)

If you already have a DotCraft AppServer running (e.g. started via `dotcraft app-server` or the Desktop app), you can point the ACP bridge at it instead of spawning a new subprocess:

```
dotcraft -acp --remote ws://<host>:<port>/ws
```

Add `--token <token>` if the AppServer requires authentication. When connected to a remote AppServer, sessions created through the editor are visible to all other connected clients (CLI, TUI, Desktop) in real time.

---

## JetBrains Rider (and JetBrains IDEs)

JetBrains IDEs with the AI Assistant plugin support ACP agents directly. Open **AIChat - Add Custom Agents** and add the following configuration:

```json
{
    "agent_servers": {
        "DotCraft": {
            "command": "dotcraft",
            "args": ["-acp"]
        }
    }
}
```

After saving, select DotCraft from the agent picker in the AI chat panel. The IDE handles process lifecycle — DotCraft starts when you open a session and stops when you close it.

---

## Obsidian

Install the [obsidian-agent-client](https://github.com/RAIT-09/obsidian-agent-client) plugin (via BRAT or manual installation), then open its settings and add a Custom agent:

| Field | Value |
|-------|-------|
| **AgentID** | DotCraft |
| **Display name** | DotCraft |
| **Path** | `dotcraft.exe` |
| **Arguments** | `-acp` |

Once configured, DotCraft appears as an agent in the plugin's chat interface. It can answer questions and read or write notes directly — the same agent that helps you code can also help you organize your knowledge base.

---

## How It Works

When the editor launches DotCraft in ACP mode, the following sequence takes place:

1. **Initialization** — The editor and the ACP bridge exchange protocol versions and capability declarations (`initialize`). The bridge then connects to the AppServer (spawning a local subprocess if no `--remote` is given) and forwards the handshake over the wire protocol.
2. **Session creation** — The editor creates a new session (`session/new`); the bridge forwards the request to AppServer, which creates the session, then relays the server's response (available slash commands, config options, etc.) back to the editor UI.
3. **Prompt exchange** — The editor sends user messages (`session/prompt`); AppServer runs the agent and streams back replies, tool call statuses, and results. The bridge relays these as `session/update` notifications to the editor.
4. **Permission requests** — Before executing file writes or shell commands, AppServer issues an approval request over the wire protocol; the bridge translates it into a `requestPermission` ACP message for the editor to surface to the user.
5. **File and terminal access** — When AppServer needs editor-native file or terminal access, it routes the call through the bridge back to the editor (`fs/readTextFile`, `fs/writeTextFile`, `terminal/*`), all through the editor's own APIs.

This means DotCraft can read unsaved buffer contents, show diffs inline before applying changes, and run commands in an editor-managed terminal — capabilities that go beyond what a plain CLI agent can offer. At the same time, all agent state is fully managed by AppServer, so sessions persist and are accessible from other clients even after the editor session ends.

## Supported Protocol Features

| Feature | Description |
|---------|-------------|
| `initialize` | Protocol version negotiation and capability exchange |
| `session/new` | Create a new session |
| `session/load` | Load an existing session and replay history |
| `session/list` | List all ACP sessions |
| `session/prompt` | Send a prompt and receive streaming replies |
| `session/update` | DotCraft pushes message chunks and tool call status to the editor |
| `session/cancel` | Cancel an in-progress operation |
| `requestPermission` | DotCraft requests execution permission for sensitive operations |
| `fs/readTextFile` | Read files through the editor, including unsaved changes |
| `fs/writeTextFile` | Write files through the editor with diff preview |
| `terminal/*` | Create and manage terminals through the editor |
| Slash Commands | Custom commands (from `.craft/commands/`) are broadcast to the editor UI |
| Config Options | Expose selectable configuration (mode, model, etc.) to the editor |

## Session & Workspace Behavior

Because ACP is now a full AppServer client, sessions created through an editor are first-class citizens in the same session store as all other channels:

- **Session ID format**: `acp_{sessionId}` (the session ID is assigned by the editor and forwarded to AppServer)
- **Session storage**: stored in `<workspace>/.craft/sessions/` alongside sessions from CLI, TUI, Desktop, and bot channels
- **Shared memory**: `memory/MEMORY.md` and `memory/HISTORY.md` are shared across all channels in the same workspace — knowledge acquired in an ACP session is accessible from a CLI or QQ bot session in the same workspace, and vice versa
- **Multi-client access**: when using `--remote`, multiple clients can connect to the same AppServer simultaneously. An ACP session started in Obsidian can be resumed or monitored from the Desktop application in real time

## Further Reading

- [Configuration Guide — ACP Mode Configuration](./config_guide.md#acp-mode-configuration) — full config reference
- [AppServer Guide](./appserver_guide.md) — running DotCraft as a headless server and connecting remote clients
- [ACP Protocol Specification](https://agentclientprotocol.com/get-started/introduction) — official protocol documentation
