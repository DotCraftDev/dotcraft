---
name: dotcraft-dev-guide
description: Development guidelines for DotCraft project. Use this skill when developing DotCraft core features, adding new modules, modifying existing code, or writing documentation. Covers C# code style, architecture patterns, module development, and bilingual documentation requirements.
---

# DotCraft Development Guide

This skill provides development guidelines for contributing to the DotCraft project. Follow these guidelines when writing code, creating new modules, or writing documentation.

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

## Architecture Guidelines

### Module Structure

DotCraft uses a modular architecture with clear separation of concerns:

```
DotCraft.Core/       # Core agent functionality (shared across all channels)
├── Abstractions/    # Interfaces and base classes
├── Agents/          # Agent execution logic
├── Tools/           # Core tools (File, Shell, Web)
├── Sessions/        # Session management
├── Memory/          # Memory system
└── Modules/         # Module system infrastructure

DotCraft.App/        # Application layer
├── CLI/             # Command-line interface
├── Api/             # HTTP API server
├── Acp/             # Agent Client Protocol
└── Gateway/         # Multi-channel concurrent mode

DotCraft.QQ/         # QQ Bot channel
DotCraft.WeCom/      # WeChat Work channel
DotCraft.Unity/      # Unity Editor integration
```

### Where to Add New Features

When adding new functionality, determine the appropriate module:

1. **Core functionality** (shared across all channels)
   - Place in `DotCraft.Core/`
   - Examples: new tools, memory improvements, session handling

2. **Channel-specific features**
   - Create a new module (e.g., `DotCraft.Discord/`)
   - Implement `IDotCraftModule` interface
   - Register via source generator

3. **Application features**
   - Place in `DotCraft.App/`
   - Examples: new CLI commands, API endpoints, configuration UI

### Module Development

All modules must implement `IDotCraftModule`:

```csharp
[DotCraftModule("my-channel", Priority = 40, Description = "My channel description")]
public sealed class MyModule : ModuleBase
{
    public override bool IsEnabled(AppConfig config) => config.MyChannel.Enabled;
    
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        services.AddSingleton<MyClient>();
        services.AddSingleton<MyService>();
    }
    
    public override IEnumerable<IAgentToolProvider> GetToolProviders()
        => [new MyToolProvider()];
    
    public override IChannelService CreateChannelService(IServiceProvider sp, ModuleContext context)
        => ActivatorUtilities.CreateInstance<MyChannelService>(sp);
}
```

**Module Priority Guidelines**:
- CLI: 0
- API: 10
- WeCom: 20
- QQ: 30
- Custom channels: 40+
- Unity: 50

For detailed module development guidance, see `references/module-development.md`.

### Dependency Injection

Use constructor injection and register services in `ConfigureServices`:

```csharp
public override void ConfigureServices(IServiceCollection services, ModuleContext context)
{
    // Singleton for stateless or shared state
    services.AddSingleton<MyClient>();
    
    // Scoped for per-request instances (in API context)
    services.AddScoped<MyService>();
    
    // Factory pattern for complex initialization
    services.AddSingleton(sp =>
    {
        var client = sp.GetRequiredService<QQBotClient>();
        return new MyService(client, context.Config);
    });
}
```

### Adding New Tools

Implement `IAgentToolProvider` and use the `[Tool]` attribute:

```csharp
public sealed class MyToolProvider : IAgentToolProvider
{
    public IEnumerable<object> GetTools(IServiceProvider serviceProvider)
        => [new MyTools()];
}

public sealed class MyTools
{
    [Description("Does something useful")]
    [Tool(Icon = "🔧")]
    public string DoSomething([Description("Input parameter")] string input)
    {
        return $"Processed: {input}";
    }
}
```

## Documentation Guidelines

### Bilingual Requirements

All documentation must be provided in both Chinese and English:

```
docs/
├── README.md          # English version
├── README_ZH.md       # Chinese version
├── config_guide.md
├── config_guide.md (if different content needed)
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
   ```markdown
   ## Usage
   
   Configure the module in `config.json`:
   ```json
   {
       "MyChannel": {
           "Enabled": true,
           "ApiKey": "your-key"
       }
   }
   ```
   ```

2. **Complete samples** in `samples/` directory (for full projects)
   ```
   samples/
   ├── hooks/
   │   ├── README.md
   │   ├── README_ZH.md
   │   ├── windows/
   │   └── linux/
   ├── bootstrap/
   └── python/
   ```

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

1. Understand the existing architecture by reading:
   - `references/architecture.md` for system overview
   - `references/module-development.md` for module patterns
   - Existing similar modules for reference

2. Determine where the feature belongs:
   - Core (shared functionality)?
   - Existing module (channel-specific)?
   - New module (new channel/integration)?

3. Check if existing abstractions are sufficient:
   - Can the feature use existing interfaces?
   - Does it require new abstractions in Core?

### Making Changes

1. Follow code style guidelines
2. Add XML documentation comments
3. Update relevant documentation (both languages)
4. Add inline examples or samples directory
5. Test the changes manually

### Code Review Checklist

- [ ] Follows C# official style conventions
- [ ] Uses modern C# features appropriately
- [ ] Includes XML documentation for public APIs
- [ ] Uses English for code comments
- [ ] Provides bilingual user messages where applicable
- [ ] Placed in the correct module
- [ ] Documentation updated in both languages
- [ ] Examples provided (inline or samples directory)

## Additional Resources

- **Architecture Details**: See `references/architecture.md` for in-depth architecture explanation
- **Module Development**: See `references/module-development.md` for step-by-step module creation guide
