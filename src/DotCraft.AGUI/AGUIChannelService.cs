using System.ClientModel;
using System.Text.Json;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Tracing;
using DotCraft.Heartbeat;
using DotCraft.Hosting;
using DotCraft.Hooks;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Sessions.Protocol;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using Spectre.Console;

namespace DotCraft.AGUI;

/// <summary>
/// Gateway channel service for AG-UI protocol. Runs a dedicated Kestrel server on AgUi.Host:AgUi.Port
/// and exposes a single POST endpoint (e.g. /ag-ui) that accepts AG-UI RunAgentInput and streams SSE events.
/// Implements <see cref="IWebHostingChannel"/> so <see cref="WebHostPool"/> can merge it with other services
/// that share the same host:port.
/// </summary>
public sealed class AGUIChannelService(
    IServiceProvider sp,
    AppConfig config,
    DotCraftPaths paths,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    McpClientManager mcpClientManager,
    ModuleRegistry moduleRegistry)
    : IChannelService, IWebHostingChannel
{
    private WebApplication? _webApp;
    private AgentFactory? _agentFactory;
    private ISessionService? _sessionService;

    // Stored during ConfigureBuilder, consumed during ConfigureApp
    private AIAgent? _agent;

    public string Name => "ag-ui";

    /// <inheritdoc />
    public HeartbeatService? HeartbeatService { get; set; }

    /// <inheritdoc />
    public CronService? CronService { get; set; }

    /// <inheritdoc />
    public IApprovalService ApprovalService { get; } = new AutoApproveApprovalService();

    /// <inheritdoc />
    public object? ChannelClient => null;

    public Task DeliverMessageAsync(string target, string content) => Task.CompletedTask;

    public IReadOnlyList<string> GetAdminTargets() => [];

    #region IWebHostingChannel

    /// <inheritdoc />
    public string ListenHost => string.IsNullOrWhiteSpace(config.GetSection<AgUiConfig>("AgUi").Host) ? "127.0.0.1" : config.GetSection<AgUiConfig>("AgUi").Host;

    /// <inheritdoc />
    public int ListenPort => config.GetSection<AgUiConfig>("AgUi").Port <= 0 ? 5100 : config.GetSection<AgUiConfig>("AgUi").Port;

    /// <inheritdoc />
    public void ConfigureBuilder(WebApplicationBuilder builder)
    {
        var agUiConfig = config.GetSection<AgUiConfig>("AgUi");

        _agentFactory = BuildAgentFactory();

        builder.Services.AddAGUI();
        if (!string.Equals(agUiConfig.ApprovalMode, "auto", StringComparison.OrdinalIgnoreCase))
            builder.Services.ConfigureHttpJsonOptions(o =>
                o.SerializerOptions.TypeInfoResolverChain.Add(ApprovalJsonContext.Default));
    }

    /// <inheritdoc />
    public void ConfigureApp(WebApplication app)
    {
        _webApp = app;
        var agUiConfig = config.GetSection<AgUiConfig>("AgUi");
        var tokenUsageStore = sp.GetService<TokenUsageStore>();
        var traceStore = sp.GetService<TraceStore>();
        var path = string.IsNullOrWhiteSpace(agUiConfig.Path) ? "/ag-ui" : agUiConfig.Path.Trim();

        // Tools are created here (after Build) so app.Services is available for IOptions<JsonOptions>.
        var tools = _agentFactory!.CreateDefaultTools();

        if (string.Equals(agUiConfig.ApprovalMode, "auto", StringComparison.OrdinalIgnoreCase))
        {
            _agent = _agentFactory.CreateAgentWithTools(tools);
        }
        else
        {
            // Wrap sensitive tools so they emit FunctionApprovalRequestContent instead of running.
            var approvalToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "WriteFile", "EditFile", "Exec" };
#pragma warning disable MEAI001
            for (var i = 0; i < tools.Count; i++)
            {
                if (tools[i] is AIFunction fn && approvalToolNames.Contains(fn.Name))
                    tools[i] = new ApprovalRequiredAIFunction(fn);
            }
#pragma warning restore MEAI001
            var baseAgent = _agentFactory.CreateAgentWithTools(tools);
            var jsonOptions = app.Services.GetRequiredService<IOptions<JsonOptions>>().Value;
            _agent = new AGUIApprovalAgent(baseAgent, jsonOptions.SerializerOptions);
        }

        // Construct Session Protocol service (auto-approval for AG-UI; client sends full history).
        var sessionAgent = _agentFactory.CreateAgentWithTools(_agentFactory.CreateDefaultTools());
        _sessionService = SessionServiceFactory.Create(_agentFactory, sessionAgent, sp);

        var pathPrefix = path.TrimEnd('/');
        if (agUiConfig.RequireAuth && !string.IsNullOrWhiteSpace(agUiConfig.ApiKey))
        {
            var apiKey = agUiConfig.ApiKey!;
            app.Use(async (context, next) =>
            {
                var requestPath = context.Request.Path.Value ?? "";
                if (requestPath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase) ||
                    (pathPrefix.Length > 0 && requestPath == pathPrefix.TrimEnd('/')))
                {
                    var authHeader = context.Request.Headers.Authorization.ToString();
                    if (!authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
                        authHeader["Bearer ".Length..].Trim() != apiKey)
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsync("Unauthorized");
                        return;
                    }
                }
                await next();
            });
        }

        app.Use(async (context, next) =>
        {
            var requestPath = context.Request.Path.Value ?? "";
            var isAgUiPath = requestPath.StartsWith(pathPrefix, StringComparison.OrdinalIgnoreCase) ||
                (pathPrefix.Length > 0 && requestPath == pathPrefix.TrimEnd('/'));
            if (!isAgUiPath || context.Request.Method != HttpMethods.Post)
            {
                await next();
                return;
            }

            context.Request.EnableBuffering();
            var sessionKeyUsed = "ag-ui:" + Guid.NewGuid().ToString("N")[..8];
            try
            {
                if (context.Request.ContentLength is > 0)
                {
                    context.Request.Body.Position = 0;
                    using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                    var body = await reader.ReadToEndAsync(context.RequestAborted);
                    context.Request.Body.Position = 0;
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(body);
                            var threadId = doc.RootElement.TryGetProperty("threadId", out var prop)
                                ? prop.GetString()
                                : null;
                            if (!string.IsNullOrWhiteSpace(threadId))
                                sessionKeyUsed = "ag-ui:" + threadId;
                        }
                        catch
                        {
                            // Keep fallback session key
                        }
                    }
                }

                var prevInput  = traceStore?.GetSession(sessionKeyUsed)?.TotalInputTokens  ?? 0;
                var prevOutput = traceStore?.GetSession(sessionKeyUsed)?.TotalOutputTokens ?? 0;

                TracingChatClient.CurrentSessionKey = sessionKeyUsed;
                TracingChatClient.ResetCallState(sessionKeyUsed);
                await next();

                var session     = traceStore?.GetSession(sessionKeyUsed);
                var inputDelta  = (session?.TotalInputTokens  ?? 0) - prevInput;
                var outputDelta = (session?.TotalOutputTokens ?? 0) - prevOutput;
                if (inputDelta > 0 || outputDelta > 0)
                {
                    var threadLabel = sessionKeyUsed.StartsWith("ag-ui:") ? sessionKeyUsed["ag-ui:".Length..] : sessionKeyUsed;
                    tokenUsageStore?.Record(new TokenUsageRecord
                    {
                        Channel = "agui",
                        UserId = threadLabel,
                        DisplayName = threadLabel,
                        InputTokens = inputDelta,
                        OutputTokens = outputDelta
                    });
                }
            }
            finally
            {
                TracingChatClient.ResetCallState(sessionKeyUsed);
                TracingChatClient.CurrentSessionKey = null;
            }
        });

        // Health probe endpoint — responds to GET requests.
        app.MapGet(path, () => Results.Ok(new { status = "ok" }));

        // POST {path} — AG-UI event stream backed by ISessionService for thread persistence.
        {
            var capturedSessionService = _sessionService!;
            app.MapPost(path, async (HttpContext context) =>
            {
                context.Request.EnableBuffering();
                string requestBody;
                using (var reader = new StreamReader(context.Request.Body, leaveOpen: true))
                    requestBody = await reader.ReadToEndAsync(context.RequestAborted);
                context.Request.Body.Position = 0;

                string agThreadId = string.Empty;
                string runId = Guid.NewGuid().ToString("N")[..8];
                var chatMessages = new List<ChatMessage>();
                var promptText = string.Empty;

                try
                {
                    using var doc = JsonDocument.Parse(requestBody);
                    var root = doc.RootElement;

                    agThreadId = root.TryGetProperty("threadId", out var tidProp)
                        ? tidProp.GetString() ?? string.Empty : string.Empty;
                    if (root.TryGetProperty("runId", out var ridProp))
                        runId = ridProp.GetString() ?? runId;

                    if (root.TryGetProperty("messages", out var msgsProp) &&
                        msgsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var m in msgsProp.EnumerateArray())
                        {
                            var role = m.TryGetProperty("role", out var r) ? r.GetString() : "user";
                            var content = m.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                            chatMessages.Add(new ChatMessage(
                                role == "assistant" ? ChatRole.Assistant :
                                role == "system" ? ChatRole.System : ChatRole.User,
                                content));
                            if (role == "user")
                                promptText = content;
                        }
                    }
                }
                catch
                {
                    context.Response.StatusCode = 400;
                    await context.Response.WriteAsync("Invalid JSON");
                    return;
                }

                var identity = new SessionIdentity
                {
                    ChannelName = "agui",
                    UserId = string.IsNullOrEmpty(agThreadId) ? "default" : agThreadId,
                    ChannelContext = agThreadId,
                    WorkspacePath = paths.WorkspacePath
                };

                // Find or create a persistent thread keyed by AG-UI threadId.
                var threads = await capturedSessionService.FindThreadsAsync(identity);
                string sessionThreadId;
                if (threads.Count > 0 && threads[0].Status != ThreadStatus.Archived)
                {
                    sessionThreadId = threads[0].Id;
                    if (threads[0].Status == ThreadStatus.Paused)
                        await capturedSessionService.ResumeThreadAsync(sessionThreadId);
                }
                else
                {
                    var thread = await capturedSessionService.CreateThreadAsync(
                        identity,
                        historyMode: HistoryMode.Client);
                    sessionThreadId = thread.Id;
                }

                context.Response.ContentType = "text/event-stream";
                context.Response.Headers.CacheControl = "no-cache";

                await WriteAguiSseAsync(context, $"{{\"type\":\"RUN_STARTED\",\"threadId\":{JsonSerializer.Serialize(agThreadId)},\"runId\":{JsonSerializer.Serialize(runId)}}}", context.RequestAborted);

                string? currentMsgId = null;

                await foreach (var evt in capturedSessionService
                    .SubmitInputAsync(sessionThreadId, promptText,
                                     messages: chatMessages.Count > 0 ? chatMessages.ToArray() : null,
                                     ct: context.RequestAborted))
                {
                    switch (evt.EventType)
                    {
                        case SessionEventType.ItemDelta
                            when evt.DeltaPayload is { } delta && !string.IsNullOrEmpty(delta.TextDelta):
                            if (currentMsgId == null)
                            {
                                currentMsgId = evt.ItemId ?? Guid.NewGuid().ToString("N")[..8];
                                await WriteAguiSseAsync(context,
                                    $"{{\"type\":\"TEXT_MESSAGE_START\",\"messageId\":{JsonSerializer.Serialize(currentMsgId)},\"role\":\"assistant\"}}",
                                    context.RequestAborted);
                            }
                            await WriteAguiSseAsync(context,
                                $"{{\"type\":\"TEXT_MESSAGE_CONTENT\",\"messageId\":{JsonSerializer.Serialize(currentMsgId)},\"delta\":{JsonSerializer.Serialize(delta.TextDelta)}}}",
                                context.RequestAborted);
                            break;

                        case SessionEventType.ItemStarted
                            when evt.ItemPayload?.Type == ItemType.ToolCall &&
                                 evt.ItemPayload.AsToolCall is { } tc:
                            await WriteAguiSseAsync(context,
                                $"{{\"type\":\"TOOL_CALL_START\",\"toolCallId\":{JsonSerializer.Serialize(tc.CallId)},\"toolCallName\":{JsonSerializer.Serialize(tc.ToolName)},\"parentMessageId\":{JsonSerializer.Serialize(currentMsgId ?? "")}}}",
                                context.RequestAborted);
                            if (tc.Arguments != null)
                                await WriteAguiSseAsync(context,
                                    $"{{\"type\":\"TOOL_CALL_ARGS\",\"toolCallId\":{JsonSerializer.Serialize(tc.CallId)},\"delta\":{JsonSerializer.Serialize(tc.Arguments.ToJsonString())}}}",
                                    context.RequestAborted);
                            break;

                        case SessionEventType.ItemCompleted
                            when evt.ItemPayload?.Type == ItemType.ToolResult &&
                                 evt.ItemPayload.AsToolResult is { } tr:
                            await WriteAguiSseAsync(context,
                                $"{{\"type\":\"TOOL_CALL_END\",\"toolCallId\":{JsonSerializer.Serialize(tr.CallId)}}}",
                                context.RequestAborted);
                            await WriteAguiSseAsync(context,
                                $"{{\"type\":\"TOOL_CALL_RESULT\",\"toolCallId\":{JsonSerializer.Serialize(tr.CallId)},\"result\":{JsonSerializer.Serialize(tr.Result)}}}",
                                context.RequestAborted);
                            break;

                        case SessionEventType.TurnCompleted:
                            if (currentMsgId != null)
                            {
                                await WriteAguiSseAsync(context,
                                    $"{{\"type\":\"TEXT_MESSAGE_END\",\"messageId\":{JsonSerializer.Serialize(currentMsgId)}}}",
                                    context.RequestAborted);
                                currentMsgId = null;
                            }
                            await WriteAguiSseAsync(context,
                                $"{{\"type\":\"RUN_FINISHED\",\"threadId\":{JsonSerializer.Serialize(agThreadId)},\"runId\":{JsonSerializer.Serialize(runId)}}}",
                                context.RequestAborted);
                            break;

                        case SessionEventType.TurnFailed:
                            var errMsg = evt.TurnPayload?.Error ?? "Turn failed";
                            await WriteAguiSseAsync(context,
                                $"{{\"type\":\"RUN_ERROR\",\"code\":\"turn_failed\",\"message\":{JsonSerializer.Serialize(errMsg)}}}",
                                context.RequestAborted);
                            break;
                    }
                }
            });
        }

        var url = $"http://{ListenHost}:{ListenPort}";
        AnsiConsole.MarkupLine($"[green][[Gateway]][/] AG-UI listening on {Markup.Escape(url)}{Markup.Escape(path)}");
    }

    #endregion

    private static async Task WriteAguiSseAsync(HttpContext context, string json, CancellationToken ct)
    {
        await context.Response.WriteAsync("data: " + json + "\n\n", ct);
        await context.Response.Body.FlushAsync(ct);
    }

    private AgentFactory BuildAgentFactory()
    {
        var cronTools = sp.GetService<CronTools>();
        var traceCollector = sp.GetService<TraceCollector>();
        var hookRunner = sp.GetService<HookRunner>();

        var toolProviders = ToolProviderCollector.Collect(moduleRegistry, config);

        return new AgentFactory(
            paths.CraftPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader, ApprovalService, blacklist,
            toolProviders: toolProviders,
            toolProviderContext: new ToolProviderContext
            {
                Config = config,
                ChatClient = new OpenAIClient(
                    new ApiKeyCredential(config.ApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(config.EndPoint) })
                    .GetChatClient(config.Model),
                WorkspacePath = paths.WorkspacePath,
                BotPath = paths.CraftPath,
                MemoryStore = memoryStore,
                SkillsLoader = skillsLoader,
                ApprovalService = ApprovalService,
                PathBlacklist = blacklist,
                CronTools = cronTools,
                McpClientManager = mcpClientManager.Tools.Count > 0 ? mcpClientManager : null,
                TraceCollector = traceCollector
            },
            traceCollector: traceCollector,
            customCommandLoader: sp.GetService<CustomCommandLoader>(),
            onConsolidatorStatus: AnsiConsole.MarkupLine,
            hookRunner: hookRunner);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Web server lifecycle is managed by WebHostPool in GatewayHost.
        // This task just holds open until the cancellation token fires.
        var tcs = new TaskCompletionSource();
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;
    }

    public async Task StopAsync()
    {
        if (_webApp != null)
            await _webApp.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_webApp != null)
            await _webApp.DisposeAsync();
        if (_agentFactory != null)
            await _agentFactory.DisposeAsync();
    }
}
