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
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;

namespace DotCraft.QQ;

/// <summary>
/// Gateway channel service for QQ Bot. Manages the QQ WebSocket connection,
/// channel adapter, and agent lifecycle as part of a multi-channel gateway.
/// </summary>
public sealed class QQChannelService(
    IServiceProvider sp,
    AppConfig config,
    DotCraftPaths paths,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    McpClientManager mcpClientManager,
    QQBotClient qqClient,
    QQPermissionService permissionService,
    QQApprovalService qqApprovalService,
    ModuleRegistry moduleRegistry)
    : IChannelService
{
    private QQChannelAdapter? _adapter;
    private static readonly ChannelDeliveryCapabilities DeliveryCapabilities = new()
    {
        StructuredDelivery = true,
        Media = new ChannelMediaCapabilitySet
        {
            Audio = new ChannelMediaConstraints
            {
                SupportsHostPath = true,
                SupportsUrl = true,
                SupportsBase64 = true
            },
            File = new ChannelMediaConstraints
            {
                SupportsHostPath = true,
                SupportsBase64 = true
            },
            Video = new ChannelMediaConstraints
            {
                SupportsHostPath = true,
                SupportsUrl = true
            }
        }
    };

    private static readonly IReadOnlyList<ChannelToolDescriptor> ChannelTools =
    [
        new()
        {
            Name = "qqSendVoiceToCurrentChat",
            Description = "Send a voice/audio message to the current QQ chat.",
            RequiresChatContext = true,
            InputSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["filePath"] = new JsonObject { ["type"] = "string" },
                    ["fileUrl"] = new JsonObject { ["type"] = "string" },
                    ["fileBase64"] = new JsonObject { ["type"] = "string" }
                }
            }
        },
        new()
        {
            Name = "qqSendVideoToCurrentChat",
            Description = "Send a video message to the current QQ chat.",
            RequiresChatContext = true,
            InputSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["filePath"] = new JsonObject { ["type"] = "string" },
                    ["fileUrl"] = new JsonObject { ["type"] = "string" }
                }
            }
        },
        new()
        {
            Name = "qqSendFileToCurrentChat",
            Description = "Upload a file to the current QQ chat.",
            RequiresChatContext = true,
            InputSchema = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["filePath"] = new JsonObject { ["type"] = "string" },
                    ["fileBase64"] = new JsonObject { ["type"] = "string" },
                    ["fileName"] = new JsonObject { ["type"] = "string" },
                    ["folder"] = new JsonObject { ["type"] = "string" }
                }
            }
        }
    ];

    public string Name => "qq";

    /// <inheritdoc />
    public HeartbeatService? HeartbeatService { get; set; }

    /// <inheritdoc />
    public CronService? CronService { get; set; }

    /// <inheritdoc />
    public IApprovalService ApprovalService => qqApprovalService;

    public ChannelDeliveryCapabilities? GetDeliveryCapabilities() => DeliveryCapabilities;

    public IReadOnlyList<ChannelToolDescriptor> GetChannelTools() => ChannelTools;

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
                var ctx = QQChatContextScope.Current;
                if (ctx == null) return;
                var text = PlanStore.RenderPlanPlainText(plan);
                if (ctx.IsGroupMessage)
                    _ = qqClient.SendGroupMessageAsync(ctx.GroupId, text);
                else
                    _ = qqClient.SendPrivateMessageAsync(ctx.UserId, text);
            },
            hookRunner: hookRunner);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var scopedApproval = new SessionScopedApprovalService(qqApprovalService);
        var agentFactory = BuildAgentFactory(scopedApproval);
        var agent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        var tokenUsageStore = sp.GetService<TokenUsageStore>();

        var activeRunRegistry = sp.GetRequiredService<ActiveRunRegistry>();
        var customCommandLoader = sp.GetService<CustomCommandLoader>();
        var sessionService = SessionServiceFactory.Create(agentFactory, agent, sp);
        _adapter = new QQChannelAdapter(
            qqClient,
            permissionService, activeRunRegistry,
            qqApprovalService,
            heartbeatService: HeartbeatService,
            cronService: CronService,
            agentFactory: agentFactory,
            tokenUsageStore: tokenUsageStore,
            customCommandLoader: customCommandLoader,
            sessionService: sessionService,
            workspacePath: paths.WorkspacePath
        );

        await qqClient.StartAsync(cancellationToken);

        AnsiConsole.MarkupLine(
            $"[green][[Gateway]][/] QQ Bot listening on ws://{config.GetSection<QQBotConfig>("QQBot").Host}:{config.GetSection<QQBotConfig>("QQBot").Port}/");

        var tcs = new TaskCompletionSource();
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;

        await StopAsync();
    }

    public async Task StopAsync()
    {
        await qqClient.StopAsync();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAdminTargets()
    {
        return config.GetSection<QQBotConfig>("QQBot").AdminUsers
            .Select(id => id.ToString())
            .ToList();
    }

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
            var destination = ParseTarget(target);
            return message.Kind.ToLowerInvariant() switch
            {
                "text" => await DeliverTextAsync(destination, message.Text ?? string.Empty),
                "audio" => await DeliverAudioAsync(destination, message),
                "video" => await DeliverVideoAsync(destination, message),
                "file" => await DeliverFileAsync(destination, message),
                _ => new ExtChannelSendResult
                {
                    Delivered = false,
                    ErrorCode = "UnsupportedDeliveryKind",
                    ErrorMessage = $"QQ channel does not support '{message.Kind}' delivery."
                }
            };
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
            var target = ResolveCurrentChatTarget(request.Context);
            if (target == null)
            {
                return new ExtChannelToolCallResult
                {
                    Success = false,
                    ErrorCode = "MissingChatContext",
                    ErrorMessage = "QQ tool execution requires a current chat context."
                };
            }

            var message = request.Tool switch
            {
                "qqSendVoiceToCurrentChat" => CreateMessageFromArgs("audio", request.Arguments),
                "qqSendVideoToCurrentChat" => CreateMessageFromArgs("video", request.Arguments),
                "qqSendFileToCurrentChat" => CreateMessageFromArgs("file", request.Arguments),
                _ => null
            };

            if (message == null)
            {
                return new ExtChannelToolCallResult
                {
                    Success = false,
                    ErrorCode = "UnsupportedChannelTool",
                    ErrorMessage = $"QQ does not expose tool '{request.Tool}'."
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
    }

    private async Task<ExtChannelSendResult> DeliverTextAsync((bool IsGroup, long Id) destination, string content)
    {
        var resp = destination.IsGroup
            ? await qqClient.SendGroupMessageAsync(destination.Id, content)
            : await qqClient.SendPrivateMessageAsync(destination.Id, content);
        return ToSendResult(resp);
    }

    private async Task<ExtChannelSendResult> DeliverAudioAsync((bool IsGroup, long Id) destination, ChannelOutboundMessage message)
    {
        var file = await ResolveAudioSourceAsync(message);
        var resp = destination.IsGroup
            ? await qqClient.SendGroupRecordAsync(destination.Id, file)
            : await qqClient.SendPrivateRecordAsync(destination.Id, file);
        return ToSendResult(resp);
    }

    private async Task<ExtChannelSendResult> DeliverVideoAsync((bool IsGroup, long Id) destination, ChannelOutboundMessage message)
    {
        var source = message.Source ?? throw new InvalidOperationException("Video delivery requires a media source.");
        var file = source.Kind switch
        {
            "hostPath" => source.HostPath ?? throw new InvalidOperationException("hostPath is required."),
            "url" => source.Url ?? throw new InvalidOperationException("url is required."),
            _ => throw new InvalidOperationException("QQ video delivery only supports hostPath or url.")
        };

        var resp = destination.IsGroup
            ? await qqClient.SendGroupVideoAsync(destination.Id, file)
            : await qqClient.SendPrivateVideoAsync(destination.Id, file);
        return ToSendResult(resp);
    }

    private async Task<ExtChannelSendResult> DeliverFileAsync((bool IsGroup, long Id) destination, ChannelOutboundMessage message)
    {
        var source = message.Source ?? throw new InvalidOperationException("File delivery requires a media source.");
        var fileName = !string.IsNullOrWhiteSpace(message.FileName)
            ? message.FileName
            : source.HostPath != null ? Path.GetFileName(source.HostPath) : "attachment.bin";

        string? tempPath = null;
        try
        {
            var hostPath = source.Kind switch
            {
                "hostPath" => source.HostPath ?? throw new InvalidOperationException("hostPath is required."),
                "dataBase64" => tempPath = WriteTempFileFromBase64(source.DataBase64, fileName),
                _ => throw new InvalidOperationException("QQ file delivery only supports hostPath or dataBase64.")
            };

            var resp = destination.IsGroup
                ? await qqClient.UploadGroupFileAsync(destination.Id, hostPath, fileName, null)
                : await qqClient.UploadPrivateFileAsync(destination.Id, hostPath, fileName);
            return ToSendResult(resp);
        }
        finally
        {
            DeleteTempFile(tempPath);
        }
    }

    private async Task<string> ResolveAudioSourceAsync(ChannelOutboundMessage message)
    {
        var source = message.Source ?? throw new InvalidOperationException("Audio delivery requires a media source.");
        return source.Kind switch
        {
            "hostPath" => "base64://" + Convert.ToBase64String(await File.ReadAllBytesAsync(source.HostPath ?? throw new InvalidOperationException("hostPath is required."))),
            "url" => source.Url ?? throw new InvalidOperationException("url is required."),
            "dataBase64" => "base64://" + (source.DataBase64 ?? throw new InvalidOperationException("dataBase64 is required.")),
            _ => throw new InvalidOperationException("Unsupported QQ audio source kind.")
        };
    }

    private static (bool IsGroup, long Id) ParseTarget(string target)
    {
        if (target.StartsWith("group:", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(target["group:".Length..], out var groupId))
        {
            return (true, groupId);
        }

        if (long.TryParse(target, out var userId))
            return (false, userId);

        throw new InvalidOperationException($"Invalid QQ target '{target}'.");
    }

    private static string? ResolveCurrentChatTarget(ExtChannelToolCallContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.GroupId))
            return $"group:{context.GroupId}";

        if (!string.IsNullOrWhiteSpace(context.SenderId))
            return context.SenderId;

        return null;
    }

    private static ChannelOutboundMessage? CreateMessageFromArgs(string kind, JsonObject args)
    {
        var filePath = args["filePath"]?.GetValue<string>();
        var fileUrl = args["fileUrl"]?.GetValue<string>();
        var fileBase64 = args["fileBase64"]?.GetValue<string>();
        var count = 0;
        if (!string.IsNullOrWhiteSpace(filePath)) count++;
        if (!string.IsNullOrWhiteSpace(fileUrl)) count++;
        if (!string.IsNullOrWhiteSpace(fileBase64)) count++;
        if (count != 1)
            throw new InvalidOperationException("Exactly one of filePath, fileUrl, or fileBase64 must be provided.");

        var source = new ChannelMediaSource
        {
            Kind = !string.IsNullOrWhiteSpace(filePath)
                ? "hostPath"
                : !string.IsNullOrWhiteSpace(fileUrl)
                    ? "url"
                    : "dataBase64",
            HostPath = filePath,
            Url = fileUrl,
            DataBase64 = fileBase64
        };

        return new ChannelOutboundMessage
        {
            Kind = kind,
            FileName = args["fileName"]?.GetValue<string>(),
            Source = source
        };
    }

    private static ExtChannelSendResult ToSendResult(OneBot.OneBotActionResponse response)
        => new()
        {
            Delivered = response.IsOk,
            ErrorCode = response.IsOk ? null : "AdapterDeliveryFailed",
            ErrorMessage = response.IsOk ? null : response.Message
        };

    private static string WriteTempFileFromBase64(string? dataBase64, string fileName)
    {
        if (string.IsNullOrWhiteSpace(dataBase64))
            throw new InvalidOperationException("dataBase64 is required.");

        var extension = Path.GetExtension(fileName);
        var tempPath = Path.Combine(Path.GetTempPath(), $"dotcraft-qq-{Guid.NewGuid():N}{extension}");
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
