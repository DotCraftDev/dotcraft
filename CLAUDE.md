# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

DotCraft is an Agent Harness crafting a persistent AI workspace around your project.

From Desktop, CLI, editors, chatbots, APIs — everywhere you work.

**Key Highlights**:
- **Project-First**: Sessions, memory, skills, and config live under `.craft/` and follow the project
- **Unified Session Core**: Desktop, CLI, editors and bots share one session model
- **Observable**: Built-in approvals, traces, Dashboard, and optional sandbox isolation

It is a .NET 10 / C# application with a modular architecture — multiple entry points (CLI, editors, bots, APIs, GitHub workflows) connect to the same workspace sharing sessions, memory, skills, and tools.

## Build & Run

**Prerequisites**: .NET 10 SDK (preview)

```bash
# Build the solution
dotnet build dotcraft.sln

# Build and package (Windows)
build.bat

# Build and package (Linux/macOS)
bash build_linux.bat

# Exclude optional modules from build
build.bat --no-qq --no-wecom --no-unity --no-github-tracker --no-agui --no-api

# Run directly
dotnet run --project src/DotCraft.App/DotCraft.App.csproj
```

## Tests

```bash
# Run all tests
dotnet test

# Run a single test by name
dotnet test --filter "FullyQualifiedName~TestClassName.TestMethodName"
```

Tests use **xUnit** with `coverlet.collector` for coverage. Test namespace: `DotCraft.Tests`. Tests are primarily protocol conformance tests under `tests/DotCraft.Core.Tests/Protocol/`.

## Architecture

### Module System

Every interaction mode is an `IDotCraftModule`. Modules are discovered via a **source generator** (`DotCraft.Gen`) that emits a `ModuleRegistrations` class when `DotCraftGenerateModuleRegistrations=true` is set in the App csproj. Modules are marked with `[DotCraftModule("name", Priority = x)]` and must be **partial** classes.

Three module types exist:
- **Host** — standalone process entry point (at most one per process, highest-priority enabled module wins). Has an associated `[HostFactory("name")]` class implementing `IHostFactory`.
- **Channel** — entry point managed by Gateway when Gateway is the active Host. Implements `CreateChannelService()` returning `IChannelService`.
- **Tool-only** — contributes tools via `GetToolProviders()` without Host or Channel (e.g., Unity).

Host priority order: CLI=0, API=10, WeCom=20, QQ=30. Gateway runs when no higher-priority standalone Host is active and manages multiple Channels concurrently.

### Session Protocol (Thread → Turn → Item)

Server-managed channels (CLI, ACP, QQ, WeCom, GitHubTracker) share a unified session model defined in `specs/session-core.md`:
- **Thread** — persistent conversation tied to workspace (stored in `.craft/threads/`)
- **Turn** — one unit of agent work from user input
- **Item** — atomic I/O unit (UserMessage, AgentMessage, ToolCall, ToolResult, ApprovalRequest, etc.)

`ISessionService` is the central API: `CreateThreadAsync`, `ResumeThreadAsync`, `SubmitInputAsync`, `ResolveApprovalAsync`. Events stream via `SessionEvent`.

**Exempted channels**: API and AG-UI use client-managed history by design and bypass Session Core.

### AppServer Protocol

`specs/appserver-protocol.md` defines a JSON-RPC wire protocol (stdio/WebSocket) projecting `ISessionService` to out-of-process clients. Used by ACP-external adapters.

### Client Applications

DotCraft has two external clients that connect to AppServer via the Wire Protocol:

**TUI (Terminal UI)** — Rust-native terminal interface built on Ratatui. Runs as a separate process communicating via stdio (default) or WebSocket.

- Location: `tui/`
- Key modules: `wire/` (JSON-RPC client), `ui/` (Ratatui widgets), `app/` (state, input routing)
- Build: `cd tui && cargo build --release`

**Desktop** — Electron + React desktop client. Connects to AppServer via WebSocket.

- Location: `desktop/`
- Key modules: `main/` (Electron main process), `renderer/` (React UI with Zustand)
- Build: `cd desktop && npm install && npm run dev`

### Agent Execution Pipeline

Built on `Microsoft.Extensions.AI`. Key components in `DotCraft.Core.Agents`:
- `AgentFactory` / `AgentRunner` — create and run agents with streaming
- `DynamicToolInjectionChatClient` — runtime tool injection
- `ToolCallFilteringChatClient` — filters tools by `EnabledTools` config
- `SubAgentManager` — concurrent subagent orchestration

### Configuration

Two-level config with workspace overriding global:
- Global: `~/.craft/config.json`
- Workspace: `.craft/config.json`

Module config is **modular**: each module defines its own section type(s) annotated with `[ConfigSection("SectionKey")]` in its own assembly and reads via `AppConfig.GetSection<T>("SectionKey")`. No changes to Core's `AppConfig` are needed for new module config. Schema is auto-generated for Dashboard.

### Tool Naming

Tool names vary by source — use exact names for whitelists (`EnabledTools`, `Tools.DeferredLoading.AlwaysLoadedTools`):
- Method-based (`AIFunctionFactory.Create(method)`): **PascalCase** (e.g., `ReadFile`, `Exec`, `WebSearch`)
- Manually created: often snake_case
- MCP tools: defined by the MCP server

### Context & Memory

- `PromptBuilder` — builds agent prompts with context
- `ContextCompactor` — compacts conversation history
- `MemoryConsolidator` — consolidates old messages into `MEMORY.md` / `HISTORY.md`
- Skills loaded from `.craft/skills/` (markdown-based)

## Source Layout

| Directory | Purpose |
|-----------|---------|
| `src/DotCraft.Core/` | Core library: agents, tools, sessions, protocol, config |
| `src/DotCraft.App/` | Main app entry, CLI, ACP, AppServer, Gateway hosts |
| `src/DotCraft.Gen/` | Source generator for module discovery |
| `src/DotCraft.QQ/` | QQ channel module (good reference for full channel impl) |
| `src/DotCraft.WeCom/` | WeCom channel module |
| `src/DotCraft.Unity/` | Unity tool provider (good reference for tool-only module) |
| `src/DotCraft.UnityClient/` | Unity client package for Unity-side integration |
| `src/DotCraft.Api/` | OpenAI-compatible API channel |
| `src/DotCraft.AGUI/` | AG-UI SSE channel |
| `src/DotCraft.Automations/` | Automation task orchestration channel |
| `src/DotCraft.GitHubTracker/` | GitHub issue/PR automation |
| `src/DotCraft.AppServerTestClient/` | End-to-end test client for AppServer Wire Protocol |
| `tests/` | Test projects: Core, App, GitHubTracker (xUnit) |
| `specs/` | Session Core and AppServer Protocol specifications |
| `sdk/` | Multi-language SDKs: Python, TypeScript for building channel adapters |
| `docs/` | Chinese docs; `docs/en/` for English |
| `samples/` | Sample projects (ag-ui-client, hooks, skills, workspace) |
| `tui/` | Rust terminal UI (Ratatui), connects via Wire Protocol |
| `desktop/` | Electron + React desktop client, connects via WebSocket |

## Code Style

- File-scoped namespaces, primary constructors, sealed by default, pattern matching
- `_camelCase` for private fields, `PascalCase` for properties/methods/constants, `camelCase` for parameters
- `#region` for organizing large sections
- XML doc comments (`/// <summary>`) for all public APIs
- Code comments in English; user-facing strings via `LanguageService.Current.T("key")` from embedded JSON language packs (`zh.json`, `en.json`)
- Type-safe string access via `DotCraft.Core.Localization.Strings` static properties

## Module Development Checklist

When adding a new module (see `samples/skills/dev-guide/references/module-development-spec.md`):

1. Decide Host vs Channel vs Tool-only
2. Mark class with `[DotCraftModule("name", Priority = x)]` and make it `partial`
3. If Host: add separate `[HostFactory("name")]` class implementing `IHostFactory`
4. If Channel: implement `CreateChannelService()` returning `IChannelService`
5. HITL: provide `IApprovalService` for channels running sensitive tools, or document AutoApprove usage
6. Config: define section type(s) with `[ConfigSection("Key")]` in your assembly, validate via `ValidateConfig`
7. Tools: optional via `GetToolProviders()`, gated by module enablement
8. Documentation: bilingual (Chinese in `docs/`, English in `docs/en/`)

## Documentation

All docs are bilingual. Chinese primary in `docs/*.md`, English in `docs/en/*.md`. Use language switcher links. Samples go in `samples/` with `README.md` + `README_ZH.md`.
