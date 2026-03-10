---
name: dotcraft-dev-guide
description: Development guidelines for DotCraft project. Use this skill when developing DotCraft core features, adding new modules, modifying existing code, or writing documentation. Covers C# code style, module development norms (via spec), and bilingual documentation requirements.
---

# DotCraft Development Guide

This skill provides development guidelines for contributing to the DotCraft project. Follow these guidelines when writing code, creating new modules, or writing documentation.

## Module Development

Module behavior (Host vs Channel, HITL, tool providers, configuration) is defined in **references/module-development-spec.md**. Follow that specification when adding or changing modules. Use the normative checklist there to verify a new module.

For implementation details and examples, search the codebase for: `[DotCraftModule(`, `CreateChannelService`, `IApprovalService`, `GetToolProviders`, `AppConfig`, `IHostFactory`.

## Code Style Guidelines

DotCraft follows official C# coding conventions with modern language features.

### Modern C# Features

Use these modern C# features consistently:

- **File-scoped namespaces** (C# 10+)
  ```csharp
  namespace DotCraft.Tools;
  ```

- **Primary constructors** for classes with simple initialization
  ```csharp
  public sealed class FileTools(
      string workspaceRoot,
      bool requireApprovalOutsideWorkspace = true)
  {
      private readonly string _workspaceRoot = workspaceRoot;
  }
  ```

- **Sealed classes** by default (unless explicitly designed for inheritance)
  ```csharp
  public sealed class AgentRunner { }
  ```

- **Pattern matching** and switch expressions
  ```csharp
  var tag = sessionKey.StartsWith("heartbeat") ? "Heartbeat"
      : sessionKey.StartsWith("cron:") ? "Cron"
      : "Agent";
  ```

### Naming Conventions

| Element | Convention | Example |
|---------|-----------|---------|
| Private fields | `_camelCase` | `_workspaceRoot` |
| Constants | `PascalCase` | `DefaultReadLimit` |
| Properties | `PascalCase` | `public string Name { get; }` |
| Methods | `PascalCase` | `public void ConfigureServices()` |
| Interfaces | `IPascalCase` | `IDotCraftModule` |
| Parameters | `camelCase` | `string workspaceRoot` |

### Code Organization

- Use `#region` to organize large code sections
  ```csharp
  #region Private Helpers
  
  private static string FormatDirectoryListing(...)
  {
      // ...
  }
  
  #endregion
  ```

- Place helper methods after public methods
- Group related methods together

### Documentation Comments

Use XML documentation comments for all public APIs:

```csharp
/// <summary>
/// Registry for managing DotCraft modules.
/// Modules are registered explicitly via <see cref="RegisterModule"/> at startup.
/// </summary>
public sealed class ModuleRegistry
{
    /// <summary>
    /// Gets all registered modules.
    /// </summary>
    public IReadOnlyList<IDotCraftModule> Modules => _modules.AsReadOnly();
}
```

### Language Preference

- **Code comments**: English
- **User-facing messages**: Use `LanguageService` for bilingual support
  ```csharp
  var lang = new LanguageService(selectedLanguage);
  AnsiConsole.MarkupLine($"[cyan]{lang.GetString("当前工作区路径", "Current workspace path:")}[/]");
  ```

## Documentation Guidelines

### Bilingual Requirements

All documentation must be provided in both Chinese and English:

```
docs/
├── README.md          # English version
├── README_ZH.md       # Chinese version
├── config_guide.md
└── ...
```

Use language switcher in documents:

```markdown
**[中文](./README_ZH.md) | English**
```

or

```markdown
**中文 | [English](./README.md)**
```

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

### Before Making Changes

1. For new or changed modules: read **references/module-development-spec.md** and satisfy the checklist.
2. Discover where the feature belongs and how similar features are implemented by searching the codebase (interfaces, existing modules).
3. Check if existing abstractions are sufficient.

### Making Changes

1. Follow code style guidelines above.
2. Add XML documentation comments for public APIs.
3. Update relevant documentation (both languages when user-facing).
4. Add inline examples or samples as appropriate.
5. Test the changes manually.

### Code Review Checklist

- [ ] Follows C# official style conventions
- [ ] Uses modern C# features appropriately
- [ ] Includes XML documentation for public APIs
- [ ] Uses English for code comments
- [ ] Provides bilingual user messages where applicable
- [ ] Module changes conform to references/module-development-spec.md
- [ ] Documentation updated in both languages where applicable
- [ ] Examples provided (inline or samples directory) where applicable

## Additional Resources

- **Module development (norms and checklist)**: references/module-development-spec.md
