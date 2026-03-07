# DotCraft Architecture Reference Index

This document provides an index of key architecture files and documentation for quick reference.

## Core Architecture Files

### Module System
- **Interface**: `src/DotCraft.Core/Abstractions/IDotBotModule.cs` - Module interface definition
- **Registry**: `src/DotCraft.Core/Modules/ModuleRegistry.cs` - Module registration and management
- **Base Class**: `src/DotCraft.Core/Modules/ModuleBase.cs` - Base class for modules
- **Example Modules**:
  - QQ: `src/DotCraft.QQ/QQModule.cs` - Complete channel module example
  - Unity: `src/DotCraft.Unity/UnityModule.cs` - Tool-only module example
  - WeCom: `src/DotCraft.WeCom/WeComModule.cs` - Another channel module example

### Core Components
- **Agent Factory**: `src/DotCraft.Core/Agents/AgentFactory.cs` - Agent creation and configuration
- **Agent Runner**: `src/DotCraft.Core/Agents/AgentRunner.cs` - Agent execution logic
- **Session Store**: `src/DotCraft.Core/Sessions/SessionStore.cs` - Session persistence
- **Memory Store**: `src/DotCraft.Core/Memory/` - Memory system (MEMORY.md + HISTORY.md)

### Tool System
- **Core Tools**: `src/DotCraft.Core/Tools/` - File, Shell, Web tools
- **Tool Provider Interface**: `src/DotCraft.Core/Abstractions/IAgentToolProvider.cs`
- **Example Tool Provider**: `src/DotCraft.QQ/Tools/QQToolProvider.cs`

### Security
- **Approval Service**: `src/DotCraft.Core/Security/IApprovalService.cs` - Approval interface
- **Example Approval**: `src/DotCraft.QQ/Services/QQApprovalService.cs` - QQ approval implementation
- **Path Blacklist**: `src/DotCraft.Core/Security/PathBlacklist.cs`

### Hooks System
- **Hook Runner**: `src/DotCraft.Core/Hooks/HookRunner.cs` - Hook execution engine
- **Hook Types**: `src/DotCraft.Core/Hooks/HookEvent.cs` - Event type definitions
- **Documentation**: `docs/hooks_guide.md` - Complete hooks guide

## Configuration

- **App Config**: `src/DotCraft.Core/Configuration/AppConfig.cs` - Configuration model
- **Module Context**: `src/DotCraft.Core/Configuration/ModuleContext.cs` - Module initialization context
- **Config Guide**: `docs/config_guide.md` - Configuration documentation

## Documentation Files

### User Guides
- **Index**: `docs/index.md` - Documentation navigation
- **Configuration**: `docs/config_guide.md` - Tools, security, MCP, Gateway
- **API Mode**: `docs/api_guide.md` - OpenAI-compatible API
- **QQ Bot**: `docs/qq_bot_guide.md` - NapCat/OneBot V11 integration
- **WeCom**: `docs/wecom_guide.md` - WeChat Work integration
- **ACP**: `docs/acp_guide.md` - Editor/IDE integration
- **Unity**: `docs/unity_guide.md` - Unity Editor integration
- **Hooks**: `docs/hooks_guide.md` - Lifecycle hooks
- **Dashboard**: `docs/dash_board_guide.md` - Web debugging UI

### Sample Code
- **Hooks**: `samples/hooks/` - Hook configuration examples (Windows/Linux)
- **Bootstrap**: `samples/bootstrap/` - Workspace initialization examples
- **Python**: `samples/python/` - Python integration examples

## Quick Reference by Task

### Creating a New Channel Module
1. Study: `src/DotCraft.QQ/QQModule.cs` (full example)
2. Interface: `src/DotCraft.Core/Abstractions/IDotBotModule.cs`
3. Base: `src/DotCraft.Core/Modules/ModuleBase.cs`
4. Service: Implement `IChannelService` (see `src/DotCraft.QQ/Services/QQChannelService.cs`)

### Creating a Tool-Only Module
1. Study: `src/DotCraft.Unity/UnityModule.cs` (minimal example)
2. Tools: `src/DotCraft.Core/Tools/FileTools.cs` (core tool pattern)
3. Provider: Implement `IAgentToolProvider`

### Adding New Tools
1. Pattern: `src/DotCraft.Core/Tools/FileTools.cs` - Example with `[Tool]` attributes
2. Provider: Create tool provider (see `src/DotCraft.QQ/Tools/QQToolProvider.cs`)

### Working with Sessions
1. Store: `src/DotCraft.Core/Sessions/SessionStore.cs`
2. Runner: `src/DotCraft.Core/Agents/AgentRunner.cs` - Session lifecycle usage

### Implementing Approval
1. Interface: `src/DotCraft.Core/Security/IApprovalService.cs`
2. Example: `src/DotCraft.QQ/Services/QQApprovalService.cs`

### Understanding Hooks
1. Runner: `src/DotCraft.Core/Hooks/HookRunner.cs`
2. Events: `src/DotCraft.Core/Hooks/HookEvent.cs`
3. Docs: `docs/hooks_guide.md`
4. Samples: `samples/hooks/`

## Search Patterns

When looking for specific patterns in the codebase:

- **Module implementations**: Search for `[DotCraftModule` attribute
- **Tool definitions**: Search for `[Tool(` attribute  
- **Service registration**: Search for `ConfigureServices` method
- **Approval handling**: Search for `IApprovalService` implementations
- **Hook usage**: Search for `HookRunner.RunAsync`

## Architecture Decisions

For understanding the "why" behind architectural choices:

- **README**: `README.md` (English) and `README_ZH.md` (Chinese) - Project overview and design philosophy
- **Docs**: `docs/config_guide.md` - Configuration design decisions
- **Docs**: `docs/hooks_guide.md` - Hooks design rationale

## When to Read What

- **Starting a new module**: Read `src/DotCraft.QQ/QQModule.cs` end-to-end
- **Understanding tool system**: Read `src/DotCraft.Core/Tools/FileTools.cs`
- **Learning session management**: Read `src/DotCraft.Core/Agents/AgentRunner.cs`
- **Implementing approval**: Read `src/DotCraft.QQ/Services/QQApprovalService.cs`
- **Adding hooks**: Read `docs/hooks_guide.md` and `samples/hooks/`
- **Configuration questions**: Read `docs/config_guide.md`
