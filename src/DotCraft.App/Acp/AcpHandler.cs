using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Context;
using DotCraft.Tracing;
using DotCraft.Hooks;
using DotCraft.Memory;
using DotCraft.Mcp;
using DotCraft.Sessions.Protocol;
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
    AgentFactory agentFactory,
    AIAgent agent,
    AcpApprovalService approvalService,
    string workspacePath,
    ISessionService sessionService,
    CustomCommandLoader? customCommandLoader = null,
    TokenUsageStore? tokenUsageStore = null,
    AcpLogger? logger = null,
    PlanStore? planStore = null,
    AcpClientProxy? clientProxy = null,
    HookRunner? hookRunner = null)
{
    // Identity used for thread discovery/creation in Session Protocol path
    private readonly SessionIdentity _acpIdentity = new()
    {
        ChannelName = "acp",
        UserId = "local",
        WorkspacePath = workspacePath
    };
    // Mutable backing field so it can be rebuilt in HandleInitialize once
    // clientProxy.Capabilities is populated (tool providers depend on extensions).
    private AIAgent _defaultAgent = agent;

    private ClientCapabilities? _clientCapabilities;
    private bool _initialized;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _activePrompts = new();
    // Sessions created via session/new but not yet materialized (no messages sent).
    // Key = placeholder thread ID; value is unused (byte as sentinel).
    private readonly ConcurrentDictionary<string, byte> _pendingSessions = new();
    // Optional per-session MCP config stored during session/new, consumed during materialization.
    private readonly ConcurrentDictionary<string, ThreadConfiguration> _pendingConfigs = new();
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

    private static Task DisposeSessionMcpManagersAsync() => Task.CompletedTask;

    /// <summary>
    /// Materializes a pending session on first use: creates and persists the thread using the
    /// pre-assigned ID. No-op if the session has already been materialized.
    /// </summary>
    private async Task EnsureThreadMaterializedAsync(string sessionId, CancellationToken ct)
    {
        if (!_pendingSessions.TryRemove(sessionId, out _))
            return;

        _pendingConfigs.TryRemove(sessionId, out var config);
        await sessionService.CreateThreadAsync(_acpIdentity, config, threadId: sessionId, ct: ct);
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
                    Image = true,
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

        // Generate the thread ID eagerly so the client gets a stable ID,
        // but defer disk writes until the first actual prompt arrives.
        var sessionId = SessionIdGenerator.NewThreadId();
        _pendingSessions.TryAdd(sessionId, 0);

        // Store MCP config alongside the pending session so it can be used during materialization.
        if (p?.McpServers is { Count: > 0 })
        {
            var config = new ThreadConfiguration { McpServers = ConvertToMcpConfigs(p.McpServers).ToArray() };
            _pendingConfigs.TryAdd(sessionId, config);
        }

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

        // Resume the Thread via Session Protocol
        var resumedThread = await sessionService.ResumeThreadAsync(sessionId, ct);

        // Replay conversation history as session/update notifications
        foreach (var turn in resumedThread.Turns)
        {
            foreach (var item in turn.Items)
            {
                if (item.Type == ItemType.UserMessage && item.Payload is UserMessagePayload userMsg)
                {
                    transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
                    {
                        SessionId = sessionId,
                        Update = new AcpSessionUpdate
                        {
                            SessionUpdate = AcpUpdateKind.UserMessageChunk,
                            Content = new AcpContentBlock { Type = "text", Text = userMsg.Text }
                        }
                    });
                }
                else if (item.Type == ItemType.AgentMessage && item.Payload is AgentMessagePayload agentMsg)
                {
                    transport.SendNotification(AcpMethods.SessionUpdate, new SessionUpdateParams
                    {
                        SessionId = sessionId,
                        Update = new AcpSessionUpdate
                        {
                            SessionUpdate = AcpUpdateKind.AgentMessageChunk,
                            Content = new AcpContentBlock { Type = "text", Text = agentMsg.Text }
                        }
                    });
                }
            }
        }

        var loadedMode = resumedThread.Configuration?.Mode ?? "agent";
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

        var threads = sessionService.FindThreadsAsync(_acpIdentity).GetAwaiter().GetResult();
        var sessions = threads
            .Select(t => new SessionListEntry
            {
                SessionId = t.Id,
                UpdatedAt = t.LastActiveAt.ToString("O"),
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

        // If the session was never materialized, just discard the pending state.
        if (_pendingSessions.TryRemove(sessionId, out _))
        {
            _pendingConfigs.TryRemove(sessionId, out _);
        }
        else
        {
            await sessionService.ArchiveThreadAsync(sessionId, ct);
        }

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

        // Materialize the thread on first use so empty threads are never written to disk.
        await EnsureThreadMaterializedAsync(sessionId, ct);

        approvalService.SetSessionId(sessionId);

        using var promptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _activePrompts[sessionId] = promptCts;

        try
        {
            // Extract plain text for commands and hooks
            var promptText = ExtractPromptText(p.Prompt);

            // Handle slash commands
            var commandExpanded = false;
            if (p.Command != null)
            {
                promptText = $"/{p.Command} {promptText}".Trim();
            }

            if (customCommandLoader != null && promptText.StartsWith('/'))
            {
                var resolved = customCommandLoader.TryResolve(promptText);
                if (resolved != null)
                {
                    promptText = resolved.ExpandedPrompt;
                    commandExpanded = true;
                }
            }

            // Build multimodal content: use expanded command text or original prompt blocks
            IList<AIContent> contentParts;
            if (commandExpanded)
            {
                contentParts = [new TextContent(promptText)];
            }
            else if (p.Command != null)
            {
                // Command was set but not resolved ??preserve the /{command} prefix as text,
                // plus any non-text blocks (e.g. images) from the original prompt.
                contentParts = new List<AIContent> { new TextContent(promptText) };
                foreach (var block in p.Prompt)
                {
                    if (!string.IsNullOrEmpty(block.Data) &&
                        !string.IsNullOrEmpty(block.MimeType) &&
                        block.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        contentParts.Add(new DataContent(Convert.FromBase64String(block.Data), block.MimeType));
                    }
                }
            }
            else
            {
                contentParts = BuildPromptContent(p.Prompt);
            }

            RuntimeContextBuilder.AppendTo(contentParts);

            // Run PrePrompt hooks (can block the prompt)
            if (hookRunner != null)
            {
                var hookPromptText = RuntimeContextBuilder.AppendTo(promptText);
                var prePromptInput = new HookInput { SessionId = sessionId, Prompt = hookPromptText };
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

            {
                var handler = new SessionEventHandler
                {
                    OnTextDelta = text => { SendMessageChunk(sessionId, text); return Task.CompletedTask; },
                    OnReasoningDelta = reasoning =>
                    {
                        SendMessageChunk(sessionId, ReasoningContentHelper.FormatBlock(reasoning));
                        return Task.CompletedTask;
                    },
                    OnToolStarted = (toolName, _, _, callId) =>
                    {
                        var fc = new FunctionCallContent(callId, toolName, null);
                        SendToolCallStarted(sessionId, fc);
                        return Task.CompletedTask;
                    },
                    OnToolCompleted = (callId, result) =>
                    {
                        var fr = new FunctionResultContent(callId, result ?? string.Empty);
                        SendToolCallCompleted(sessionId, fr);
                        return Task.CompletedTask;
                    },
                    OnApprovalRequested = async req =>
                    {
                        var toolKind = req.ApprovalType == "shell"
                            ? AcpToolKind.Execute
                            : req.Operation.ToLowerInvariant() switch
                            {
                                "write" or "edit" => AcpToolKind.Edit,
                                "delete" => AcpToolKind.Delete,
                                _ => AcpToolKind.Read
                            };
                        var toolCall = new AcpToolCallInfo
                        {
                            ToolCallId = req.RequestId,
                            Title = req.ApprovalType == "shell"
                                ? $"Shell: {(req.Operation.Length > 80 ? req.Operation[..80] + "..." : req.Operation)}"
                                : $"File {req.Operation}: {req.Target}",
                            Kind = toolKind,
                            Status = AcpToolStatus.Pending
                        };
                        var permParams = new RequestPermissionParams
                        {
                            SessionId = sessionId,
                            ToolCall = toolCall,
                            Options =
                            [
                                new PermissionOption { OptionId = "allow-once",   Name = "Allow once",   Kind = AcpPermissionKind.AllowOnce   },
                                new PermissionOption { OptionId = "allow-always", Name = "Allow always", Kind = AcpPermissionKind.AllowAlways },
                                new PermissionOption { OptionId = "reject-once",  Name = "Reject",       Kind = AcpPermissionKind.RejectOnce  },
                            ]
                        };
                        try
                        {
                            var resultElement = await transport.SendClientRequestAsync(
                                AcpMethods.RequestPermission, permParams,
                                timeout: TimeSpan.FromSeconds(120));
                            var result = resultElement.Deserialize<RequestPermissionResult>();
                            return result?.Outcome?.OptionId is "allow-once" or "allow-always";
                        }
                        catch
                        {
                            return false;
                        }
                    },
                    OnTurnCompleted = usage =>
                    {
                        if (usage != null)
                        {
                            tokenUsageStore?.Record(new TokenUsageRecord
                            {
                                Channel = "acp",
                                UserId = sessionId,
                                DisplayName = sessionId,
                                InputTokens = usage.InputTokens,
                                OutputTokens = usage.OutputTokens
                            });
                        }
                        return Task.CompletedTask;
                    }
                };

                await handler.ProcessAsync(
                    sessionService.SubmitInputAsync(sessionId, promptText, ct: promptCts.Token),
                    (tid, rid, ok) => sessionService.ResolveApprovalAsync(tid, rid, ok, promptCts.Token),
                    promptCts.Token);

                // Run Stop hooks
                if (hookRunner != null)
                {
                    var stopInput = new HookInput { SessionId = sessionId };
                    await hookRunner.RunAsync(HookEvent.Stop, stopInput, CancellationToken.None);
                }

                // Send structured plan update (covers Plan mode creation and Agent mode todo updates)
                if (planStore != null && planStore.StructuredPlanExists(sessionId))
                {
                    var structuredPlan = await planStore.LoadStructuredPlanAsync(sessionId);
                    if (structuredPlan != null)
                        SendPlanUpdate(sessionId, structuredPlan);
                }

                logger?.LogEvent($"Prompt complete [session={sessionId}] stop=end_turn (Session Protocol)");
                transport.SendResponse(request.Id, new SessionPromptResult { StopReason = AcpStopReason.EndTurn });
            }
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

    // ????? Helper methods ?????

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
        var modeName = p.Mode?.ToLowerInvariant() ?? "agent";

        sessionService.SetThreadModeAsync(sessionId, modeName).GetAwaiter().GetResult();

        var resolvedMode = modeName == "plan" ? AgentMode.Plan : AgentMode.Agent;
        transport.SendResponse(request.Id, new { mode = resolvedMode.ToString().ToLower() });
        SendModeUpdate(sessionId, resolvedMode);

        logger?.LogEvent($"Mode changed [session={sessionId}]: {modeName}");
        AnsiConsole.MarkupLine($"[green][[ACP]][/] Mode changed to {Markup.Escape(modeName)} [[session={Markup.Escape(sessionId)}]]");
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
            var modeName = p.Value.ToLowerInvariant();

            sessionService.SetThreadModeAsync(sessionId, modeName).GetAwaiter().GetResult();

            var updatedOptions = BuildConfigOptions(modeName);
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

    /// <summary>
    /// Builds multimodal content from ACP prompt blocks, supporting text, resource, and image blocks.
    /// </summary>
    private static IList<AIContent> BuildPromptContent(List<AcpContentBlock> prompt)
    {
        var parts = new List<AIContent>();
        foreach (var block in prompt)
        {
            switch (block.Type)
            {
                case "text" when !string.IsNullOrEmpty(block.Text):
                    parts.Add(new TextContent(block.Text));
                    break;

                case "resource" when block.Resource != null:
                {
                    var resourceText = $"[File: {block.Resource.Uri}]";
                    if (!string.IsNullOrEmpty(block.Resource.Text))
                        resourceText += $"\n{block.Resource.Text}";
                    parts.Add(new TextContent(resourceText));
                    break;
                }

                default:
                    if (!string.IsNullOrEmpty(block.Data) &&
                        !string.IsNullOrEmpty(block.MimeType) &&
                        block.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                    {
                        parts.Add(new DataContent(Convert.FromBase64String(block.Data), block.MimeType));
                    }
                    break;
            }
        }

        if (parts.Count == 0 && prompt.Count > 0)
            parts.Add(new TextContent(ExtractPromptText(prompt)));

        return parts;
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
