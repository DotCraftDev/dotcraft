using System.Collections.Concurrent;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Tracing;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using OpenAI.Chat;

namespace DotCraft.Abstractions;

/// <summary>
/// Provides context information for tool provider to create tools.
/// </summary>
public sealed class ToolProviderContext
{
    /// <summary>
    /// The application configuration.
    /// </summary>
    public required AppConfig Config { get; init; }

    /// <summary>
    /// The chat client for AI interactions.
    /// Required for subagent spawning and other AI-powered tools.
    /// </summary>
    public required ChatClient ChatClient { get; init; }

    /// <summary>
    /// The workspace path.
    /// </summary>
    public required string WorkspacePath { get; init; }

    /// <summary>
    /// The bot path for configuration and memory storage.
    /// </summary>
    public required string BotPath { get; init; }

    /// <summary>
    /// The memory store for context persistence.
    /// </summary>
    public required MemoryStore MemoryStore { get; init; }

    /// <summary>
    /// The skills loader for skill-based tools.
    /// </summary>
    public required SkillsLoader SkillsLoader { get; init; }

    /// <summary>
    /// The approval service for sensitive operations.
    /// </summary>
    public required IApprovalService ApprovalService { get; init; }

    /// <summary>
    /// Optional path blacklist for security restrictions.
    /// </summary>
    public PathBlacklist? PathBlacklist { get; init; }

    /// <summary>
    /// Optional cron tools for scheduled tasks.
    /// </summary>
    public CronTools? CronTools { get; init; }

    /// <summary>
    /// Optional MCP client manager for external tool integration.
    /// </summary>
    public McpClientManager? McpClientManager { get; init; }

    /// <summary>
    /// Optional trace collector for debugging and monitoring.
    /// </summary>
    public TraceCollector? TraceCollector { get; init; }

    /// <summary>
    /// Optional channel-specific client (e.g., QQBotClient).
    /// Used by channel-specific tool providers.
    /// </summary>
    public object? ChannelClient { get; init; }

    /// <summary>
    /// Optional ACP extension proxy for extension method calls.
    /// Available when running in ACP mode (connected to Unity/IDE client).
    /// </summary>
    public IAcpExtensionProxy? AcpExtensionProxy { get; init; }

    /// <summary>
    /// Collection of disposable resources created by tool providers.
    /// These resources will be disposed when the application shuts down.
    /// </summary>
    public ConcurrentBag<IAsyncDisposable> DisposableResources { get; } = [];

    /// <summary>
    /// File system abstraction for channel tools that need host-local file access.
    /// Defaults to <see cref="HostAgentFileSystem"/>; overridden to sandbox implementation
    /// when sandbox mode is enabled (see <c>SandboxToolProvider</c>).
    /// </summary>
    public IAgentFileSystem AgentFileSystem
    {
        get => field ??= new HostAgentFileSystem(WorkspacePath);
        set;
    }
}
