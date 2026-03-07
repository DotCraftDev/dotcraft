# Module Development Reference Index

This document provides step-by-step guidance and file references for developing new DotCraft modules.

## Development Steps Overview

1. **Study Existing Modules** - Understand the pattern by examining real implementations
2. **Define Configuration** - Add configuration model to AppConfig
3. **Create Module Class** - Implement IDotCraftModule interface
4. **Implement Services** - Channel service, client, approval service
5. **Add Tools (Optional)** - Channel-specific tools
6. **Create Factories** - Service initialization logic
7. **Write Validators** - Configuration validation
8. **Update Project Files** - Add to solution
9. **Write Documentation** - Bilingual user guides
10. **Test** - Manual testing in real environment

## Reference Implementations

### Complete Channel Module Example
**QQ Module** - Full-featured channel with approval, tools, and session isolation

Key files to study in order:
```
src/DotCraft.QQ/
├── QQModule.cs                    # Start here: module entry point
├── Configuration/
│   └── QQBotConfig.cs             # Configuration model
├── Services/
│   ├── QQChannelService.cs        # Channel implementation
│   ├── QQBotClient.cs             # External API client
│   ├── QQApprovalService.cs       # User approval via chat
│   └── QQPermissionService.cs     # Permission checking
├── Tools/
│   ├── QQToolProvider.cs          # Tool provider
│   └── QQTools.cs                 # QQ-specific tools
├── Factories/
│   ├── QQClientFactory.cs         # Client creation
│   └── QQApprovalServiceFactory.cs # Approval service creation
└── Context/
    └── QQChatContextProvider.cs   # Chat context extraction
```

### Minimal Tool-Only Module Example
**Unity Module** - Simple module that only contributes tools

Key files:
```
src/DotCraft.Unity/
├── UnityModule.cs                 # Minimal module implementation
└── Tools/
    └── UnityAcpToolProvider.cs    # Tool provider only
```

### Another Channel Example
**WeCom Module** - Similar to QQ, different platform

```
src/DotCraft.WeCom/
├── WeComModule.cs
├── Services/
└── ...
```

## Step-by-Step File References

### Step 1: Configuration

**Define configuration class**:
- Pattern: `src/DotCraft.QQ/Configuration/QQBotConfig.cs`
- Add to: `src/DotCraft.Core/Configuration/AppConfig.cs`

**Example to follow**:
```csharp
// See: src/DotCraft.QQ/Configuration/QQBotConfig.cs
public sealed class QQBotConfig
{
    public bool Enabled { get; set; }
    public string? ApiKey { get; set; }
    // ...
}
```

### Step 2: Module Class

**Create module class**:
- Pattern: `src/DotCraft.QQ/QQModule.cs`
- Interface: `src/DotCraft.Core/Abstractions/IDotBotModule.cs`
- Base class: `src/DotCraft.Core/Modules/ModuleBase.cs`

**Key methods to implement**:
```csharp
// Study: src/DotCraft.QQ/QQModule.cs
[DotCraftModule("qq", Priority = 30)]
public sealed class QQModule : ModuleBase
{
    public override bool IsEnabled(AppConfig config) => config.QQBot.Enabled;
    public override void ConfigureServices(IServiceCollection services, ModuleContext context) { }
    public override IEnumerable<IAgentToolProvider> GetToolProviders() => [];
    public override IChannelService CreateChannelService(IServiceProvider sp, ModuleContext context);
}
```

### Step 3: Channel Service

**Implement IChannelService**:
- Interface: `src/DotCraft.Core/Abstractions/IChannelService.cs`
- Example: `src/DotCraft.QQ/Services/QQChannelService.cs`

**Session key derivation pattern**:
```csharp
// See: src/DotCraft.QQ/Services/QQChannelService.cs
private static string DeriveSessionKey(QQMessage message)
{
    return message.IsGroup 
        ? $"qq_{message.GroupId}" 
        : $"qq_{message.UserId}";
}
```

### Step 4: Client Implementation

**Create API client**:
- Example: `src/DotCraft.QQ/Services/QQBotClient.cs`
- Pattern: WebSocket/HTTP client for external service

### Step 5: Approval Service

**Implement IApprovalService**:
- Interface: `src/DotCraft.Core/Security/IApprovalService.cs`
- Example: `src/DotCraft.QQ/Services/QQApprovalService.cs`

**Pattern**:
```csharp
// Study: src/DotCraft.QQ/Services/QQApprovalService.cs
public async Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext context)
{
    // 1. Check permissions
    // 2. Send approval request to chat
    // 3. Wait for user response
    // 4. Return result
}
```

### Step 6: Tools (Optional)

**Create tool provider**:
- Interface: `src/DotCraft.Core/Abstractions/IAgentToolProvider.cs`
- Example: `src/DotCraft.QQ/Tools/QQToolProvider.cs`

**Create tools**:
- Pattern: `src/DotCraft.Core/Tools/FileTools.cs` (core tool example)
- Example: `src/DotCraft.QQ/Tools/QQTools.cs` (channel-specific tools)

**Tool attributes**:
```csharp
// See: src/DotCraft.Core/Tools/FileTools.cs
[Description("Read file contents")]
[Tool(Icon = "📄")]
public async Task<string> ReadFile([Description("File path")] string path)
```

### Step 7: Factories

**Create service factories**:
- Example: `src/DotCraft.QQ/Factories/QQClientFactory.cs`
- Example: `src/DotCraft.QQ/Factories/QQApprovalServiceFactory.cs`

### Step 8: Configuration Validator

**Create validator**:
- Example: `src/DotCraft.QQ/Configuration/QQConfigValidator.cs`

### Step 9: Chat Context Provider (Optional)

**Provide context for approval**:
- Example: `src/DotCraft.QQ/Context/QQChatContextProvider.cs`
- Registry: `src/DotCraft.Core/Context/ChatContextRegistry.cs`

## Project File Setup

**Create .csproj file**:
- Pattern: `src/DotCraft.QQ/DotCraft.QQ.csproj`

**Add to solution**:
- Update `dotcraft.sln`
- Add reference in `src/DotCraft.App/DotCraft.App.csproj`

## Documentation Requirements

### User Guide
- **English**: `docs/qq_bot_guide.md` (example)
- **Chinese**: `docs/qq_bot_guide.md` (should be bilingual version)
- **Structure**: See `docs/hooks_guide.md` for good example structure

### Sample Code (if complex)
- **Location**: `samples/your-feature/`
- **Example**: `samples/hooks/` - includes README.md + README_ZH.md + examples

**Documentation must include**:
- Configuration options table
- Usage examples
- Troubleshooting section
- Both English and Chinese versions

## Module Priority Guidelines

Assign priority based on channel type:

| Channel Type | Priority | Examples |
|--------------|----------|----------|
| CLI | 0 | Default local interface |
| API | 10 | HTTP service |
| WeCom | 20 | WeChat Work |
| QQ | 30 | QQ Bot |
| Custom | 40+ | New channels |
| Unity | 50 | Tool-only modules |

## Key Patterns to Follow

### Service Registration Pattern
```csharp
// See: src/DotCraft.QQ/QQModule.cs - ConfigureServices
services.AddSingleton(sp => QQClientFactory.CreateClient(context));
services.AddSingleton<QQApprovalService>();
```

### Tool Provider Pattern
```csharp
// See: src/DotCraft.QQ/Tools/QQToolProvider.cs
public IEnumerable<object> GetTools(IServiceProvider serviceProvider)
{
    var client = serviceProvider.GetService<QQBotClient>();
    if (client != null)
        yield return new QQTools(client);
}
```

### Approval Pattern
```csharp
// See: src/DotCraft.QQ/Services/QQApprovalService.cs
public async Task<bool> RequestFileApprovalAsync(...)
{
    if (!permissionService.CanApprove(userId))
        return false;
    
    await client.SendMessageAsync(channelId, approvalRequest);
    return await WaitForApprovalAsync();
}
```

## Testing Checklist

Before submitting a module:

- [ ] Module class has `[DotCraftModule]` attribute with priority
- [ ] Implements `IDotCraftModule` (or inherits `ModuleBase`)
- [ ] Configuration added to `AppConfig.cs`
- [ ] Configuration validator created
- [ ] Services registered in `ConfigureServices`
- [ ] Session keys derived correctly for isolation
- [ ] Approval service implemented (if needed)
- [ ] Tools created and registered (if needed)
- [ ] Factories created for complex initialization
- [ ] ChatContext provider created (if needed)
- [ ] Documentation in both English and Chinese
- [ ] Project file updated with conditional compilation
- [ ] Added to solution and App project
- [ ] Manually tested in real environment
- [ ] Follows C# style guidelines
- [ ] XML documentation comments added

## Search Patterns for Development

Use these patterns to find relevant code:

- **Module examples**: Search `[DotCraftModule(`
- **Service registration**: Search `ConfigureServices(`
- **Tool definitions**: Search `[Tool(`
- **Approval implementations**: Search `IApprovalService`
- **Session derivation**: Search `DeriveSessionKey`
- **Channel services**: Search `IChannelService`

## When to Study Which File

- **Starting out**: Read `src/DotCraft.QQ/QQModule.cs` end-to-end
- **Configuration**: Study `src/DotCraft.QQ/Configuration/QQBotConfig.cs`
- **Channel logic**: Study `src/DotCraft.QQ/Services/QQChannelService.cs`
- **Approval**: Study `src/DotCraft.QQ/Services/QQApprovalService.cs`
- **Tools**: Study `src/DotCraft.Core/Tools/FileTools.cs` and `src/DotCraft.QQ/Tools/QQTools.cs`
- **Minimal module**: Study `src/DotCraft.Unity/UnityModule.cs`
- **Factories**: Study `src/DotCraft.QQ/Factories/QQClientFactory.cs`
- **Testing**: Run existing modules and observe behavior
