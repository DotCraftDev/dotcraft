using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Cron;
using DotCraft.Tracing;
using DotCraft.Heartbeat;
using DotCraft.Hooks;
using DotCraft.Hosting;
using DotCraft.Lsp;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Protocol.AppServer;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using Spectre.Console;

namespace DotCraft.Api;

/// <summary>
/// Gateway channel service for OpenAI-compatible HTTP API.
/// Manages the ASP.NET Core web server and agent lifecycle as part of a multi-channel gateway.
/// Implements <see cref="IWebHostingChannel"/> so <see cref="WebHostPool"/> can merge it with
/// other services that share the same host:port (e.g. the Dashboard).
/// </summary>
public sealed class ApiChannelService(
    IServiceProvider sp,
    AppConfig config,
    DotCraftPaths paths,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    McpClientManager mcpClientManager,
    LspServerManager lspServerManager,
    ApiApprovalService approvalService,
    ModuleRegistry moduleRegistry)
    : IChannelService, IWebHostingChannel
{
    private WebApplication? _webApp;
    private AgentFactory? _agentFactory;

    // Stored during ConfigureBuilder, consumed during ConfigureApp
    private IHostedAgentBuilder? _agentBuilder;
    private List<AITool>? _tools;

    private ApiConfig ApiConfig => config.GetSection<ApiConfig>("Api");

    public string Name => "api";

    /// <inheritdoc />
    public HeartbeatService? HeartbeatService { get; set; }

    /// <inheritdoc />
    public CronService? CronService { get; set; }

    /// <inheritdoc />
    public IApprovalService ApprovalService { get; } = new AutoApproveApprovalService();

    #region IWebHostingChannel

    /// <inheritdoc />
    public string ListenHost => string.IsNullOrWhiteSpace(ApiConfig.Host) ? "127.0.0.1" : ApiConfig.Host;

    /// <inheritdoc />
    public int ListenPort => ApiConfig.Port <= 0 ? 8080 : ApiConfig.Port;

    /// <inheritdoc />
    public void ConfigureBuilder(WebApplicationBuilder builder)
    {
        _agentFactory = BuildAgentFactory();
        var traceCollector = sp.GetService<TraceCollector>();

        _tools = _agentFactory.CreateDefaultTools();

        builder.AddOpenAIChatCompletions();

        _agentBuilder = builder.AddAIAgent(
            "dotcraft",
            _agentFactory.CreateToolCallFilteringChatClient(),
            CreateApiAgentOptions(_tools, traceCollector))
            .WithAITools(_tools.ToArray())
            .WithInMemorySessionStore();
    }

    /// <inheritdoc />
    public void ConfigureApp(WebApplication app)
    {
        _webApp = app;

        var traceCollector = sp.GetService<TraceCollector>();

        if (!string.IsNullOrEmpty(ApiConfig.ApiKey))
        {
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value ?? "";
                if (path.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
                {
                    if (path == "/v1/health")
                    {
                        await next();
                        return;
                    }
                    
                    if (!Authenticate(context))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
                        return;
                    }
                }
                await next();
            });
        }

        if (traceCollector != null)
        {
            var capturedTraceStore = sp.GetService<TraceStore>();
            var capturedTokenUsageStore = sp.GetService<TokenUsageStore>();
            app.Use(async (context, next) =>
            {
                var path = context.Request.Path.Value ?? "";
                if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                {
                    var sessionKey = await ResolveSessionKeyAsync(context);
                    TracingChatClient.CurrentSessionKey = sessionKey;
                    TracingChatClient.ResetCallState(sessionKey);

                    var inputBefore = capturedTraceStore?.GetSession(sessionKey)?.TotalInputTokens ?? 0;
                    var outputBefore = capturedTraceStore?.GetSession(sessionKey)?.TotalOutputTokens ?? 0;

                    try
                    {
                        await next();
                    }
                    finally
                    {
                        if (capturedTokenUsageStore != null && capturedTraceStore != null)
                        {
                            var session = capturedTraceStore.GetSession(sessionKey);
                            var inputDelta = (session?.TotalInputTokens ?? 0) - inputBefore;
                            var outputDelta = (session?.TotalOutputTokens ?? 0) - outputBefore;
                            if (inputDelta > 0 || outputDelta > 0)
                            {
                                capturedTokenUsageStore.Record(new TokenUsageRecord
                                {
                                    SourceId = "api",
                                    SourceMode = TokenUsageSourceModes.ClientManaged,
                                    SubjectKind = TokenUsageSubjectKinds.Session,
                                    SubjectId = sessionKey,
                                    SubjectLabel = sessionKey,
                                    SessionKey = sessionKey,
                                    InputTokens = inputDelta,
                                    OutputTokens = outputDelta
                                });
                            }
                        }

                        TracingChatClient.ResetCallState(sessionKey);
                        TracingChatClient.CurrentSessionKey = null;
                    }
                }
                else
                {
                    await next();
                }
            });
        }

        app.Use(NonStreamingResponseMiddleware);
        app.MapOpenAIChatCompletions(_agentBuilder!);
        MapAdditionalRoutes(app);

        var url = $"http://{ListenHost}:{ListenPort}";
        AnsiConsole.MarkupLine($"[green][[Gateway]][/] API listening on {Markup.Escape(url)}");

        var approvalMode = ApiApprovalService.ParseMode(ApiConfig.AutoApprove);
        AnsiConsole.MarkupLine($"[grey]  Approval mode: {approvalMode.ToString().ToLowerInvariant()}[/]");
    }

    #endregion

    private AgentFactory BuildAgentFactory()
    {
        var cronTools = sp.GetService<CronTools>();
        var traceCollector = sp.GetService<TraceCollector>();
        var hookRunner = sp.GetService<HookRunner>();

        // Collect tool providers from modules
        var toolProviders = ToolProviderCollector.Collect(moduleRegistry, config);
        var openAIClientProvider = sp.GetRequiredService<OpenAIClientProvider>();
        var mainModel = openAIClientProvider.ResolveMainModel(config);

        return new AgentFactory(
            paths.CraftPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader, approvalService, blacklist,
            toolProviders: toolProviders,
            toolProviderContext: new ToolProviderContext
            {
                Config = config,
                ChatClient = openAIClientProvider.GetChatClient(config, mainModel),
                OpenAIClientProvider = openAIClientProvider,
                EffectiveMainModel = mainModel,
                WorkspacePath = paths.WorkspacePath,
                BotPath = paths.CraftPath,
                MemoryStore = memoryStore,
                SkillsLoader = skillsLoader,
                ApprovalService = approvalService,
                PathBlacklist = blacklist,
                CronTools = cronTools,
                McpClientManager = mcpClientManager,
                LspServerManager = lspServerManager,
                TraceCollector = traceCollector
            },
            traceCollector: traceCollector,
            customCommandLoader: sp.GetService<CustomCommandLoader>(),
            onConsolidatorStatus: AnsiConsole.MarkupLine,
            hookRunner: hookRunner,
            openAIClientProvider: openAIClientProvider);
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
        // WebApp is stopped by the pool; nothing extra to do here.
        if (_webApp != null)
            await _webApp.StopAsync();
    }

    public Task<ExtChannelSendResult> DeliverAsync(
        string target,
        ChannelOutboundMessage message,
        object? metadata = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ExtChannelSendResult
        {
            Delivered = false,
            ErrorCode = "UnsupportedDeliveryKind",
            ErrorMessage = "API channel does not support proactive delivery."
        });

    /// <summary>
    /// Resolves a stable session key for the current request.
    /// Priority: X-Session-Key header → 'user' field in body → SHA-256 fingerprint of
    /// first message content → random fallback.
    /// Grouping by the first message fingerprint ensures that all turns of the same
    /// conversation (which always starts with the same first message) are recorded
    /// under a single Dashboard session.
    /// </summary>
    private static async Task<string> ResolveSessionKeyAsync(HttpContext context)
    {
        var fromHeader = context.Request.Headers["X-Session-Key"].FirstOrDefault();
        if (!string.IsNullOrEmpty(fromHeader))
            return fromHeader;

        context.Request.EnableBuffering();
        try
        {
            using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Use the 'user' field if the client supplies one (e.g. a persistent user ID).
            if (root.TryGetProperty("user", out var userProp) &&
                userProp.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(userProp.GetString()))
            {
                return $"api:{userProp.GetString()}";
            }

            // Fingerprint from the first message in the conversation.
            // Chatbox always sends the full history, so messages[0] is identical across
            // all turns of the same conversation — making it a stable session identifier.
            if (root.TryGetProperty("messages", out var messages) &&
                messages.ValueKind == JsonValueKind.Array &&
                messages.GetArrayLength() > 0)
            {
                var first = messages[0];
                var content = first.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";
                if (!string.IsNullOrEmpty(content))
                {
                    var hash = Convert.ToHexString(
                        SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..12].ToLowerInvariant();
                    return $"api:{hash}";
                }
            }
        }
        catch
        {
            // Ignore parse errors and fall through to random key.
        }

        return $"api:{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}";
    }

    private void MapAdditionalRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/health", () => Results.Json(new
        {
            status = "ok",
            version = "1.0.0",
            mode = "gateway-api",
            model = config.Model,
            protocol = "openai-compatible"
        }));
    }

    private ChatClientAgentOptions CreateApiAgentOptions(IReadOnlyList<AITool> tools,
        TraceCollector? traceCollector)
    {
        return new ChatClientAgentOptions
        {
            ChatOptions = new ChatOptions
            {
                Reasoning = _agentFactory?.CreateReasoningOptions()
            },
            AIContextProviders =
            [
                new MemoryContextProvider(
                    memoryStore, skillsLoader,
                    paths.CraftPath, paths.WorkspacePath,
                    traceCollector,
                    () => tools.Select(t => t.Name).ToArray(),
                    sp.GetService<CustomCommandLoader>(),
                    sandboxEnabled: config.Tools.Sandbox.Enabled)
            ]
        };
    }

    private bool Authenticate(HttpContext context)
    {
        var apiKey = ApiConfig.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
            return true;

        var authHeader = context.Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return authHeader["Bearer ".Length..].Trim() == apiKey;

        return false;
    }

    private static async Task NonStreamingResponseMiddleware(HttpContext context, RequestDelegate next)
    {
        var path = context.Request.Path.Value ?? "";
        if (!path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var requestBody = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        var isStreaming = false;
        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            if (doc.RootElement.TryGetProperty("stream", out var streamProp) &&
                streamProp.ValueKind == JsonValueKind.True)
                isStreaming = true;
        }
        catch { /* ignored */ }

        if (isStreaming)
        {
            await next(context);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await next(context);

        buffer.Position = 0;
        var contentType = context.Response.ContentType ?? "";
        if (contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var doc = await JsonDocument.ParseAsync(buffer);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array)
                {
                    var candidateChoices = new List<JsonElement>();
                    JsonElement? bestTextChoice = null;

                    for (var i = 0; i < choices.GetArrayLength(); i++)
                    {
                        var choice = choices[i];
                        var hasToolCalls = false;
                        var hasNonEmptyContent = false;

                        if (choice.TryGetProperty("message", out var msg) &&
                            msg.ValueKind == JsonValueKind.Object)
                        {
                            if (msg.TryGetProperty("tool_calls", out var toolCalls) &&
                                toolCalls.ValueKind == JsonValueKind.Array &&
                                toolCalls.GetArrayLength() > 0)
                                hasToolCalls = true;

                            if (msg.TryGetProperty("content", out var content) &&
                                content.ValueKind == JsonValueKind.String &&
                                !string.IsNullOrWhiteSpace(content.GetString()))
                            {
                                hasNonEmptyContent = true;
                                bestTextChoice = choice;
                            }
                        }

                        if (hasToolCalls && !hasNonEmptyContent)
                            continue;

                        candidateChoices.Add(choice);
                    }

                    if (candidateChoices.Count > 0)
                    {
                        var selectedChoice = candidateChoices.Count == 1
                            ? candidateChoices[0]
                            : bestTextChoice ?? candidateChoices[^1];

                        using var output = new MemoryStream();
                        using (var writer = new Utf8JsonWriter(output))
                        {
                            writer.WriteStartObject();
                            foreach (var prop in root.EnumerateObject())
                            {
                                if (prop.Name == "choices")
                                {
                                    writer.WriteStartArray("choices");
                                    writer.WriteStartObject();
                                    foreach (var cp in selectedChoice.EnumerateObject())
                                    {
                                        if (cp.Name == "index")
                                        {
                                            writer.WriteNumber("index", 0);
                                        }
                                        else if (cp.Name == "message" &&
                                                 cp.Value.ValueKind == JsonValueKind.Object)
                                        {
                                            writer.WriteStartObject("message");
                                            foreach (var mp in cp.Value.EnumerateObject())
                                            {
                                                if (!string.Equals(mp.Name, "tool_calls",
                                                        StringComparison.Ordinal))
                                                    mp.WriteTo(writer);
                                            }
                                            writer.WriteEndObject();
                                        }
                                        else
                                        {
                                            cp.WriteTo(writer);
                                        }
                                    }
                                    writer.WriteEndObject();
                                    writer.WriteEndArray();
                                }
                                else
                                {
                                    prop.WriteTo(writer);
                                }
                            }
                            writer.WriteEndObject();
                        }

                        context.Response.Body = originalBody;
                        context.Response.ContentLength = output.Length;
                        output.Position = 0;
                        await output.CopyToAsync(originalBody);
                        return;
                    }
                }
            }
            catch { /* ignored */ }
        }

        buffer.Position = 0;
        context.Response.Body = originalBody;
        context.Response.ContentLength = buffer.Length;
        await buffer.CopyToAsync(originalBody);
    }

    public async ValueTask DisposeAsync()
    {
        if (_webApp != null)
            await _webApp.DisposeAsync();
        if (_agentFactory != null)
            await _agentFactory.DisposeAsync();
    }
}
