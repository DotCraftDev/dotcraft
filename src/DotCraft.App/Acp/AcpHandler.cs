using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Context;
using DotCraft.DashBoard;
using DotCraft.Hooks;
using DotCraft.Memory;
using DotCraft.Mcp;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace DotCraft.Acp;

/// <summary>
/// Core handler for all ACP protocol messages.
/// Manages initialization, sessions, prompt turns, and tool call reporting.
/// </summary>
public sealed class AcpHandler(
    AcpTransport transport,
    SessionStore sessionStore,
    AgentFactory agentFactory,
    AIAgent agent,
    AcpApprovalService approvalService,
    string workspacePath,
    CustomCommandLoader? customCommandLoader = null,
    TraceCollector? traceCollector = null,
    AcpLogger? logger = null,
    PlanStore? planStore = null,
    AcpClientProxy? clientProxy = null,
    HookRunner? hookRunner = null)
{
    // Mutable backing field so it can be rebuilt in HandleInitialize once
    // clientProxy.Capabilities is populated (tool providers depend on extensions).
    private AIAgent _defaultAgent = agent;

    private ClientCapabilities? _clientCapabilities;
    private bool _initialized;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activePrompts = new();
    private readonly ConcurrentDictionary<string, AgentModeManager> _sessionModes = new();
    private readonly ConcurrentDictionary<string, AIAgent> _sessionAgents = new();
    private readonly ConcurrentDictionary<string, McpClientManager> _sessionMcpManagers = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public const int ProtocolVersion = 1;

    /// <summary>
    /// Main message processing loop. Reads requests from transport and dispatches them.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var request = await transport.ReadRequestAsync(ct);
                if (request == null) break; // EOF

                try
                {
                    await DispatchAsync(request, ct);
                }
                catch (Exception ex)
                {
                    logger?.LogError($"Error handling {request.Method}", ex);
                    AnsiConsole.MarkupLine($"[red][[ACP]] Error handling {Markup.Escape(request.Method)}: {Markup.Escape(ex.Message)}[/]");
                    if (!request.IsNotification)
                    {
                        transport.SendError(request.Id, -32603, ex.Message);
                    }
                }
            }
        }
        finally
        {
            await DisposeSessionMcpManagersAsync();
        }
    }

    private async Task DisposeSessionMcpManagersAsync()
    {
        foreach (var sessionId in _sessionMcpManagers.Keys.ToList())
        {
            if (_sessionMcpManagers.TryRemove(sessionId, out var manager))
            {
                try
                {
                    await manager.DisposeAsync();
                }
                catch (Exception ex)
                {
                    logger?.LogError($"Error disposing MCP manager for session {sessionId}", ex);
                }
            }
        }
    }

    private async Task DispatchAsync(JsonRpcRequest request, CancellationToken ct)
    {
        switch (request.Method)
        {
            case AcpMethods.Initialize:
                HandleInitialize(request);
                break;

            case AcpMethods.SessionNew:
                await HandleSessionNewAsync(request, ct);
                break;

            case AcpMethods.SessionLoad:
                await HandleSessionLoadAsync(request, ct);
                break;

            case AcpMethods.SessionList:
                HandleSessionList(request);
                break;

            case AcpMethods.DotCraftSessionDelete:
                await HandleDotCraftSessionDeleteAsync(request, ct);
                break;

            case AcpMethods.SessionPrompt:
                // Run prompt asynchronously so we can handle cancellation.
                // Exceptions must be caught here; the discarded Task would swallow them.
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandleSessionPromptAsync(request, ct);
                    }
                    catch (Exception ex)
                    {
                        logger?.LogError($"Unhandled prompt exception", ex);
                        transport.SendError(request.Id, -32603, ex.Message);
                    }
                }, ct);
                break;

            case AcpMethods.SessionCancel:
                HandleSessionCancel(request);
                break;

            case AcpMethods.SessionMode:
                HandleSessionMode(request);
                break;

            case AcpMethods.SessionSetConfigOption:
                HandleSessionSetConfigOption(request);
                break;

            default:
                if (!request.IsNotification)
                {
                    transport.SendError(request.Id, -32601, $"Method not found: {request.Method}");
                }
                break;
        }
    }

    private void HandleInitialize(JsonRpcRequest request)
    {
        var p = Deserialize<InitializeParams>(request.Params);

        _clientCapabilities = p?.ClientCapabilities;

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

        var result = new InitializeResult
        {
            ProtocolVersion = ProtocolVersion,
            AgentCapabilities = new AgentCapabilities
            {
                LoadSession = true,
                ListSessions = true,
                PromptCapabilities = new PromptCapabilities
                {
                    Text = true,
                    EmbeddedContext = true
                },
                McpCapabilities = new McpCapabilities { Http = true },
                Meta = new AgentCapabilitiesMeta
                {
                    DotCraft = new DotCraftAgentCapabilities
                    {
                        SessionDelete = true
                    }
                }
            },
            AgentInfo = new AgentInfo
            {
                Name = "DotCraft",
                Title = "DotCraft AI Agent",
                Version = version
            }
        };

        _initialized = true;

        // Update capabilities on the existing proxy, then rebuild the default agent so
        // that tool providers (e.g. UnityAcpToolProvider) can see the client extensions.
        if (clientProxy != null)
        {
            clientProxy.Capabilities = _clientCapabilities;
            _defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        }

        transport.SendResponse(request.Id, result);

        var caps = new List<string>();
        if (clientProxy?.SupportsFileRead == true) caps.Add("fs.read");
        if (clientProxy?.SupportsFileWrite == true) caps.Add("fs.write");
        if (clientProxy?.SupportsTerminal == true) caps.Add("terminal");
        if (clientProxy?.Extensions is { Count: > 0 } exts)
            caps.AddRange(exts.Select(e => $"ext:{e}"));
        var capsStr = caps.Count > 0 ? string.Join(", ", caps) : "none";
        logger?.LogEvent($"Initialized (client capabilities: {capsStr})");
        AnsiConsole.MarkupLine($"[green][[ACP]][/] Initialized (client capabilities: {Markup.Escape(capsStr)})");
    }

    private async Task HandleSessionNewAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!EnsureInitialized(request)) return;

        var p = Deserialize<SessionNewParams>(request.Params);
        var sessionId = $"acp_{SessionStore.GenerateSessionId()}";

        approvalService.SetSessionId(sessionId);
        _sessionModes[sessionId] = new AgentModeManager();

        if (p?.McpServers is { Count: > 0 })
        {
            await ConnectSessionMcpAsync(sessionId, p.McpServers, ct);
            _sessionAgents[sessionId] = CreateSessionAgentWithMcp(sessionId, AgentMode.Agent);
        }

        // Persist the new (empty) session to disk immediately so it appears in session/list
        var currentAgent = _sessionAgents.GetValueOrDefault(sessionId, _defaultAgent);
        var newSession = await sessionStore.LoadOrCreateAsync(currentAgent, sessionId, ct);
        await sessionStore.SaveAsync(currentAgent, newSession, sessionId, ct);

        // Run SessionStart hooks
        if (hookRunner != null)
        {
            var hookInput = new HookInput { SessionId = sessionId };
            await hookRunner.RunAsync(HookEvent.SessionStart, hookInput, ct);
        }

        var result = new SessionNewResult
        {
            SessionId = sessionId,
            ConfigOptions = BuildConfigOptions()
        };

        transport.SendResponse(request.Id, result);

        // Send slash commands after session creation
        BroadcastSlashCommands(sessionId);

        logger?.LogEvent($"Session created: {sessionId}");
        AnsiConsole.MarkupLine($"[green][[ACP]][/] Session created: {Markup.Escape(sessionId)}");
    }

    private async Task HandleSessionLoadAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!EnsureInitialized(request)) return;

        var p = Deserialize<SessionLoadParams>(request.Params);
        if (p == null)
        {
            transport.SendError(request.Id, -32602, "Invalid params");
            return;
        }

        var sessionId = p.SessionId;
        approvalService.SetSessionId(sessionId);

        if (p.McpServers is { Count: > 0 })
        {
            await ConnectSessionMcpAsync(sessionId, p.McpServers, ct);
            _sessionModes.TryAdd(sessionId, new AgentModeManager());
            _sessionAgents[sessionId] = CreateSessionAgentWithMcp(sessionId, AgentMode.Agent);
        }

        // Load the session to get chat history
        var currentAgent = _sessionAgents.GetValueOrDefault(sessionId, _defaultAgent);
        var session = await sessionStore.LoadOrCreateAsync(currentAgent, sessionId, ct);
        var chatHistory = session.GetService<ChatHistoryProvider>();

        if (chatHistory is InMemoryChatHistoryProvider memoryProvider)
        {
            // Replay conversation as session/update notifications
            foreach (var msg in memoryProvider)
            {
                if (msg.Role == ChatRole.User)
                {
                    transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
                    {
                        SessionId = sessionId,
                        Update = new AcpSessionUpdate
                        {
                            SessionUpdate = AcpUpdateKind.UserMessageChunk,
                            Content = new AcpContentBlock { Type = "text", Text = StripRuntimeContext(msg.Text) }
                        }
                    });
                }
                else if (msg.Role == ChatRole.Assistant && !string.IsNullOrEmpty(msg.Text))
                {
                    transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
                    {
                        SessionId = sessionId,
                        Update = new AcpSessionUpdate
                        {
                            SessionUpdate = AcpUpdateKind.AgentMessageChunk,
                            Content = new AcpContentBlock { Type = "text", Text = msg.Text }
                        }
                    });
                }
            }
        }

        var loadedMode = _sessionModes.TryGetValue(sessionId, out var mm)
            ? mm.CurrentMode.ToString().ToLower()
            : "agent";
        transport.SendResponse(request.Id, new SessionLoadResult
        {
            SessionId = sessionId,
            ConfigOptions = BuildConfigOptions(loadedMode)
        });
        // Run SessionStart hooks for loaded sessions
        if (hookRunner != null)
        {
            var hookInput = new HookInput { SessionId = sessionId };
            await hookRunner.RunAsync(HookEvent.SessionStart, hookInput, ct);
        }

        BroadcastSlashCommands(sessionId);
        logger?.LogEvent($"Session loaded: {sessionId}");
        AnsiConsole.MarkupLine($"[green][[ACP]][/] Session loaded: {Markup.Escape(sessionId)}");
    }

    private void HandleSessionList(JsonRpcRequest request)
    {
        if (!EnsureInitialized(request)) return;

        var sessions = sessionStore.ListSessions()
            .Where(s => s.Key.StartsWith("acp_"))
            .Select(s => new SessionListEntry
            {
                SessionId = s.Key,
                UpdatedAt = s.UpdatedAt,
                Cwd = workspacePath
            })
            .ToList();

        transport.SendResponse(request.Id, new SessionListResult { Sessions = sessions });
    }

    private async Task HandleDotCraftSessionDeleteAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!EnsureInitialized(request)) return;

        var p = Deserialize<SessionDeleteParams>(request.Params);
        if (p == null || string.IsNullOrWhiteSpace(p.SessionId))
        {
            transport.SendError(request.Id, -32602, "Invalid params");
            return;
        }

        var sessionId = p.SessionId;

        if (_activePrompts.TryRemove(sessionId, out var promptCts))
        {
            promptCts.Cancel();
            promptCts.Dispose();
        }

        sessionStore.Delete(sessionId);
        planStore?.DeletePlan(sessionId);
        _sessionModes.TryRemove(sessionId, out _);
        _sessionAgents.TryRemove(sessionId, out _);
        await DisposeSessionMcpManagerAsync(sessionId);

        transport.SendResponse(request.Id, new SessionDeleteResult());
    }

    private async Task HandleSessionPromptAsync(JsonRpcRequest request, CancellationToken ct)
    {
        if (!EnsureInitialized(request)) return;

        var p = Deserialize<SessionPromptParams>(request.Params);
        if (p == null)
        {
            transport.SendError(request.Id, -32602, "Invalid params");
            return;
        }

        var sessionId = p.SessionId;
        approvalService.SetSessionId(sessionId);

        using var promptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activePrompts[sessionId] = promptCts;

        try
        {
            // Extract prompt text from content blocks
            var promptText = ExtractPromptText(p.Prompt);

            // Handle slash commands
            if (p.Command != null)
            {
                promptText = $"/{p.Command} {promptText}".Trim();
            }

            if (customCommandLoader != null && promptText.StartsWith('/'))
            {
                var resolved = customCommandLoader.TryResolve(promptText);
                if (resolved != null)
                    promptText = resolved.ExpandedPrompt;
            }

            promptText = RuntimeContextBuilder.AppendTo(promptText);

            // Run PrePrompt hooks (can block the prompt)
            if (hookRunner != null)
            {
                var prePromptInput = new HookInput { SessionId = sessionId, Prompt = promptText };
                var prePromptResult = await hookRunner.RunAsync(HookEvent.PrePrompt, prePromptInput, promptCts.Token);
                if (prePromptResult.Blocked)
                {
                    logger?.LogEvent($"Prompt blocked by hook [session={sessionId}]: {prePromptResult.BlockReason}");
                    transport.SendResponse(request.Id, new SessionPromptResult
                    {
                        StopReason = AcpStopReason.EndTurn
                    });
                    return;
                }
            }

            logger?.LogEvent($"Prompt start [session={sessionId}]: {(promptText.Length > 200 ? promptText[..200] + "..." : promptText)}");

            // Set up tracing
            TracingChatClient.CurrentSessionKey = sessionId;
            TracingChatClient.ResetCallState(sessionId);

            traceCollector?.RecordSessionMetadata(
                sessionId, null,
                agentFactory.LastCreatedTools?.Select(t => t.Name));

        var currentAgent = _sessionAgents.GetValueOrDefault(sessionId, _defaultAgent);
        var session = await sessionStore.LoadOrCreateAsync(currentAgent, sessionId, promptCts.Token);
            long inputTokens = 0, outputTokens = 0, totalTokens = 0;
            var tokenTracker = agentFactory.GetOrCreateTokenTracker(sessionId);

            try
            {
                await foreach (var update in currentAgent.RunStreamingAsync(promptText, session, cancellationToken: promptCts.Token))
                {
                    foreach (var content in update.Contents)
                    {
                        switch (content)
                        {
                            case FunctionCallContent fc:
                                SendToolCallStarted(sessionId, fc);
                                break;

                            case FunctionResultContent fr:
                                SendToolCallCompleted(sessionId, fr);
                                break;

                            case UsageContent usage:
                                if (usage.Details.InputTokenCount.HasValue)
                                    inputTokens = usage.Details.InputTokenCount.Value;
                                if (usage.Details.OutputTokenCount.HasValue)
                                    outputTokens = usage.Details.OutputTokenCount.Value;
                                if (usage.Details.TotalTokenCount.HasValue)
                                    totalTokens = usage.Details.TotalTokenCount.Value;
                                break;
                        }
                    }

                    if (!string.IsNullOrEmpty(update.Text))
                    {
                        SendMessageChunk(sessionId, update.Text);
                    }
                }
            }
            finally
            {
                TracingChatClient.ResetCallState(sessionId);
                TracingChatClient.CurrentSessionKey = null;
            }

            if (totalTokens == 0 && (inputTokens > 0 || outputTokens > 0))
                totalTokens = inputTokens + outputTokens;

            await sessionStore.SaveAsync(currentAgent, session, sessionId, CancellationToken.None);

            // Run Stop hooks after agent finishes responding
            if (hookRunner != null)
            {
                var stopInput = new HookInput { SessionId = sessionId };
                await hookRunner.RunAsync(HookEvent.Stop, stopInput, CancellationToken.None);
            }

            // Send structured plan update whenever a plan exists (covers both
            // plan creation in Plan mode and todo updates in Agent mode)
            if (planStore != null && planStore.StructuredPlanExists(sessionId))
            {
                var structuredPlan = await planStore.LoadStructuredPlanAsync(sessionId);
                if (structuredPlan != null)
                    SendPlanUpdate(sessionId, structuredPlan);
            }

            if (totalTokens > 0)
                tokenTracker.Update(inputTokens, outputTokens);

            // Handle context compaction
            if (agentFactory is { Compactor: not null, MaxContextTokens: > 0 } &&
                inputTokens >= agentFactory.MaxContextTokens)
            {
                if (await agentFactory.Compactor.TryCompactAsync(session, promptCts.Token))
                {
                    tokenTracker.Reset();
                    traceCollector?.RecordContextCompaction(sessionId);
                }
            }

            _ = agentFactory.TryConsolidateMemory(session, sessionId);

            logger?.LogEvent($"Prompt complete [session={sessionId}] stop=end_turn tokens(in={inputTokens},out={outputTokens},total={totalTokens})");

            transport.SendResponse(request.Id, new SessionPromptResult
            {
                StopReason = AcpStopReason.EndTurn
            });
        }
        catch (OperationCanceledException)
        {
            logger?.LogEvent($"Prompt cancelled [session={sessionId}]");
            transport.SendResponse(request.Id, new SessionPromptResult
            {
                StopReason = AcpStopReason.Cancelled
            });
        }
        catch (Exception ex)
        {
            logger?.LogError($"Prompt error [session={sessionId}]", ex);
            AnsiConsole.MarkupLine($"[red][[ACP]][/] Prompt error: {Markup.Escape(ex.Message)}");
            transport.SendError(request.Id, -32603, ex.Message);
        }
        finally
        {
            _activePrompts.TryRemove(sessionId, out _);
        }
    }

    private void HandleSessionCancel(JsonRpcRequest request)
    {
        var p = Deserialize<SessionCancelParams>(request.Params);
        if (p == null) return;

        if (_activePrompts.TryGetValue(p.SessionId, out var cts))
        {
            cts.Cancel();
            logger?.LogEvent($"Session cancel requested: {p.SessionId}");
            AnsiConsole.MarkupLine($"[yellow][[ACP]][/] Cancelled session: {Markup.Escape(p.SessionId)}");
        }
    }

    // ───── Helper methods ─────

    private static List<McpServerConfig> ConvertToMcpConfigs(List<AcpMcpServer> mcpServers)
    {
        var configs = new List<McpServerConfig>();
        foreach (var s in mcpServers)
        {
            if (string.IsNullOrWhiteSpace(s.Name))
                continue;
            var transport = string.IsNullOrEmpty(s.Type) || s.Type.Equals("stdio", StringComparison.OrdinalIgnoreCase)
                ? "stdio"
                : s.Type.Equals("http", StringComparison.OrdinalIgnoreCase)
                    ? "http"
                    : null;
            if (transport == null)
                continue; // e.g. "sse" not supported
            var config = new McpServerConfig
            {
                Name = s.Name,
                Enabled = true,
                Transport = transport,
                Command = s.Command ?? "",
                Arguments = s.Args ?? [],
                EnvironmentVariables = s.Env != null ? s.Env.ToDictionary(e => e.Name, e => e.Value) : new Dictionary<string, string>(),
                Url = s.Url ?? "",
                Headers = s.Headers != null ? s.Headers.ToDictionary(h => h.Name, h => h.Value) : new Dictionary<string, string>()
            };
            configs.Add(config);
        }
        return configs;
    }

    private async Task ConnectSessionMcpAsync(string sessionId, List<AcpMcpServer> mcpServers, CancellationToken ct)
    {
        var configs = ConvertToMcpConfigs(mcpServers);
        if (configs.Count == 0)
            return;
        var manager = new McpClientManager();
        await manager.ConnectAsync(configs, ct);
        _sessionMcpManagers[sessionId] = manager;
    }

    private async Task DisposeSessionMcpManagerAsync(string sessionId)
    {
        if (!_sessionMcpManagers.TryRemove(sessionId, out var manager))
            return;

        try
        {
            await manager.DisposeAsync();
        }
        catch (Exception ex)
        {
            logger?.LogError($"Error disposing MCP manager for session {sessionId}", ex);
        }
    }

    private AIAgent CreateSessionAgentWithMcp(string sessionId, AgentMode mode)
    {
        var modeManager = _sessionModes.GetValueOrDefault(sessionId);
        var tools = agentFactory.CreateToolsForMode(mode);
        if (_sessionMcpManagers.TryGetValue(sessionId, out var mcpManager) && mcpManager.Tools.Count > 0)
        {
            foreach (var t in mcpManager.Tools)
                tools.Add(t);
        }
        return agentFactory.CreateAgentWithTools(tools, modeManager);
    }

    private void SendMessageChunk(string sessionId, string text)
    {
        transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
        {
            SessionId = sessionId,
            Update = new AcpSessionUpdate
            {
                SessionUpdate = AcpUpdateKind.AgentMessageChunk,
                Content = new AcpContentBlock { Type = "text", Text = text }
            }
        });
    }

    internal void SendPlanUpdate(string sessionId, StructuredPlan plan)
    {
        var entries = plan.Todos.Select(t => new AcpPlanEntry
        {
            Content = t.Content,
            Priority = t.Priority switch
            {
                PlanTodoPriority.High => AcpPlanEntryPriority.High,
                PlanTodoPriority.Low => AcpPlanEntryPriority.Low,
                _ => AcpPlanEntryPriority.Medium
            },
            Status = t.Status switch
            {
                PlanTodoStatus.InProgress => AcpToolStatus.InProgress,
                PlanTodoStatus.Completed or PlanTodoStatus.Cancelled => AcpToolStatus.Completed,
                _ => AcpToolStatus.Pending
            }
        }).ToList();

        transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
        {
            SessionId = sessionId,
            Update = new AcpSessionUpdate
            {
                SessionUpdate = AcpUpdateKind.Plan,
                Entries = entries
            }
        });
    }

    private void SendToolCallStarted(string sessionId, FunctionCallContent fc)
    {
        var kind = AcpToolKindMapper.GetKind(fc.Name);
        var filePaths = AcpToolKindMapper.ExtractFilePaths(fc.Name, fc.Arguments);

        var argsStr = string.Empty;
        if (fc.Arguments != null)
        {
            try
            {
                argsStr = JsonSerializer.Serialize(fc.Arguments, JsonOptions);
            }
            catch
            {
                argsStr = fc.Arguments.ToString() ?? "";
            }
        }

        transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
        {
            SessionId = sessionId,
            Update = new AcpSessionUpdate
            {
                SessionUpdate = AcpUpdateKind.ToolCall,
                ToolCallId = fc.CallId,
                Title = fc.Name,
                Kind = kind,
                Status = AcpToolStatus.InProgress,
                Content = string.IsNullOrEmpty(argsStr)
                    ? null
                    : new List<AcpContentBlock> { new() { Type = "text", Text = argsStr } },
                FileLocations = filePaths?.Select(p => new AcpFileLocation { Uri = $"file://{p}" }).ToList()
            }
        });
    }

    private void SendToolCallCompleted(string sessionId, FunctionResultContent fr)
    {
        var resultText = ImageContentSanitizingChatClient.DescribeResult(fr.Result);
        var preview = resultText.Length > 500 ? resultText[..500] + "..." : resultText;

        transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
        {
            SessionId = sessionId,
            Update = new AcpSessionUpdate
            {
                SessionUpdate = AcpUpdateKind.ToolCallUpdate,
                ToolCallId = fr.CallId,
                Status = fr.Result is Exception ? AcpToolStatus.Failed : AcpToolStatus.Completed,
                Content = new List<AcpContentBlock> { new() { Type = "text", Text = preview } }
            }
        });
    }

    private void BroadcastSlashCommands(string sessionId)
    {
        if (customCommandLoader == null) return;

        var commands = customCommandLoader.ListCommands()
            .Select(c => new AcpSlashCommand
            {
                Name = c.Name,
                Description = string.IsNullOrWhiteSpace(c.Description) ? null : c.Description
            })
            .ToList();

        if (commands.Count == 0) return;

        transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
        {
            SessionId = sessionId,
            Update = new AcpSessionUpdate
            {
                SessionUpdate = AcpUpdateKind.AvailableCommandsUpdate,
                Commands = commands
            }
        });
    }

    private static List<ConfigOption> BuildConfigOptions(string currentMode = "agent")
    {
        return
        [
            new ConfigOption
            {
                Id = "mode",
                Name = "Mode",
                Category = "mode",
                CurrentValue = currentMode,
                Options =
                [
                    new ConfigOptionValue { Value = "agent", Name = "Agent", Description = "Full agent mode with all tools" },
                    new ConfigOptionValue { Value = "plan", Name = "Plan", Description = "Read-only planning mode" }
                ]
            }
        ];
    }

    private void HandleSessionMode(JsonRpcRequest request)
    {
        if (!EnsureInitialized(request)) return;

        var p = Deserialize<SessionModeParams>(request.Params);
        if (p == null)
        {
            transport.SendError(request.Id, -32602, "Invalid params");
            return;
        }

        var sessionId = p.SessionId;
        var modeManager = _sessionModes.GetOrAdd(sessionId, _ => new AgentModeManager());

        var newMode = p.Mode?.ToLowerInvariant() switch
        {
            "plan" => AgentMode.Plan,
            _ => AgentMode.Agent
        };

        modeManager.SwitchMode(newMode);

        // Rebuild the agent with mode-appropriate tools (preserve session MCP if present)
        var newAgent = _sessionMcpManagers.ContainsKey(sessionId)
            ? CreateSessionAgentWithMcp(sessionId, newMode)
            : agentFactory.CreateAgentForMode(newMode, modeManager);
        _sessionAgents[sessionId] = newAgent;

        // Respond and notify
        transport.SendResponse(request.Id, new { mode = newMode.ToString().ToLower() });
        SendModeUpdate(sessionId, newMode);

        logger?.LogEvent($"Mode changed [session={sessionId}]: {newMode.ToString().ToLower()}");
        AnsiConsole.MarkupLine($"[green][[ACP]][/] Mode changed to {newMode.ToString().ToLower()} [[session={Markup.Escape(sessionId)}]]");
    }

    private void HandleSessionSetConfigOption(JsonRpcRequest request)
    {
        if (!EnsureInitialized(request)) return;

        var p = Deserialize<SessionSetConfigOptionParams>(request.Params);
        if (p == null)
        {
            transport.SendError(request.Id, -32602, "Invalid params");
            return;
        }

        var sessionId = p.SessionId;

        if (p.ConfigId == "mode")
        {
            var modeManager = _sessionModes.GetOrAdd(sessionId, _ => new AgentModeManager());
            var newMode = p.Value.ToLowerInvariant() switch
            {
                "plan" => AgentMode.Plan,
                _ => AgentMode.Agent
            };

            modeManager.SwitchMode(newMode);

            var newAgent = _sessionMcpManagers.ContainsKey(sessionId)
                ? CreateSessionAgentWithMcp(sessionId, newMode)
                : agentFactory.CreateAgentForMode(newMode, modeManager);
            _sessionAgents[sessionId] = newAgent;

            var updatedOptions = BuildConfigOptions(p.Value.ToLowerInvariant());
            transport.SendResponse(request.Id, new SessionSetConfigOptionResult { ConfigOptions = updatedOptions });

            // Notify client of the config change
            transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
            {
                SessionId = sessionId,
                Update = new AcpSessionUpdate
                {
                    SessionUpdate = AcpUpdateKind.ConfigOptionsUpdate,
                    ConfigOptions = updatedOptions
                }
            });

            logger?.LogEvent($"Config option set [session={sessionId}]: mode={p.Value}");
            AnsiConsole.MarkupLine($"[green][[ACP]][/] Config option set: mode={Markup.Escape(p.Value)} [[session={Markup.Escape(sessionId)}]]");
        }
        else
        {
            transport.SendError(request.Id, -32602, $"Unknown configId: {p.ConfigId}");
        }
    }

    private void SendModeUpdate(string sessionId, AgentMode mode)
    {
        transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
        {
            SessionId = sessionId,
            Update = new AcpSessionUpdate
            {
                SessionUpdate = AcpUpdateKind.CurrentModeUpdate,
                Content = new AcpContentBlock { Type = "text", Text = mode.ToString().ToLower() }
            }
        });
    }

    private static string ExtractPromptText(List<AcpContentBlock> prompt)
    {
        if (prompt.Count == 0)
            return "";

        var sb = new StringBuilder();
        foreach (var block in prompt)
        {
            switch (block.Type)
            {
                case "text" when !string.IsNullOrEmpty(block.Text):
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(block.Text);
                    break;

                case "resource" when block.Resource != null:
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append($"[File: {block.Resource.Uri}]");
                    if (!string.IsNullOrEmpty(block.Resource.Text))
                    {
                        sb.Append('\n');
                        sb.Append(block.Resource.Text);
                    }
                    break;
            }
        }

        return sb.ToString();
    }

    private bool EnsureInitialized(JsonRpcRequest request)
    {
        if (_initialized) return true;
        transport.SendError(request.Id, -32002, "Agent not initialized. Call 'initialize' first.");
        return false;
    }

    /// <summary>
    /// Removes the [Runtime Context] block appended by RuntimeContextBuilder from a stored user message,
    /// so that dynamic metadata (timestamps, etc.) is not shown in session history replay.
    /// </summary>
    private static string StripRuntimeContext(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var idx = text.IndexOf("\n\n[Runtime Context]", StringComparison.Ordinal);
        return idx >= 0 ? text[..idx] : text;
    }

    private static T? Deserialize<T>(JsonElement? element) where T : class
    {
        if (element == null || element.Value.ValueKind == JsonValueKind.Undefined)
            return null;
        return element.Value.Deserialize<T>(JsonOptions);
    }
}
