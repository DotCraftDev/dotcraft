using System.Collections.Concurrent;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Tracing;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Lsp;
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
    /// When set (e.g. local automation), absolute path to the task directory containing <c>task.md</c>.
    /// </summary>
    public string? AutomationTaskDirectory { get; init; }

    /// <summary>
    /// When set, overrides <see cref="Configuration.AppConfig.Tools.File.RequireApprovalOutsideWorkspace"/> for file/shell tools.
    /// </summary>
    public bool? RequireApprovalOutsideWorkspace { get; init; }

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
    /// Optional LSP server manager for language-intelligence tools.
    /// </summary>
    public LspServerManager? LspServerManager { get; init; }

    /// <summary>
    /// Registry for deferred MCP tools. Populated by <see cref="DeferredToolProvider"/>
    /// when deferred loading is active. Read by <see cref="DotCraft.Agents.AgentFactory"/>
    /// to wire <c>FunctionInvokingChatClient.AdditionalTools</c> and insert
    /// <c>DynamicToolInjectionChatClient</c> into the pipeline.
    /// </summary>
    public DeferredToolRegistry? DeferredToolRegistry { get; set; }

    /// <summary>
    /// Optional trace collector for debugging and monitoring.
    /// </summary>
    public TraceCollector? TraceCollector { get; init; }

    /// <summary>
    /// Optional thread-scoped store for external CLI session ids used by resumable subagents.
    /// </summary>
    public IExternalCliSessionStore? ExternalCliSessionStore { get; init; }

    /// <summary>
    /// Optional ACP extension proxy for extension method calls.
    /// Available when running in ACP mode (connected to Unity/IDE client).
    /// </summary>
    public IAcpExtensionProxy? AcpExtensionProxy { get; init; }

    /// <summary>
    /// Optional browser-use proxy for Desktop-hosted browser automation.
    /// Available only when the current AppServer thread is bound to a client that declared browser-use support.
    /// </summary>
    public IBrowserUseProxy? BrowserUseProxy { get; init; }

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
