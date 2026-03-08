using System.ClientModel;
using System.Text.Json;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Commands.Custom;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.DashBoard;
using DotCraft.Heartbeat;
using DotCraft.Hosting;
using DotCraft.Hooks;
using DotCraft.Mcp;
using DotCraft.Memory;
using DotCraft.Modules;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Agents.AI.Hosting.AGUI.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;

namespace DotCraft.AGUI;

/// <summary>
/// Gateway channel service for AG-UI protocol. Runs a dedicated Kestrel server on AgUi.Host:AgUi.Port
/// and exposes a single POST endpoint (e.g. /ag-ui) that accepts AG-UI RunAgentInput and streams SSE events.
/// </summary>
public sealed class AGUIChannelService(
    IServiceProvider sp,
    AppConfig config,
    DotCraftPaths paths,
    SessionStore sessionStore,
    MemoryStore memoryStore,
    SkillsLoader skillsLoader,
    PathBlacklist blacklist,
    McpClientManager mcpClientManager,
    ModuleRegistry moduleRegistry)
    : IChannelService
{
    private WebApplication? _webApp;
    private AgentFactory? _agentFactory;

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
        _ = sessionStore; // reserved for future session key from threadId
        var agUiConfig = config.AgUi;
        var path = string.IsNullOrWhiteSpace(agUiConfig.Path) ? "/ag-ui" : agUiConfig.Path.Trim();
        var host = string.IsNullOrWhiteSpace(agUiConfig.Host) ? "0.0.0.0" : agUiConfig.Host;
        var port = agUiConfig.Port <= 0 ? 5100 : agUiConfig.Port;

        _agentFactory = BuildAgentFactory();
        var tools = _agentFactory.CreateDefaultTools();
        var agent = _agentFactory.CreateAgentWithTools(tools);

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAGUI();

        _webApp = builder.Build();

        var pathPrefix = path.TrimEnd('/');
        if (agUiConfig.RequireAuth && !string.IsNullOrWhiteSpace(agUiConfig.ApiKey))
        {
            var apiKey = agUiConfig.ApiKey!;
            _webApp.Use(async (context, next) =>
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
                        await context.Response.WriteAsync("Unauthorized", cancellationToken);
                        return;
                    }
                }
                await next();
            });
        }

        _webApp.Use(async (context, next) =>
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

                TracingChatClient.CurrentSessionKey = sessionKeyUsed;
                TracingChatClient.ResetCallState(sessionKeyUsed);
                await next();
            }
            finally
            {
                TracingChatClient.ResetCallState(sessionKeyUsed);
                TracingChatClient.CurrentSessionKey = null;
            }
        });

        _webApp.MapAGUI(path, agent);

        var url = $"http://{host}:{port}";
        AnsiConsole.MarkupLine($"[green][[Gateway]][/] AG-UI listening on {Markup.Escape(url)}{Markup.Escape(path)}");

        _ = _webApp.RunAsync(url);

        var tcs = new TaskCompletionSource();
        await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
        await tcs.Task;

        await StopAsync();
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
