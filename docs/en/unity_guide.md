# Unity Integration Guide

DotCraft provides seamless integration with the Unity Editor through the Agent Client Protocol (ACP). This guide covers installation, configuration, and usage of the Unity integration.

## Architecture

The Unity integration consists of two components:

1. **Server-side Module** (`DotCraft.Unity`): A DotCraft module that provides 13 specialized tools for Unity Editor operations
2. **Unity Client Package** (`com.dotcraft.unityclient`): A Unity Editor extension with in-editor chat interface

```
┌─────────────────────┐         ACP Protocol         ┌─────────────────────┐
│   DotCraft Server   │◄────────────────────────────►│   Unity Editor      │
│                     │      _unity/* extensions      │                     │
│  - UnityModule      │                               │  - Editor Window    │
│  - Tool Provider    │                               │  - Protocol Client  │
└─────────────────────┘                               └─────────────────────┘
```

## Prerequisites

- Unity 2022.3 or later
- DotCraft installed and accessible from command line
- [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity) package manager
- `System.Text.Json 9.0.10` (installed via NuGetForUnity)

## Installation

### Step 1: Install NuGetForUnity

If you haven't installed NuGetForUnity yet:

1. Open **Window → Package Manager** in Unity
2. Click **+ → Add package from git URL**
3. Enter: `https://github.com/GlitchEnzo/NuGetForUnity.git`

### Step 2: Install System.Text.Json

1. Open **NuGet → Manage NuGet Packages** in Unity
2. Search for `System.Text.Json`
3. Select version `9.0.10` and click **Install**

### Step 3: Install DotCraft Unity Client Package

**Option A — Git URL** (recommended):

1. Open **Window → Package Manager** in Unity
2. Click **+ → Add package from git URL**
3. Enter:
   ```
   https://github.com/DotCraftDev/DotCraft.git?path=src/DotCraft.UnityClient/Packages/com.dotcraft.unityclient
   ```

**Option B — Local path**:

1. Clone the DotCraft repository
2. In Unity, open **Window → Package Manager**
3. Click **+ → Add package from disk**
4. Navigate to `src/DotCraft.UnityClient/Packages/com.dotcraft.unityclient/package.json` and select it

### Step 4: Configure DotCraft

Ensure DotCraft is installed and configured with your LLM API key in `~/.craft/config.json`:

```json
{
    "ApiKey": "sk-your-api-key",
    "Model": "gpt-4o-mini",
    "EndPoint": "https://api.openai.com/v1"
}
```

## Quick Start

1. Open the DotCraft window via **Tools → DotCraft Assistant** in Unity
2. Click **Connect** to launch the DotCraft process and establish an ACP session
3. Type a message in the input field and press **Enter** (or click **Send**)
4. The AI assistant can now interact with your Unity project

## Editor Window

### Status Bar

The status bar shows the current connection state:

| State | Indicator Color |
|-------|----------------|
| Disconnected | Red |
| Connecting | Yellow |
| Connected | Green |

### Mode and Model Selectors

When connected, dropdown menus allow you to switch:
- **Mode**: Different agent modes (e.g., agent, ask)
- **Model**: Active LLM model for the session

### Session Management

- Switch between existing sessions via the **Session** dropdown
- Start a new session by selecting **+ New Session** or clicking the **+** button

### Chat Panel

- Messages display in chronological order
- Agent responses support Markdown rendering
- Tool calls and results appear as collapsible inline entries

### Attaching Assets

Drag any Unity asset from the **Project** window onto the DotCraft window to attach it to your message. Attached assets appear as tags above the input field.

## Configuration

Open **Edit → Project Settings → DotCraft** to configure:

| Setting | Default | Description |
|---------|---------|-------------|
| **Command** | `dotcraft` | Executable name or full path to DotCraft |
| **Arguments** | `-acp` | Command-line arguments for DotCraft |
| **Workspace Path** | *(empty)* | Working directory (defaults to Unity project root) |
| **Environment Variables** | *(empty)* | Key-value pairs for the DotCraft process |
| **Auto Reconnect** | `true` | Automatically reconnect after Domain Reload |
| **Verbose Logging** | `false` | Print DotCraft stderr to Unity Console |
| **Request Timeout (s)** | `30` | Maximum wait time for ACP requests (5–120 s) |
| **Max History Messages** | `1000` | Maximum messages in chat view |

### Configuring API Keys via Environment Variables

To avoid storing secrets in version control:

1. Open **Edit → Project Settings → DotCraft**
2. Under **Environment Variables**, click **+ Add Variable**
3. Set key to `DOTCRAFT_API_KEY` and paste your API key as the value

## Available Tools

DotCraft provides 13 Unity-specific tools when connected:

### Scene Tools

| Tool | Description |
|------|-------------|
| `unity_scene_query` | Query scene hierarchy with optional component details |
| `unity_get_selection` | Get currently selected objects in Unity Editor |
| `unity_set_selection` | Set selection by object paths |
| `unity_create_gameobject` | Create a new GameObject with optional components |
| `unity_modify_component` | Modify properties of a component on a GameObject |
| `unity_delete_gameobject` | Delete a GameObject from the scene |

### Console Tools

| Tool | Description |
|------|-------------|
| `unity_get_console_logs` | Retrieve recent Unity Console log entries |

### Editor Tools

| Tool | Description |
|------|-------------|
| `unity_execute_menu_item` | Execute a Unity Editor menu item by path |
| `unity_get_project_info` | Get Unity version, project name, and packages |

### Asset Tools

| Tool | Description |
|------|-------------|
| `unity_get_asset_info` | Get metadata about an asset (type, dependencies, settings) |
| `unity_import_asset` | Trigger AssetDatabase.ImportAsset for an asset |
| `unity_find_assets` | Search for assets using AssetDatabase.FindAssets |

## Permission Approval

When DotCraft requests permission for high-risk operations, an approval panel appears with three options:

| Button | Action |
|--------|--------|
| **Allow** | Approve this single request |
| **Allow Always** | Approve all similar requests for this session |
| **Reject** | Deny this request |

## Domain Reload Handling

When Unity triggers a Domain Reload (e.g., after script compilation), the client:

1. Saves the current session ID
2. Kills the DotCraft process cleanly
3. Restarts and reconnects to the same session (if **Auto Reconnect** is enabled)

## Workspace

By default, the Unity project root (folder containing `Assets/`) is the DotCraft workspace. The workspace must contain a `.craft/` folder with a valid configuration.

Override the workspace path in **Edit → Project Settings → DotCraft** if needed.

## Example Usage

### Query Scene Hierarchy

```
User: Show me all GameObjects in the scene
AI: [Uses unity_scene_query tool]
    Found 15 GameObjects:
    - Main Camera
    - Directional Light
    - Canvas
    ...
```

### Create GameObject

```
User: Create a cube at position (5, 2, 3) with a Rigidbody component
AI: [Uses unity_create_gameobject tool]
    Created GameObject "Cube" at /Cube with InstanceId 12345
```

### Search Assets

```
User: Find all prefab assets in the project
AI: [Uses unity_find_assets tool with filter "t:Prefab"]
    Found 8 prefab assets:
    - Assets/Prefabs/Player.prefab
    - Assets/Prefabs/Enemy.prefab
    ...
```

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---------|-------------|-----|
| "Failed to start DotCraft process" | `dotcraft` not on PATH | Install DotCraft and add to PATH, or set full path in **Command** |
| Stuck at "Connecting…" | DotCraft crashed during startup | Enable **Verbose Logging** and check Unity Console for stderr |
| Window disconnects after every compile | **Auto Reconnect** disabled | Enable **Auto Reconnect** in Project Settings |
| Permission panel never dismisses | Previous callback lost during Domain Reload | Disconnect and reconnect for a fresh session |
| Tools not available | ACP client doesn't advertise `_unity` extension | Ensure DotCraft server module is loaded (ACP mode enabled) |

## Tips

- Use **Verbose Logging** during initial setup to diagnose connection issues
- Configure environment variables for API keys instead of modifying global config
- The workspace path can be set to a parent directory to share memory across multiple Unity projects
- Tool calls that modify the scene will trigger Unity's undo system, allowing you to revert changes

## See Also

- [Configuration Guide](./config_guide.md) - DotCraft configuration options
- [ACP Mode Guide](./acp_guide.md) - Agent Client Protocol details
- [Unity Client README](https://github.com/DotCraftDev/DotCraft/tree/master/src/DotCraft.UnityClient/Packages/com.dotcraft.unityclient) - Package documentation
