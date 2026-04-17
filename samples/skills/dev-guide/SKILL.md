---
name: dotcraft-dev-guide
description: Development guidelines for DotCraft project. Use this skill when developing DotCraft core features, adding new modules (including external channel adapters via AppServer/JRPC), modifying existing code, or writing documentation. Covers C# code style, tool naming (PascalCase for AI functions), module development norms (via spec), external channel extension with Python SDK, spec-first workflow, testing requirements, and bilingual documentation.
---

# DotCraft Development Guide

This skill provides development guidelines for contributing to the DotCraft project. Follow these guidelines when writing code, creating new modules, or writing documentation.

## Module Development

Module behavior (Host vs Channel, HITL, tool providers, configuration) is defined in **references/module-development-spec.md**. Follow that specification when adding or changing modules. Use the normative checklist there to verify a new module.

When adding a new module: mark the module class with `[DotCraftModule("name", Priority = x, Description = "...")]` and declare the class as **partial** (the source generator provides `Name` and `Priority` and registration). If the module is a Host, add a separate class marked with `[HostFactory("name")]` that implements `IHostFactory`. Implementation details and examples: search the codebase for `[DotCraftModule(`, `[HostFactory(`, `CreateChannelService`, `IApprovalService`, `GetToolProviders`, `AppConfig`, `IHostFactory`.

### Extension via AppServer (External Channel)

In addition to in-process Host and Channel modules, DotCraft supports **out-of-process** social channel adapters through the **AppServer** and the **External Channel** mechanism:

- **AppServer** exposes a JSON-RPC 2.0 wire protocol (see `specs/appserver-protocol.md`) that allows external processes to interact with DotCraft (threads, turns, events, approval).
- **External Channel Adapter** (see `specs/external-channel-adapter.md`) extends the AppServer protocol with channel-specific lifecycle (registration, message routing, heartbeat) so an external process can act as a full channel without being compiled into the C# host.
- A **Python SDK** (`sdk/python/`, package `dotcraft-wire`) is provided to simplify building external channel adapters. It includes `DotCraftClient` (raw JSON-RPC), `ChannelAdapter` (high-level base class for social bots), and transport implementations (Stdio, WebSocket). See `sdk/python/README.md` for installation and API reference, and `sdk/python/examples/telegram/` for a complete Telegram adapter example.

When to choose External Channel over an in-process Channel:

| Consideration | In-process Channel (C#) | External Channel (JRPC + SDK) |
|---------------|--------------------------|-------------------------------|
| Language/runtime | Must be C# | Any language with JSON-RPC support (Python SDK provided) |
| Deployment | Same process as DotCraft host | Separate process; can be deployed independently |
| Performance | In-process, lower latency | Cross-process via Stdio/WebSocket |
| Typical use case | Core channels tightly integrated with host | Community/third-party social platform adapters |

For external channel development, follow the protocol specs and use the SDK. The relevant specs and SDK docs serve as the primary references:

- `specs/appserver-protocol.md` — wire protocol
- `specs/external-channel-adapter.md` — adapter contract and lifecycle
- `sdk/python/README.md` — Python SDK quick start and API
- `sdk/python/ARCHITECTURE.md` — SDK internal design

## Code Style Guidelines

DotCraft follows official C# coding conventions with modern language features.

### Modern C# Features

Use these modern C# features consistently: file-scoped namespaces (C# 10+), primary constructors for simple initialization, sealed classes by default (unless designed for inheritance), pattern matching and switch expressions where appropriate.

### Naming Conventions

| Element | Convention | Example |
|---------|-----------|---------|
| Private fields | `_camelCase` | `_workspaceRoot` |
| Constants | `PascalCase` | `DefaultReadLimit` |
| Properties | `PascalCase` | `public string Name { get; }` |
| Methods | `PascalCase` | `public void ConfigureServices()` |
| Interfaces | `IPascalCase` | `IDotCraftModule` |
| Parameters | `camelCase` | `string workspaceRoot` |

### Tool naming (AI functions)

Tool names exposed to the model vary by source. There is no global rule; use the **exact** name when configuring whitelists (**EnabledTools**, **Tools.DeferredLoading.AlwaysLoadedTools** in AppConfig).

| Source | Naming | Example |
|--------|--------|--------|
| Method-based (`AIFunctionFactory.Create(method)`) | PascalCase (method name as-is; no snake_case conversion) | `ReadFile`, `Exec`, `WebSearch`, `GrepFiles` |
| Manually created (`AIFunctionFactory.Create(..., name: "...")`) | No fixed convention; often snake_case | e.g. extension/ACP tools |
| MCP tools | Defined by the MCP server; typically snake_case | As returned by the server |

Use `[Description("...")]` on the method and on parameters for tool and parameter descriptions; optional `[Tool(Icon, DisplayType, DisplayMethod)]` for Dashboard UI (see FileTools.ReadFile).

Tool argument streaming is enabled for every tool by default: AppServer clients receive `item/toolCall/argumentsDelta` notifications as the model fills in the arguments JSON, and the TUI/Desktop render per-tool live previews. If a tool must ship with a single atomic payload (for example because it proxies arguments to another process that cannot consume partial JSON, or because the arguments are sensitive), opt out by annotating the method with `[StreamArguments(false)]`; clients will then only see `item/started` followed by `item/completed` with no deltas. Omit the attribute to keep streaming enabled.

### Code Organization

Use `#region` to organize large code sections. Place helper methods after public methods. Group related methods together.

### Documentation Comments

Use XML documentation comments (`/// <summary>`, `/// <param>`, `/// <returns>`) for all public APIs.

### Language Preference

- **Code comments**: English
- **User-facing messages**: Use `LanguageService` for bilingual support, e.g. `lang.GetString("中文", "English")`. For CLI strings, the codebase centralizes entries in `DotCraft.Core.Localization.Strings`; search for `Strings.` and `LanguageService` for examples.

### Rust Style (TUI)

Use idiomatic Rust with modern features:
- **Modules**: `mod.rs` for directory modules
- **Error handling**: `anyhow::Result` for application code, `thiserror` for library errors
- **Async**: Tokio runtime; use `#[tokio::main]` for entry points
- **Naming**: `snake_case` for functions/variables, `PascalCase` for types

Key crates (see `tui/Cargo.toml`): `ratatui` (TUI), `tokio` (async), `serde_json`, `pulldown-cmark` + `syntect` (Markdown/syntax highlighting).

### React/TypeScript Style (Desktop)

Follow standard React + TypeScript conventions:
- **Components**: Functional components with hooks
- **State**: Zustand stores in `renderer/stores/`
- **Styling**: Tailwind CSS 4
- **Naming**: `PascalCase` for components, `camelCase` for utilities

Key dependencies (see `desktop/package.json`): `react` 19, `electron` 35, `zustand` (state), `react-markdown` + `highlight.js` (rendering).

## Documentation Guidelines

### Bilingual Requirements

All documentation must be provided in both Chinese and English:

- **Default (Chinese)**: `docs/*.md`, with entry at `docs/index.md`
- **English**: `docs/en/*.md`, with entry at `docs/en/index.md`

Use a language switcher in documents by linking to the index of the other language, e.g.:

```markdown
**[中文](../index.md) | English**
```

(in a doc under `docs/en/`) or

```markdown
**中文 | [English](./en/index.md)**
```

(in a doc under `docs/` root)

### Sample Code

Provide examples in two ways:

1. **Inline examples** in documentation (for short snippets)
2. **Complete samples** in `samples/` directory (for full projects), with README.md and README_ZH.md where applicable.

### Documentation Structure

Follow this structure for feature documentation:

```markdown
# Feature Name

Brief description of the feature.

## Quick Start

Minimal working example.

## Configuration

Detailed configuration options.

## Usage Examples

Multiple practical examples.

## Advanced Topics

Edge cases and advanced scenarios.

## Troubleshooting

Common issues and solutions.
```

## Development Workflow

### Spec-First Development

When modifying protocol designs or process flows defined in `specs/`, **always update the spec first, then implement**. This ensures the design is deliberated and documented before code is written, preventing design decisions from being driven by implementation accidents. If a proposed change conflicts with an existing spec, resolve the spec-level conflict before touching code.

### Development Steps

1. **Module work**: Read references/module-development-spec.md and satisfy its checklist; search the codebase for similar features and existing abstractions.
2. **Implementation**: Follow code style and documentation guidelines above; add XML docs for public APIs; update user-facing docs in both languages; add examples as appropriate.
3. **Testing**:
   - **Protocol-dependent code**: Any C# code that depends on a communication protocol (JSON-RPC, wire format, etc.) **must** have corresponding test cases that verify the implementation is complete and conforms to the spec.
   - **Complex workflows**: Code involving non-trivial multi-step flows or state machines should also have dedicated test cases covering key paths and edge cases.
   - **Pre-commit full test**: After completing a feature or fix, run the **full test suite** and ensure all tests pass before committing. Do not commit with known test failures.
4. **Verify**: Confirm module changes conform to the spec and that docs/examples are in place.

## Additional Resources

- **Module development (norms and checklist)**: references/module-development-spec.md
- **Specs (protocol and process design)**: `specs/` — always consult and update these before implementation
- **Python SDK for external channels**: `sdk/python/`
- **TUI README**: `tui/README.md` — build instructions, CLI flags, key bindings
- **Desktop README**: `desktop/README.md` — usage, settings, npm scripts
