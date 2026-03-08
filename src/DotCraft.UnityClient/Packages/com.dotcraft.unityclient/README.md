# DotCraft Unity Client

DotCraft Unity Client is a Unity Editor extension that integrates the [DotCraft](https://github.com/DotCraftDev/DotCraft) AI agent into the Unity Editor via the [Agent Client Protocol (ACP)](https://agentclientprotocol.com/). It provides an in-editor chat interface with full file read/write and terminal capabilities.

**Minimum Unity version:** 2022.3

## Dependencies

Install `System.Text.Json 9.0.10` via NuGetForUnity:

1. Open **NuGet → Manage NuGet Packages**
2. Search for `System.Text.Json`
3. Select version `9.0.10` and press **Install**

## Installation

Add the package to your Unity project via the Package Manager using one of the following methods:

**Option A — Git URL**

In **Window → Package Manager**, click **+ → Add package from git URL** and enter:

```
https://github.com/DotCraftDev/DotCraft.git?path=src/DotCraft.UnityClient/Packages/com.dotcraft.unityclient
```

**Option B — Local path**

Clone the repository and add the package from disk: **+ → Add package from disk**, then select `src/DotCraft.UnityClient/Packages/com.dotcraft.unityclient/package.json`.

## Prerequisites

Before using the Unity Client, ensure DotCraft is installed and accessible from the command line:

```bash
# Build and install DotCraft (from the repo root)
build.bat
cd Release/DotCraft
powershell -File install_to_path.ps1
```

Configure the DotCraft global config (`~/.craft/config.json`) with your LLM API key:

```json
{
    "ApiKey": "sk-your-api-key",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1"
}
```

## Quick Start

1. Open the DotCraft window via **Tools → DotCraft Assistant**
2. Click **Connect** to launch the DotCraft process and establish an ACP session
3. Type a message in the input field and press **Enter** (or click **Send**)
4. Click **Stop** to cancel a running request at any time

## Built-in Unity Tools

DotCraft provides 4 read-only Unity tools to help the AI assistant understand your project state:

| Tool | Description |
|------|-------------|
| `unity_scene_query` | Query scene hierarchy with optional component details |
| `unity_get_selection` | Get currently selected objects in Unity Editor |
| `unity_get_console_logs` | Retrieve recent Unity Console log entries |
| `unity_get_project_info` | Get Unity version, project name, and packages |

These tools work out-of-the-box with no additional configuration, enabling the AI assistant to:
- Understand scene structure and object relationships
- Know what objects you're currently focused on
- View compilation errors and warnings
- Get project context information

## Extended Capabilities: SkillsForUnity

For full Unity manipulation capabilities (create, modify, delete GameObjects, execute menus, etc.), we recommend installing [SkillsForUnity](https://github.com/BestyAIGC/Unity-Skills).

SkillsForUnity provides 100+ Unity Editor skills including:
- GameObject management (create, delete, batch operations)
- Component operations (add, modify properties)
- Scene management (create, load, save, screenshots)
- Asset operations (find, import, manage)
- UI building, material/prefab management
- Editor control (play, stop, undo, menu execution)
- Advanced modules (Cinemachine, Terrain, Animator, NavMesh, Timeline)

### Installing SkillsForUnity

1. In **Window → Package Manager**, click **+ → Add package from git URL**
2. Enter: `https://github.com/BestyAIGC/Unity-Skills.git?path=SkillsForUnity`
3. After installation, start the HTTP server via **Window → UnitySkills → Start Server**

## Settings

Open **Edit → Project Settings → DotCraft** to configure the client.

Settings are stored in `UserSettings/DotCraftSettings.json` (per-user, excluded from version control).

| Setting | Default | Description |
|---------|---------|-------------|
| **Command** | `dotcraft` | Executable name or full path to the DotCraft binary |
| **Arguments** | `-acp` | Command-line arguments passed on launch |
| **Workspace Path** | *(empty)* | Working directory for DotCraft. Defaults to the Unity project root when left empty |
| **Environment Variables** | *(empty)* | Key-value pairs injected into the DotCraft process. Use this for API keys instead of modifying global config |
| **Auto Reconnect** | `true` | Automatically reconnect to the previous session after a Domain Reload |
| **Verbose Logging** | `false` | Print DotCraft stderr output to the Unity Console for debugging |
| **Request Timeout (s)** | `30` | Maximum wait time for an ACP request (5–120 s) |
| **Max History Messages** | `1000` | Maximum number of messages retained in the chat view |

### Configuring API Keys via Environment Variables

To avoid storing secrets in files that may be committed to version control, add your API key as an environment variable in the settings panel:

1. Open **Edit → Project Settings → DotCraft**
2. Under **Environment Variables**, click **+ Add Variable**
3. Set the key to `DOTCRAFT_API_KEY` (or the variable name your config references) and paste your API key as the value

## Editor Window

### Status Bar

The status bar at the top of the window shows the current connection state and provides the **Connect / Disconnect** button.

| State | Indicator color |
|-------|----------------|
| Disconnected | Red |
| Connecting | Yellow |
| Connected | Green |

### Mode and Model Selectors

When connected, dropdown menus appear in the toolbar to switch the active **Mode** (e.g., agent, ask) and **Model**. Changes take effect immediately for the current session.

### Session Management

The **Session** dropdown lists all available sessions for the current workspace. You can:

- Switch to an existing session by selecting it from the dropdown
- Start a new session by selecting **+ New Session** or clicking the **+** button

### Chat Panel

- Messages are displayed in the scrollable chat area in chronological order
- Agent responses support Markdown rendering
- Tool calls and their results are shown inline as collapsible entries

### Attaching Assets

Drag any Unity asset from the **Project** window onto the DotCraft window to attach it to the next message. Attached assets appear as tags above the input field; click **×** to remove an individual attachment before sending.

### Permission Approval

When DotCraft requests permission to perform a high-risk operation (e.g., executing a shell command or writing a file), an approval panel appears at the bottom of the window with three options:

| Button | Action |
|--------|--------|
| **Allow** | Approve this single request |
| **Allow Always** | Approve all future requests of the same kind for this session |
| **Reject** | Deny this request |

## Domain Reload Handling

When Unity triggers a Domain Reload (e.g., after recompiling scripts), the client automatically:

1. Saves the current session ID before the reload
2. Kills the DotCraft process cleanly
3. Restarts and reconnects to the same session after the reload (requires **Auto Reconnect** to be enabled)

## Workspace

By default the client uses the Unity project root (the folder containing the `Assets` directory) as the DotCraft workspace. Override this with a custom **Workspace Path** in Project Settings if you want DotCraft to operate in a different directory.

**The workspace must contain a `.craft/` directory before you connect.** The Unity Client checks for this directory when you open the DotCraft window and again when you click **Connect**. If the directory is missing, a red banner is shown in the window and the connection is blocked until the issue is resolved.

To create the `.craft/` directory, run `dotcraft` in the workspace directory (or perform any initial DotCraft run that creates it), then click **Retry** in the banner or click **Connect** again. See the [DotCraft Configuration Guide](https://github.com/DotCraftDev/DotCraft/blob/master/docs/en/config_guide.md) for details.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---------|-------------|-----|
| Red banner: ".craft directory not found" | `.craft/` folder missing from workspace | Run `dotcraft` in the workspace, then click **Retry** in the banner |
| "Failed to start DotCraft process" | `dotcraft` not found on PATH | Install DotCraft and add it to PATH, or set the full path in **Command** |
| Stuck at "Connecting…" | DotCraft crashed during startup | Enable **Verbose Logging** and check the Unity Console for stderr output |
| Window disconnects after every compile | **Auto Reconnect** disabled | Enable it in Project Settings |
| Permission panel never dismisses | Previous approval callback was lost during a Domain Reload | Disconnect and reconnect to start a fresh session |

## See Also

- [Unity Integration Guide](https://github.com/DotCraftDev/DotCraft/blob/master/docs/en/unity_guide.md) - Detailed integration documentation
- [ACP Mode Guide](https://github.com/DotCraftDev/DotCraft/blob/master/docs/en/acp_guide.md) - Agent Client Protocol details
- [SkillsForUnity](https://github.com/BestyAIGC/Unity-Skills) - Complete Unity operation skill library
