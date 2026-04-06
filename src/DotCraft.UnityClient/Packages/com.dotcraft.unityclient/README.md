# DotCraft Unity Client

DotCraft Unity Client is a Unity Editor extension that integrates the [DotCraft](https://github.com/DotHarness/DotCraft) AI agent into the Unity Editor via the [Agent Client Protocol (ACP)](https://agentclientprotocol.com/). It provides an in-editor chat interface with full file read/write and terminal capabilities.

**Minimum Unity version:** 2022.3

## Dependencies

Install `System.Text.Json 9.0.10` via NuGetForUnity:

1. Open **NuGet → Manage NuGet Packages**
2. Search for `System.Text.Json`
3. Select version `9.0.10` and press **Install**

## Installation

Add the package to your Unity project via the Package Manager:

**Option A — Git URL** (recommended)

```
https://github.com/DotHarness/DotCraft.git?path=src/DotCraft.UnityClient/Packages/com.dotcraft.unityclient
```

**Option B — Local path**

Clone the repository and add from disk: **+ → Add package from disk**, then select `src/DotCraft.UnityClient/Packages/com.dotcraft.unityclient/package.json`.

## Quick Start

1. Open the DotCraft window via **Tools → DotCraft Assistant**
2. Click **Connect** to launch the DotCraft process
3. Start chatting with the AI assistant

For detailed configuration, troubleshooting, and usage guides, see the [Unity Integration Guide](https://github.com/DotHarness/DotCraft/blob/master/docs/en/unity_guide.md).

## Extended Capabilities

### Built-in Unity Tools

DotCraft provides 4 read-only Unity tools out-of-the-box:

| Tool | Description |
|------|-------------|
| `unity_scene_query` | Query scene hierarchy |
| `unity_get_selection` | Get selected objects |
| `unity_get_console_logs` | Retrieve console logs |
| `unity_get_project_info` | Get project information |

### SkillsForUnity

For full Unity manipulation capabilities (100+ skills), install [SkillsForUnity](https://github.com/BestyAIGC/Unity-Skills):

```
https://github.com/BestyAIGC/Unity-Skills.git?path=SkillsForUnity
```

### unity-mcp

For MCP protocol compatible Unity integration (30+ tools), install [unity-mcp](https://github.com/CoplayDev/unity-mcp):

```
https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main
```

## See Also

- [Unity Integration Guide](https://github.com/DotHarness/DotCraft/blob/master/docs/en/unity_guide.md) - Detailed documentation
- [ACP Mode Guide](https://github.com/DotHarness/DotCraft/blob/master/docs/en/acp_guide.md) - Protocol details
