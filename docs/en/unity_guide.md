# Unity Integration Guide

DotCraft provides seamless integration with the Unity Editor through the Agent Client Protocol (ACP). This guide covers installation, configuration, and usage of the Unity integration.

## Architecture

The Unity integration consists of two components:

1. **Server-side Module** (`DotCraft.Unity`): A DotCraft module that provides 4 read-only tools for understanding Unity project state
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
   https://github.com/DotHarness/dotcraft.git?path=src/DotCraft.UnityClient/Packages/com.dotcraft.unityclient
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
| **Enable Builtin Unity Tools** | `true` | Enable built-in `_unity/*` extension methods. Disable when using external Unity integration |

### Configuring API Keys via Environment Variables

To avoid storing secrets in version control:

1. Open **Edit → Project Settings → DotCraft**
2. Under **Environment Variables**, click **+ Add Variable**
3. Set key to `DOTCRAFT_API_KEY` and paste your API key as the value

## Built-in Tools

DotCraft provides 4 Unity read-only tools to help the AI assistant understand project state:

### Scene Tools

| Tool | Description |
|------|-------------|
| `unity_scene_query` | Query scene hierarchy with optional component details |
| `unity_get_selection` | Get currently selected objects in Unity Editor |

### Console Tools

| Tool | Description |
|------|-------------|
| `unity_get_console_logs` | Retrieve recent Unity Console log entries |

### Project Tools

| Tool | Description |
|------|-------------|
| `unity_get_project_info` | Get Unity version, project name, and packages |

These read-only tools work out-of-the-box with no additional configuration, enabling the AI assistant to:
- Understand scene structure and object relationships
- Know what objects the user is currently focused on
- View compilation errors and warnings
- Get project context information

## Extended Capabilities: SkillsForUnity

For full Unity manipulation capabilities (create, modify, delete GameObjects, execute menus, etc.), we recommend installing the [SkillsForUnity](https://github.com/BestyAIGC/Unity-Skills) plugin.

### SkillsForUnity Features

SkillsForUnity provides 100+ Unity Editor skills, including:

- **GameObject Management**: Create, delete, batch operations
- **Component Operations**: Add, modify properties, batch settings
- **Scene Management**: Create, load, save, screenshots
- **Asset Operations**: Find, import, manage
- **UI Building**: Create Canvas, buttons, layouts
- **Material/Prefab Management**
- **Editor Control**: Play, stop, undo, menu execution
- **Advanced Modules**: Cinemachine, Terrain, Animator, NavMesh, Timeline

### Installing SkillsForUnity

1. Open **Window → Package Manager** in Unity
2. Click **+ → Add package from git URL**
3. Enter: `https://github.com/BestyAIGC/Unity-Skills.git?path=SkillsForUnity`
4. After installation, start the HTTP server via **Window → UnitySkills → Start Server**
5. Install skill descriptions via **Window → UnitySkills → Install to Claude Code**

### Architecture Comparison

| Feature | DotCraft Built-in Tools | SkillsForUnity | unity-mcp |
|---------|------------------------|----------------|-----------|
| **Installation** | Works out-of-the-box | Requires HTTP server startup | Requires Python + HTTP server |
| **Scope** | 4 read-only tools | 100+ skills | 30+ tools |
| **Communication** | ACP protocol (stdio) | HTTP REST API | MCP protocol (HTTP/stdio) |
| **Cross-IDE Support** | DotCraft only | Multiple IDEs | Multiple IDEs |
| **Use Case** | Understanding project state | Full Unity operations | Cross-platform Unity operations |

## Extended Capabilities: unity-mcp

[unity-mcp](https://github.com/CoplayDev/unity-mcp) is another Unity integration solution using the MCP (Model Context Protocol), supporting multiple AI IDEs.

### unity-mcp Features

unity-mcp provides 30+ Unity operation tools, including:

- **Scene Management**: Load, save, create, query hierarchy
- **GameObject Management**: Create, modify, transform, delete
- **Component Operations**: Add, remove, configure
- **Asset Management**: Create, modify, search
- **Material/Prefab/Script Management**
- **Batch Execution**: 10-100x faster batch operations
- **Console Reading**: Get Unity Console output
- **Test Running**: Run Unity tests

### Installing unity-mcp

**Prerequisites**:
- Unity 2021.3 LTS or later
- Python 3.10+ and [uv](https://docs.astral.sh/uv/) package manager

**Installation Steps**:

1. Open **Window → Package Manager** in Unity
2. Click **+ → Add package from git URL**
3. Enter: `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main`
4. Open **Window → MCP for Unity** and click **Start Server**
5. Select MCP client from dropdown and click **Configure**

![unity-mcp](https://github.com/DotHarness/resources/raw/master/dotcraft/unity-mcp.png)

### Recommended Usage

1. **Basic Usage**: DotCraft's built-in read-only tools meet daily project understanding needs
2. **Advanced Operations**: Install SkillsForUnity or unity-mcp for complete Unity Editor control
3. **Disable Built-in Tools**: If using external Unity integration, disable **Enable Builtin Unity Tools** in **Project Settings → DotCraft**
4. **Selection Guide**:
   - **SkillsForUnity**: Most feature-rich (100+ skills), ideal for deep Unity development
   - **unity-mcp**: MCP protocol compatible, ideal for cross-AI-IDE usage
   - **Built-in Tools**: Simplest option, ideal for quick start

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

### Get Console Logs

```
User: Check for compilation errors
AI: [Uses unity_get_console_logs tool with filter "error"]
    Found 2 errors:
    - Assets/Scripts/Player.cs(45): error CS0103: 'velocity' does not exist
    - Assets/Scripts/Enemy.cs(12): error CS0246: Type 'Navigation' not found
```

### Get Project Info

```
User: What Unity version is this project using?
AI: [Uses unity_get_project_info tool]
    Project info:
    - Unity version: 2022.3.15f1
    - Project name: MyGame
    - Installed packages: 12
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
- Install SkillsForUnity or unity-mcp for complete Unity Editor operation capabilities
- Disable built-in Unity tools in settings when using external Unity integration

## See Also

- [Configuration Guide](./config_guide.md) - DotCraft configuration options
- [ACP Mode Guide](./acp_guide.md) - Agent Client Protocol details
- [Unity Client README](https://github.com/DotHarness/dotcraft/tree/master/src/DotCraft.UnityClient/Packages/com.dotcraft.unityclient) - Package documentation
- [SkillsForUnity](https://github.com/BestyAIGC/Unity-Skills) - HTTP REST API based Unity skill library
- [unity-mcp](https://github.com/CoplayDev/unity-mcp) - MCP protocol based Unity integration tool
