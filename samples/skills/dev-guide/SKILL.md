---
name: dotcraft-dev-guide
description: Development guidelines for DotCraft project. Use this skill when developing DotCraft core features, adding new modules, modifying existing code, or writing documentation. Covers C# code style, tool naming (PascalCase for AI functions), module development norms (via spec), and bilingual documentation requirements.
---

# DotCraft Development Guide

This skill provides development guidelines for contributing to the DotCraft project. Follow these guidelines when writing code, creating new modules, or writing documentation.

## Module Development

Module behavior (Host vs Channel, HITL, tool providers, configuration) is defined in **references/module-development-spec.md**. Follow that specification when adding or changing modules. Use the normative checklist there to verify a new module.

When adding a new module: mark the module class with `[DotCraftModule("name", Priority = x, Description = "...")]` and declare the class as **partial** (the source generator provides `Name` and `Priority` and registration). If the module is a Host, add a separate class marked with `[HostFactory("name")]` that implements `IHostFactory`. Implementation details and examples: search the codebase for `[DotCraftModule(`, `[HostFactory(`, `CreateChannelService`, `IApprovalService`, `GetToolProviders`, `AppConfig`, `IHostFactory`.

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

### Code Organization

Use `#region` to organize large code sections. Place helper methods after public methods. Group related methods together.

### Documentation Comments

Use XML documentation comments (`/// <summary>`, `/// <param>`, `/// <returns>`) for all public APIs.

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

1. **Module work**: Read references/module-development-spec.md and satisfy its checklist; search the codebase for similar features and existing abstractions.
2. **Implementation**: Follow code style and documentation guidelines above; add XML docs for public APIs; update user-facing docs in both languages; add examples as appropriate.
3. **Verify**: Test manually; confirm module changes conform to the spec and that docs/examples are in place.

## Additional Resources

- **Module development (norms and checklist)**: references/module-development-spec.md
