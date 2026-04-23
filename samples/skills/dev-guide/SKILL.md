---
name: dotcraft-dev-guide
description: Development workflow and project-specific norms for DotCraft. Use when adding or modifying modules, changing protocol behavior, or shipping user-facing features. Covers spec-first workflow, testing rules (including when tests are optional), meaningful test criteria, module development pointers, tool naming, and bilingual docs.
---

# DotCraft Development Guide

Project-specific workflow and norms for DotCraft.
For generic C#/Rust/React style, follow each ecosystem's standard conventions.
For repo orientation, read `CLAUDE.md`.

## Development Workflow

### Spec-First

When modifying protocol designs or process flows defined in `specs/`, update the spec first, then implement.
If a proposed change conflicts with an existing spec, resolve the spec-level conflict before touching code.

### Steps

1. **Plan**: For module work, read `references/module-development-spec.md` and satisfy its checklist. Search the codebase for similar features before adding new abstractions.
2. **Implement**: Follow the project-specific norms below. Add XML docs for public C# APIs. Update user-facing docs in both languages when behavior changes.
3. **Test**: Follow the testing rules below.
4. **Verify**: Confirm changes conform to the relevant spec and docs/examples are in place.

### Testing Rules

Tests are required for:

- Protocol-dependent code (JSON-RPC, wire format, session/appserver protocol): add conformance tests aligned with the spec.
- Complex multi-step flows or state machines: cover key paths and edge cases.

Tests are not required for:

- Small bug fixes that do not change an observable contract.
- Pure UI polish (layout, styling, copy tweaks) in desktop or TUI.
- Trivial refactors with no behavior change.

A test is meaningful only when (applies to C# xUnit and TypeScript tests equally):

- It catches a real regression, not language semantics, trivial getters/setters, or framework internals.
- It is not redundant with existing coverage; check before adding.
- It does not merely restate implementation details via excessive mocking; prefer state/output assertions unless interaction itself is the contract.
- It is not written just to inflate coverage numbers.

Pre-commit: run the relevant full suites for touched areas (`dotnet test` for C#, and corresponding `npm test`/`cargo test` where applicable). Do not commit with known failures.

## Module Development

Module behavior (Host vs Channel, HITL, tool providers, configuration) is defined in **references/module-development-spec.md**. Follow that specification when adding or changing modules. Use the normative checklist there to verify a new module.

When adding a new module: mark the module class with `[DotCraftModule("name", Priority = x, Description = "...")]` and declare the class as **partial** (the source generator provides `Name` and `Priority` and registration). If the module is a Host, add a separate class marked with `[HostFactory("name")]` that implements `IHostFactory`. Implementation details and examples: search the codebase for `[DotCraftModule(`, `[HostFactory(`, `CreateChannelService`, `IApprovalService`, `GetToolProviders`, `AppConfig`, `IHostFactory`.

### Tool naming (AI functions)

Tool names exposed to the model vary by source. There is no global rule; use the **exact** name when configuring whitelists (**EnabledTools**, **Tools.DeferredLoading.AlwaysLoadedTools** in AppConfig).

| Source | Naming | Example |
|--------|--------|--------|
| Method-based (`AIFunctionFactory.Create(method)`) | PascalCase (method name as-is; no snake_case conversion) | `ReadFile`, `Exec`, `WebSearch`, `GrepFiles` |
| Manually created (`AIFunctionFactory.Create(..., name: "...")`) | No fixed convention; often snake_case | e.g. extension/ACP tools |
| MCP tools | Defined by the MCP server; typically snake_case | As returned by the server |

Use `[Description("...")]` on the method and on parameters for tool and parameter descriptions; optional `[Tool(Icon, DisplayType, DisplayMethod)]` for Dashboard UI (see FileTools.ReadFile).

Tool argument streaming is enabled for every tool by default: AppServer clients receive `item/toolCall/argumentsDelta` notifications as the model fills in the arguments JSON, and the TUI/Desktop render per-tool live previews. If a tool must ship with a single atomic payload (for example because it proxies arguments to another process that cannot consume partial JSON, or because the arguments are sensitive), opt out by annotating the method with `[StreamArguments(false)]`; clients will then only see `item/started` followed by `item/completed` with no deltas. Omit the attribute to keep streaming enabled.

### Language Preference

- **Code comments**: English
- **User-facing messages**: Use `LanguageService` for bilingual support, e.g. `lang.GetString("中文", "English")`. For CLI strings, the codebase centralizes entries in `DotCraft.Core.Localization.Strings`; search for `Strings.` and `LanguageService` for examples.

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

## Additional Resources

- **Module development (norms and checklist)**: references/module-development-spec.md
- **Specs (protocol and process design)**: `specs/` — always consult and update these before implementation
- **Python SDK for external channels**: `sdk/python/`
- **TUI README**: `tui/README.md` — build instructions, CLI flags, key bindings
- **Desktop README**: `desktop/README.md` — usage, settings, npm scripts
