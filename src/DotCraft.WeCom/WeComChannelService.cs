using System.ClientModel;
using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Tracing;
using DotCraft.Diagnostics;
using DotCraft.Sessions;
using DotCraft.Heartbeat;
using DotCraft.Hooks;
using DotCraft.Hosting;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;

namespace DotCraft.WeCom;

/// <summary>
/// Gateway channel service for WeCom Bot. Manages the ASP.NET Core HTTP server,
/// channel adapter, and agent lifecycle as part of a multi-channel gateway.
/// Implements <see cref="IWebHostingChannel"/> so <see cref="WebHostPool"/> can merge it with
/// other services that share the same address (scheme + host + port).
/// </summary>
public sealed class WeComChannelService(
    IServiceProvider sp,
    AppConfig config,
    DotCraftPaths paths,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    McpClientManager mcpClientManager,
    WeComBotRegistry registry,
    WeComPermissionService permissionService,
    WeComApprovalService wecomApprovalService,
    ModuleRegistry moduleRegistry)
    : IChannelService, IWebHostingChannel
{
    private WebApplication? _webApp;
    private WeComChannelAdapter? _adapter;
    private AgentFactory? _agentFactory;
    private HttpClient? _httpClient;
    private static readonly ChannelDeliveryCapabilities DeliveryCapabilities = new()
    {
        StructuredDelivery = true,
        Media = new ChannelMediaCapabilitySet
        {
            Audio = new ChannelMediaConstraints
            {
                SupportsHostPath = true,
                SupportsBase64 = true
            },
            File = new ChannelMediaConstraints
            {
                SupportsHostPath = true,
                SupportsBase64 = true
            }
        }
    };

    private static readonly IReadOnlyList<ChannelToolDescriptor> ChannelTools =
    [
        new()
        {
            Name = "wecomSendVoiceToCurrentChat",
            Description = "Send a voice message to the current WeCom chat. Voice files must be AMR.",
            RequiresChatContext = true,
            InputSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["filePath"] = new JsonObject { ["type"] = "string" },
                    ["fileBase64"] = new JsonObject { ["type"] = "string" },
                    ["fileName"] = new JsonObject { ["type"] = "string" }
                }
            }
        },
        new()
        {
            Name = "wecomSendFileToCurrentChat",
            Description = "Send a file to the current WeCom chat.",
            RequiresChatContext = true,
            InputSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["filePath"] = new JsonObject { ["type"] = "string" },
                    ["fileBase64"] = new JsonObject { ["type"] = "string" },
                    ["fileName"] = new JsonObject { ["type"] = "string" }
                }
            }
        }
    ];

    public string Name => "wecom";

    /// <inheritdoc />
    public HeartbeatService? HeartbeatService { get; set; }

    /// <inheritdoc />
    public CronService? CronService { get; set; }

    /// <inheritdoc />
    public IApprovalService ApprovalService => wecomApprovalService;

    public ChannelDeliveryCapabilities? GetDeliveryCapabilities() => DeliveryCapabilities;

    public IReadOnlyList<ChannelToolDescriptor> GetChannelTools() => ChannelTools;

    #region IWebHostingChannel

    /// <inheritdoc />
    // WeCom requires HTTPS; this prevents accidental merging with HTTP-only services.
    public string ListenScheme => "https";

    /// <inheritdoc />
    public string ListenHost => string.IsNullOrWhiteSpace(config.GetSection<WeComBotConfig>("WeComBot").Host) ? "0.0.0.0" : config.GetSection<WeComBotConfig>("WeComBot").Host;

    /// <inheritdoc />
    public int ListenPort => config.GetSection<WeComBotConfig>("WeComBot").Port <= 0 ? 9000 : config.GetSection<WeComBotConfig>("WeComBot").Port;

    /// <inheritdoc />
    public void ConfigureBuilder(WebApplicationBuilder builder)
    {
        // WeCom Bot server has no additional DI registrations needed on the builder.
        // Agent factory and adapter are initialized in ConfigureApp where the full
        // service provider is available.
    }

    /// <inheritdoc />
    public void ConfigureApp(WebApplication app)
    {
        _webApp = app;

        var scopedApproval = new SessionScopedApprovalService(wecomApprovalService);
        _agentFactory = BuildAgentFactory(scopedApproval);
        var agent = _agentFactory.CreateAgentForMode(AgentMode.Agent);
        var tokenUsageStore = sp.GetService<TokenUsageStore>();

        var activeRunRegistry = sp.GetRequiredService<ActiveRunRegistry>();
        var customCommandLoader = sp.GetService<CustomCommandLoader>();
        var sessionService = SessionServiceFactory.Create(_agentFactory, agent, sp);
        _httpClient = new HttpClient(new SocketsHttpHandler
        {
            SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10),
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
            DefaultRequestHeaders = { { "User-Agent", "DotCraft/1.0" } }
        };
        _adapter = new WeComChannelAdapter(
            registry,
            permissionService, wecomApprovalService, activeRunRegistry,
            heartbeatService: HeartbeatService,
            cronService: CronService,
            agentFactory: _agentFactory,
            tokenUsageStore: tokenUsageStore,
            customCommandLoader: customCommandLoader,
            httpClient: _httpClient,
            sessionService: sessionService,
            workspacePath: paths.WorkspacePath);

        var logger = new WeComServerLogger();
        var server = new WeComBotServer(registry, httpClient: _httpClient, logger: logger);
        server.MapRoutes(app);

        var url = $"https://{ListenHost}:{ListenPort}";
        AnsiConsole.MarkupLine($"[green][[Gateway]][/] WeCom Bot listening on {Markup.Escape(url)}");
        foreach (var path in registry.GetAllPaths())
        {
            AnsiConsole.MarkupLine($"[grey]  - {Markup.Escape(url + path)}[/]");
        }
    }

    #endregion

    private AgentFactory BuildAgentFactory(SessionScopedApprovalService scopedApproval)
    {
        var cronTools = sp.GetService<CronTools>();
        var traceCollector = sp.GetService<TraceCollector>();
        var hookRunner = sp.GetService<HookRunner>();

        // Collect tool providers from modules
        var toolProviders = ToolProviderCollector.Collect(moduleRegistry, config);

        var planStore = new PlanStore(paths.CraftPath);

        return new AgentFactory(
            paths.CraftPath, paths.WorkspacePath, config,
            memoryStore, skillsLoader, scopedApproval, blacklist,
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
                ApprovalService = scopedApproval,
                PathBlacklist = blacklist,
                CronTools = cronTools,
                McpClientManager = mcpClientManager.Tools.Count > 0 ? mcpClientManager : null,
                TraceCollector = traceCollector
            },
            traceCollector: traceCollector,
            customCommandLoader: sp.GetService<CustomCommandLoader>(),
            onConsolidatorStatus: AnsiConsole.MarkupLine,
            planStore: planStore,
            onPlanUpdated: plan =>
            {
                if (!DebugModeService.IsEnabled()) return;
                var pusher = WeComPusherScope.Current;
                if (pusher == null) return;
                var md = PlanStore.RenderPlanMarkdown(plan);
                _ = pusher.PushMarkdownAsync(md);
            },
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

    /// <inheritdoc />
    public IReadOnlyList<string> GetAdminTargets() => [""];

    public async Task<ExtChannelSendResult> DeliverAsync(
        string target,
        ChannelOutboundMessage message,
        object? metadata = null,
        CancellationToken cancellationToken = default)
    {
        _ = metadata;
        _ = cancellationToken;

        try
        {
            var pusher = CreatePusher(target);
            if (pusher == null)
            {
                return new ExtChannelSendResult
                {
                    Delivered = false,
                    ErrorCode = "AdapterDeliveryFailed",
                    ErrorMessage = $"No WeCom webhook is available for target '{target}'."
                };
            }

            switch (message.Kind.ToLowerInvariant())
            {
                case "text":
                    await pusher.PushTextAsync(message.Text ?? string.Empty);
                    return new ExtChannelSendResult { Delivered = true };
                case "audio":
                    await SendMediaAsync(pusher, message, "voice");
                    return new ExtChannelSendResult { Delivered = true };
                case "file":
                    await SendMediaAsync(pusher, message, "file");
                    return new ExtChannelSendResult { Delivered = true };
                default:
                    return new ExtChannelSendResult
                    {
                        Delivered = false,
                        ErrorCode = "UnsupportedDeliveryKind",
                        ErrorMessage = $"WeCom channel does not support '{message.Kind}' delivery."
                    };
            }
        }
        catch (Exception ex)
        {
            return new ExtChannelSendResult
            {
                Delivered = false,
                ErrorCode = "AdapterDeliveryFailed",
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<ExtChannelToolCallResult> ExecuteToolAsync(
        ExtChannelToolCallParams request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var target = request.Context.ChannelContext;
            if (string.IsNullOrWhiteSpace(target))
            {
                return new ExtChannelToolCallResult
                {
                    Success = false,
                    ErrorCode = "MissingChatContext",
                    ErrorMessage = "WeCom tool execution requires a current chat context."
                };
            }

            var message = request.Tool switch
            {
                "wecomSendVoiceToCurrentChat" => CreateMessageFromArgs("audio", request.Arguments),
                "wecomSendFileToCurrentChat" => CreateMessageFromArgs("file", request.Arguments),
                _ => null
            };

            if (message == null)
            {
                return new ExtChannelToolCallResult
                {
                    Success = false,
                    ErrorCode = "UnsupportedChannelTool",
                    ErrorMessage = $"WeCom does not expose tool '{request.Tool}'."
                };
            }

            var result = await DeliverAsync(target, message, cancellationToken: cancellationToken);
            return new ExtChannelToolCallResult
            {
                Success = result.Delivered,
                ContentItems =
                [
                    new ExtChannelToolContentItem
                    {
                        Type = "text",
                        Text = result.Delivered ? "Message sent." : (result.ErrorMessage ?? "Tool execution failed.")
                    }
                ],
                StructuredResult = new JsonObject
                {
                    ["delivered"] = result.Delivered,
                    ["errorCode"] = result.ErrorCode,
                    ["target"] = target
                },
                ErrorCode = result.ErrorCode,
                ErrorMessage = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            return new ExtChannelToolCallResult
            {
                Success = false,
                ErrorCode = "ChannelToolCallFailed",
                ErrorMessage = ex.Message
            };
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_adapter != null)
            await _adapter.DisposeAsync();
        if (_webApp != null)
            await _webApp.DisposeAsync();
        if (_agentFactory != null)
            await _agentFactory.DisposeAsync();
        _httpClient?.Dispose();
    }

    private IWeComPusher? CreatePusher(string target)
    {
        var webhookUrl = (!string.IsNullOrWhiteSpace(target) ? registry.GetWebhookUrl(target) : null)
            ?? config.GetSection<WeComConfig>("WeCom").WebhookUrl;
        if (string.IsNullOrWhiteSpace(webhookUrl) || _httpClient == null)
            return null;

        return new WeComPusher(target, webhookUrl, _httpClient);
    }

    private async Task SendMediaAsync(IWeComPusher pusher, ChannelOutboundMessage message, string mediaKind)
    {
        var source = message.Source ?? throw new InvalidOperationException("Media delivery requires a source.");
        var fileName = !string.IsNullOrWhiteSpace(message.FileName)
            ? message.FileName
            : source.HostPath != null ? Path.GetFileName(source.HostPath) : $"{mediaKind}.bin";

        if (mediaKind == "voice" && !fileName.EndsWith(".amr", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("WeCom voice delivery only supports AMR files.");

        string? tempPath = null;
        try
        {
            var hostPath = source.Kind switch
            {
                "hostPath" => source.HostPath ?? throw new InvalidOperationException("hostPath is required."),
                "dataBase64" => tempPath = WriteTempFileFromBase64(source.DataBase64, fileName),
                _ => throw new InvalidOperationException($"WeCom {mediaKind} delivery only supports hostPath or dataBase64.")
            };

            await using var fs = File.OpenRead(hostPath);
            var mediaId = await pusher.UploadMediaAsync(fs, fileName, mediaKind);
            if (mediaKind == "voice")
                await pusher.PushVoiceAsync(mediaId);
            else
                await pusher.PushFileAsync(mediaId);
        }
        finally
        {
            DeleteTempFile(tempPath);
        }
    }

    private static ChannelOutboundMessage? CreateMessageFromArgs(string kind, JsonObject args)
    {
        var filePath = args["filePath"]?.GetValue<string>();
        var fileBase64 = args["fileBase64"]?.GetValue<string>();
        var count = 0;
        if (!string.IsNullOrWhiteSpace(filePath)) count++;
        if (!string.IsNullOrWhiteSpace(fileBase64)) count++;
        if (count != 1)
            throw new InvalidOperationException("Exactly one of filePath or fileBase64 must be provided.");

        return new ChannelOutboundMessage
        {
            Kind = kind,
            FileName = args["fileName"]?.GetValue<string>(),
            Source = new ChannelMediaSource
            {
                Kind = !string.IsNullOrWhiteSpace(filePath) ? "hostPath" : "dataBase64",
                HostPath = filePath,
                DataBase64 = fileBase64
            }
        };
    }

    private static string WriteTempFileFromBase64(string? dataBase64, string fileName)
    {
        if (string.IsNullOrWhiteSpace(dataBase64))
            throw new InvalidOperationException("dataBase64 is required.");

        var extension = Path.GetExtension(fileName);
        var tempPath = Path.Combine(Path.GetTempPath(), $"dotcraft-wecom-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(tempPath, Convert.FromBase64String(dataBase64));
        return tempPath;
    }

    private static void DeleteTempFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
