using System.ClientModel;
using System.Collections.Concurrent;
using DotCraft.Abstractions;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Tracing;
using DotCraft.Hooks;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace DotCraft.Agents;

/// <summary>
/// Factory for creating AI agents with tool aggregation from providers.
/// Tools are aggregated from registered <see cref="IAgentToolProvider"/> instances.
/// </summary>
public sealed class AgentFactory : IAsyncDisposable
{
    private readonly AppConfig _config;
    private readonly MemoryStore _memoryStore;
    private readonly SkillsLoader _skillsLoader;
    private readonly string _dotcraftPath;
    private readonly string _workspacePath;
    private readonly ChatClient _chatClient;
    private readonly ConcurrentDictionary<string, TokenTracker> _tokenTrackers = new();
    private readonly ConcurrentDictionary<string, int> _lastConsolidated = new();
    private readonly HashSet<string> _consolidating = [];
    private readonly TraceCollector? _traceCollector;
    private readonly HashSet<string> _globalEnabledToolNames;
    private readonly ToolProviderContext _toolProviderContext;
    private readonly IReadOnlyList<IAgentToolProvider> _toolProviders;
    private readonly CustomCommandLoader? _customCommandLoader;
    private readonly PlanStore? _planStore;
    private readonly Action<StructuredPlan>? _onPlanUpdated;
    private readonly HookRunner? _hookRunner;

    /// <summary>
    /// Creates a new AgentFactory with tool providers.
    /// </summary>
    public AgentFactory(
        string dotcraftPath,
        string workspacePath,
        AppConfig config,
        MemoryStore memoryStore,
        SkillsLoader skillsLoader,
        IApprovalService approvalService,
        PathBlacklist? blacklist,
        IEnumerable<IAgentToolProvider> toolProviders,
        ToolProviderContext? toolProviderContext = null,
        TraceCollector? traceCollector = null,
        CustomCommandLoader? customCommandLoader = null,
        PlanStore? planStore = null,
        Action<StructuredPlan>? onPlanUpdated = null,
        Action<string>? onConsolidatorStatus = null,
        HookRunner? hookRunner = null)
    {
        _config = config;
        _memoryStore = memoryStore;
        _skillsLoader = skillsLoader;
        _dotcraftPath = dotcraftPath;
        _workspacePath = workspacePath;
        _traceCollector = traceCollector;
        _customCommandLoader = customCommandLoader;
        _planStore = planStore;
        _onPlanUpdated = onPlanUpdated;
        _hookRunner = hookRunner;
        _globalEnabledToolNames = ResolveGlobalEnabledToolNames(_config);

        var openAIClient = new OpenAIClient(new ApiKeyCredential(config.ApiKey), new OpenAIClientOptions
        {
            Endpoint = new Uri(config.EndPoint)
        });
        _chatClient = openAIClient.GetChatClient(_config.Model);

        string consolidationModel = string.IsNullOrWhiteSpace(_config.ConsolidationModel) ? _config.Model : _config.ConsolidationModel;
        var consolidationChatClient = openAIClient.GetChatClient(consolidationModel);

        Consolidator = new MemoryConsolidator(consolidationChatClient, memoryStore, onConsolidatorStatus);

        if (config.MaxContextTokens > 0)
            Compactor = new ContextCompactor(_chatClient, Consolidator);

        // Build tool provider context
        _toolProviderContext = toolProviderContext ?? new ToolProviderContext
        {
            Config = config,
            ChatClient = _chatClient,
            WorkspacePath = workspacePath,
            BotPath = dotcraftPath,
            MemoryStore = memoryStore,
            SkillsLoader = skillsLoader,
            ApprovalService = approvalService,
            PathBlacklist = blacklist,
            TraceCollector = traceCollector
        };

        _toolProviders = toolProviders.ToList();
    }

    /// <summary>
    /// Process-level tool provider context (workspace root, memory, skills).
    /// Per-thread overrides are passed to <see cref="CreateToolsForMode(AgentMode, ToolProviderContext)"/>
    /// and the overload of <see cref="CreateAgentWithTools(List{AITool}, AgentModeManager?, ToolProviderContext)"/>.
    /// </summary>
    public ToolProviderContext ToolProviderContext => _toolProviderContext;

    /// <summary>
    /// Gets the last created tools for inspection.
    /// </summary>
    public IReadOnlyList<AITool>? LastCreatedTools { get; private set; }

    /// <summary>
    /// Gets the plan store for persisting plan files.
    /// </summary>
    public PlanStore? PlanStore => _planStore;

    /// <summary>
    /// Gets the context compactor for large conversations.
    /// </summary>
    public ContextCompactor? Compactor { get; }

    /// <summary>
    /// Gets the memory consolidator for persisting conversation knowledge.
    /// </summary>
    public MemoryConsolidator? Consolidator { get; }

    /// <summary>
    /// Gets the maximum context tokens from configuration.
    /// </summary>
    public int MaxContextTokens => _config.MaxContextTokens;

    /// <summary>
    /// Gets the memory window (message count threshold for consolidation).
    /// </summary>
    public int MemoryWindow => _config.MemoryWindow;

    /// <summary>
    /// Gets or creates a token tracker for the specified session.
    /// </summary>
    public TokenTracker GetOrCreateTokenTracker(string sessionKey)
    {
        return _tokenTrackers.GetOrAdd(sessionKey, _ => new TokenTracker());
    }

    /// <summary>
    /// Removes the token tracker for the specified session.
    /// </summary>
    public void RemoveTokenTracker(string sessionKey)
    {
        _tokenTrackers.TryRemove(sessionKey, out _);
    }

    /// <summary>
    /// Checks whether the session's message count exceeds <see cref="MemoryWindow"/> and, if so,
    /// returns a Task that performs memory consolidation for the new messages since the last
    /// consolidation. Returns null when conditions are not met (no-op).
    /// The caller decides whether to await the task or fire-and-forget it.
    /// </summary>
    public Task? TryConsolidateMemory(AgentSession session, string sessionKey)
    {
        if (Consolidator == null || _config.MemoryWindow <= 0)
            return null;

        var chatHistory = session.GetService<ChatHistoryProvider>();
        if (chatHistory is not InMemoryChatHistoryProvider memoryProvider)
            return null;

        int messageCount = memoryProvider.Count;
        int lastConsolidated = _lastConsolidated.GetOrAdd(sessionKey, 0);

        int newMessageCount = messageCount - lastConsolidated;
        if (newMessageCount <= _config.MemoryWindow)
            return null;

        lock (_consolidating)
        {
            if (!_consolidating.Add(sessionKey))
                return null;
        }

        // Determine the slice of messages to consolidate:
        // keep the last MemoryWindow/2 messages for continuity.
        int keepCount = _config.MemoryWindow / 2;
        int consolidateEnd = messageCount - keepCount;
        if (consolidateEnd <= lastConsolidated)
        {
            lock (_consolidating) { _consolidating.Remove(sessionKey); }
            return null;
        }

        var toConsolidate = new List<AiChatMessage>();
        for (int i = lastConsolidated; i < consolidateEnd; i++)
            toConsolidate.Add(memoryProvider[i]);

        _lastConsolidated[sessionKey] = consolidateEnd;

        var consolidator = Consolidator;
        return Task.Run(async () =>
        {
            try
            {
                await consolidator.ConsolidateAsync(toConsolidate);
            }
            finally
            {
                lock (_consolidating) { _consolidating.Remove(sessionKey); }
            }
        });
    }

    /// <summary>
    /// Resets the consolidation tracking for the given session (e.g., when session is cleared).
    /// </summary>
    public void ResetConsolidationTracking(string sessionKey)
    {
        _lastConsolidated.TryRemove(sessionKey, out _);
        lock (_consolidating) { _consolidating.Remove(sessionKey); }
    }

    /// <summary>
    /// Tool names that are forbidden in Plan mode.
    /// The system prompt is responsible for restricting Exec to observation-only use.
    /// </summary>
    private static readonly HashSet<string> PlanModeDeniedTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "WriteFile", "EditFile"
    };

    /// <summary>
    /// Creates default tools by aggregating all registered tool providers.
    /// Tools are ordered by provider priority (lower priority value = earlier in list).
    /// </summary>
    public List<AITool> CreateDefaultTools() => CreateDefaultTools(_toolProviderContext);

    /// <summary>
    /// Creates default tools using the given tool context (e.g. per-thread workspace override).
    /// </summary>
    public List<AITool> CreateDefaultTools(ToolProviderContext toolContext)
    {
        var tools = _toolProviders
            .OrderBy(p => p.Priority)
            .SelectMany(p => p.CreateTools(toolContext))
            .ToList();

        // Apply global tool filtering if configured
        if (_globalEnabledToolNames.Count > 0)
        {
            tools = tools
                .Where(t => _globalEnabledToolNames.Contains(t.Name))
                .ToList();
        }

        // Wrap tools with hook interceptors
        tools = ApplyHooks(tools);

        tools = ApplyResultLimits(tools, toolContext.WorkspacePath);

        return tools;
    }

    /// <summary>
    /// Creates tools from an explicit provider list (e.g. registered tool profile).
    /// </summary>
    public List<AITool> CreateToolsFromProviders(
        IReadOnlyList<IAgentToolProvider> providers,
        ToolProviderContext toolContext)
    {
        var tools = providers
            .OrderBy(p => p.Priority)
            .SelectMany(p => p.CreateTools(toolContext))
            .ToList();

        if (_globalEnabledToolNames.Count > 0)
        {
            tools = tools
                .Where(t => _globalEnabledToolNames.Contains(t.Name))
                .ToList();
        }

        tools = ApplyHooks(tools);

        tools = ApplyResultLimits(tools, toolContext.WorkspacePath);

        return tools;
    }

    /// <summary>
    /// Creates tools filtered for the given <see cref="AgentMode"/>.
    /// Plan mode strips write/execute tools.
    /// </summary>
    public List<AITool> CreateToolsForMode(AgentMode mode) => CreateToolsForMode(mode, _toolProviderContext);

    /// <summary>
    /// Creates tools for the given mode using the specified tool context (e.g. per-thread workspace override).
    /// </summary>
    public List<AITool> CreateToolsForMode(AgentMode mode, ToolProviderContext toolContext)
    {
        var tools = CreateDefaultTools(toolContext);

        if (mode == AgentMode.Plan)
        {
            tools.RemoveAll(t => PlanModeDeniedTools.Contains(t.Name));

            if (_planStore != null)
            {
                // Use GetActiveSessionKey for reliable session key retrieval across async boundaries
                var planTools = new PlanTools(_planStore, TracingChatClient.GetActiveSessionKey, _onPlanUpdated);
                tools.Add(AIFunctionFactory.Create(planTools.CreatePlan));
            }
        }
        else if (mode == AgentMode.Agent && _planStore != null)
        {
            // Use GetActiveSessionKey for reliable session key retrieval across async boundaries
            var planTools = new PlanTools(_planStore, TracingChatClient.GetActiveSessionKey, _onPlanUpdated);
            tools.Add(AIFunctionFactory.Create(planTools.UpdateTodos));
            tools.Add(AIFunctionFactory.Create(planTools.TodoWrite));
        }

        tools = ApplyResultLimits(tools, toolContext.WorkspacePath);

        return tools;
    }

    /// <summary>
    /// Creates the default AI agent with all registered tools.
    /// </summary>
    public AIAgent CreateDefaultAgent()
    {
        return CreateAgentWithTools(CreateDefaultTools());
    }

    /// <summary>
    /// Creates an AI agent configured for the specified mode.
    /// </summary>
    public AIAgent CreateAgentForMode(AgentMode mode, AgentModeManager? modeManager = null)
    {
        return CreateAgentWithTools(CreateToolsForMode(mode), modeManager);
    }

    /// <summary>
    /// Creates an AI agent with the specified tools.
    /// </summary>
    public AIAgent CreateAgentWithTools(List<AITool> tools, AgentModeManager? modeManager = null) =>
        BuildAgent(tools, modeManager, _toolProviderContext, instructions: null);

    /// <summary>
    /// Creates an AI agent with the specified tools and tool context (e.g. per-thread workspace override).
    /// </summary>
    public AIAgent CreateAgentWithTools(List<AITool> tools, AgentModeManager? modeManager, ToolProviderContext toolContext) =>
        BuildAgent(tools, modeManager, toolContext, instructions: null);

    /// <summary>
    /// Creates an AI agent with explicit system instructions (e.g. ephemeral commit-message assistant).
    /// </summary>
    public AIAgent CreateAgentWithTools(
        List<AITool> tools,
        AgentModeManager? modeManager,
        ToolProviderContext toolContext,
        string? instructions) =>
        BuildAgent(tools, modeManager, toolContext, instructions);

    private AIAgent BuildAgent(
        List<AITool> tools,
        AgentModeManager? modeManager,
        ToolProviderContext ctx,
        string? instructions = null)
    {
        LastCreatedTools = tools;

        var deferredRegistry = ctx.DeferredToolRegistry;

        // Reverse middleware control:
        // OpenAIChatClient => ImageContentSanitizingChatClient => [DynamicToolInjectionChatClient] => FunctionInvokingChatClient => TracingChatClient
        var chatClientBuilder = new ChatClientBuilder(_chatClient.AsIChatClient());
        if (_traceCollector != null)
        {
            var tc = _traceCollector;
            chatClientBuilder.Use(innerClient => new TracingChatClient(innerClient, tc));
        }
        chatClientBuilder.Use(innerClient =>
        {
            var fic = new FunctionInvokingChatClient(innerClient)
            {
                MaximumIterationsPerRequest = _config.MaxToolCallRounds,
                AllowConcurrentInvocation = true
            };
            if (deferredRegistry != null)
                fic.AdditionalTools = deferredRegistry.ActivatedToolsList;
            return fic;
        });
        if (deferredRegistry != null)
        {
            var registry = deferredRegistry;
            var tc = _traceCollector;
            var hr = _hookRunner;
            chatClientBuilder.Use(innerClient => new DynamicToolInjectionChatClient(innerClient, registry, tc, hr));
        }
        chatClientBuilder.Use(innerClient => new ImageContentSanitizingChatClient(innerClient));
        var configuredChatClient = chatClientBuilder.Build();

        var options = new ChatClientAgentOptions
        {
            Name = "DotCraft",
            UseProvidedChatClientAsIs = true,
            ChatOptions = CreateChatOptions(tools, instructions)
        };

        // Custom instructions: skip MemoryContextProvider so ChatOptions.Instructions is the system prompt (e.g. commit-suggest).
        if (string.IsNullOrWhiteSpace(instructions))
        {
            // When deferred loading is active, derive connected server names from the
            // ToolServerMap so the system prompt can list them for the model.
            IReadOnlyList<string>? deferredServerNames = null;
            if (deferredRegistry != null && ctx.McpClientManager != null)
            {
                deferredServerNames = ctx.McpClientManager.ToolServerMap.Values
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }

            options.AIContextProviderFactory = (_, _) => new ValueTask<AIContextProvider>(
                new MemoryContextProvider(
                    ctx.MemoryStore,
                    ctx.SkillsLoader,
                    ctx.BotPath,
                    ctx.WorkspacePath,
                    _traceCollector,
                    () => tools.Select(t => t.Name).ToArray(),
                    _customCommandLoader,
                    modeManager,
                    _planStore,
                    () => TracingChatClient.CurrentSessionKey,
                    sandboxEnabled: _config.Tools.Sandbox.Enabled,
                    deferredMcpServerNames: deferredServerNames));
        }

        return configuredChatClient.AsAIAgent(options);
    }

    /// <summary>
    /// Creates provider-specific reasoning options based on the current configuration.
    /// Returns <see langword="null"/> when reasoning is disabled.
    /// </summary>
    public ReasoningOptions? CreateReasoningOptions()
    {
        return _config.Reasoning.ToOptions();
    }

    /// <summary>
    /// Creates a chat client with filtering tool call.
    /// </summary>
    public IChatClient CreateToolCallFilteringChatClient()
    {
        var deferredRegistry = _toolProviderContext.DeferredToolRegistry;

        // Reverse middleware control:
        // OpenAIChatClient => ImageContentSanitizingChatClient => [DynamicToolInjectionChatClient] => FunctionInvokingChatClient => TracingChatClient => ToolCallFilteringChatClient
        var chatClientBuilder = new ChatClientBuilder(_chatClient.AsIChatClient());
        chatClientBuilder.Use(innerClient => new ToolCallFilteringChatClient(innerClient));
        if (_traceCollector != null)
        {
            chatClientBuilder.Use(innerClient => new TracingChatClient(innerClient, _traceCollector));
        }
        chatClientBuilder.Use(innerClient =>
        {
            var fic = new FunctionInvokingChatClient(innerClient)
            {
                MaximumIterationsPerRequest = _config.MaxToolCallRounds,
                AllowConcurrentInvocation = true
            };
            if (deferredRegistry != null)
                fic.AdditionalTools = deferredRegistry.ActivatedToolsList;
            return fic;
        });
        if (deferredRegistry != null)
        {
            var registry = deferredRegistry;
            var tc = _traceCollector;
            var hr = _hookRunner;
            chatClientBuilder.Use(innerClient => new DynamicToolInjectionChatClient(innerClient, registry, tc, hr));
        }
        chatClientBuilder.Use(innerClient => new ImageContentSanitizingChatClient(innerClient));
        return chatClientBuilder.Build();
    }

    /// <summary>
    /// Gets the hook runner, if configured.
    /// </summary>
    public HookRunner? HookRunner => _hookRunner;

    /// <summary>
    /// Wraps tools with hook interceptors when PreToolUse/PostToolUse hooks are configured.
    /// Each <see cref="AIFunction"/> is wrapped in a <see cref="HookWrappedFunction"/>
    /// that runs hooks before/after tool execution.
    /// Session ID is resolved dynamically from <see cref="DashBoard.TracingChatClient.CurrentSessionKey"/>.
    /// </summary>
    public List<AITool> ApplyHooks(List<AITool> tools)
    {
        if (_hookRunner == null || !_hookRunner.HasToolHooks)
        {
            if (Diagnostics.DebugModeService.IsEnabled())
                Console.Error.WriteLine($"[Hooks] ApplyHooks: skipped (hookRunner={(_hookRunner == null ? "null" : "present")}, hasToolHooks={_hookRunner?.HasToolHooks})");
            return tools;
        }

        var wrappedCount = 0;
        var result = tools.Select<AITool, AITool>(tool => tool switch
        {
            AIFunction fn => Wrap(fn),
            _ => tool
        }).ToList();

        if (Diagnostics.DebugModeService.IsEnabled())
            Console.Error.WriteLine($"[Hooks] ApplyHooks: wrapped {wrappedCount}/{tools.Count} tools");

        return result;

        AITool Wrap(AIFunction fn)
        {
            wrappedCount++;
            return new HookWrappedFunction(fn, _hookRunner);
        }
    }

    /// <summary>
    /// Wraps each <see cref="AIFunction"/> with <see cref="ResultSizeLimitingFunction"/> so oversized
    /// tool outputs are spilled to disk with a preview. Skips functions already wrapped.
    /// </summary>
    public List<AITool> ApplyResultLimits(List<AITool> tools, string workspacePath)
    {
        var globalMax = _config.Tools.ResultLimits.MaxToolResultChars;
        var previewLines = _config.Tools.ResultLimits.SpillPreviewLines;

        return [.. tools.Select<AITool, AITool>(tool => tool switch
        {
            AIFunction fn when fn is not ResultSizeLimitingFunction => Wrap(fn),
            _ => tool
        })];

        AITool Wrap(AIFunction fn)
        {
            var limit = ToolResultProcessor.ResolveMaxResultChars(fn.Name, globalMax);
            return new ResultSizeLimitingFunction(fn, limit, workspacePath, previewLines);
        }
    }

    private static HashSet<string> ResolveGlobalEnabledToolNames(AppConfig config)
    {
        return config.EnabledTools.Count == 0
            ? []
            : new HashSet<string>(config.EnabledTools, StringComparer.OrdinalIgnoreCase);
    }

    private ChatOptions CreateChatOptions(IEnumerable<AITool> tools, string? instructions = null)
    {
        var chatOptions = new ChatOptions
        {
            Tools = [.. tools],
            Reasoning = CreateReasoningOptions()
        };

        if (!string.IsNullOrWhiteSpace(instructions))
            chatOptions.Instructions = instructions;

        return chatOptions;
    }

    /// <summary>
    /// Disposes all resources created by tool providers.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in _toolProviderContext.DisposableResources)
        {
            await disposable.DisposeAsync();
        }
        _toolProviderContext.DisposableResources.Clear();
    }
}
