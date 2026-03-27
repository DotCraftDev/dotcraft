using System.Text.Json;
using DotCraft.CLI.Rendering;
using DotCraft.Commands.Custom;
using DotCraft.Common;
using DotCraft.Configuration;
using DotCraft.Cron;
using DotCraft.Tracing;
using DotCraft.Hooks;
using DotCraft.Hosting;
using DotCraft.Mcp;
using DotCraft.Modules;
using DotCraft.Protocol.AppServer;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace DotCraft.CLI;

public sealed class CliHost(
    IServiceProvider sp,
    AppConfig config,
    DotCraftPaths paths,
    SkillsLoader skillsLoader,
    McpClientManager mcpClientManager,
    ModuleRegistry moduleRegistry) : IDotCraftHost
{
    private AppServerProcess? _appServerProcess;
    private WebSocketClientConnection? _wsConnection;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var tokenUsageStore = sp.GetService<TokenUsageStore>();
        var hookRunner = sp.GetService<HookRunner>();
        var cliConfig = config.GetSection<CliConfig>("CLI");

        ToolProviderCollector.ScanToolIcons(moduleRegistry, config);

        ICliSession cliSession;
        CliBackendInfo backendInfo;
        AppServerWireClient wire;
        string? dashBoardUrl = null;

        if (!string.IsNullOrWhiteSpace(cliConfig.AppServerUrl))
        {
            // -------------------------------------------------------------------
            // WebSocket mode: connect to an already-running AppServer via WebSocket
            // -------------------------------------------------------------------
            var wsUri = new Uri(cliConfig.AppServerUrl);
            AnsiConsole.MarkupLine($"[grey][[CLI]][/] Connecting to AppServer at {wsUri}...");

            _wsConnection = await WebSocketClientConnection.ConnectAsync(
                wsUri,
                cliConfig.AppServerToken,
                cancellationToken);

            var wsInitResponse = await _wsConnection.Wire.InitializeAsync(
                clientName: "dotcraft-cli",
                clientVersion: AppVersion.Informational,
                approvalSupport: true,
                streamingSupport: true);

            wire = _wsConnection.Wire;
            cliSession = new WireCliSession(wire, tokenUsageStore);
            dashBoardUrl = TryGetDashboardUrl(wsInitResponse);
            backendInfo = new CliBackendInfo
            {
                ServerVersion = TryGetServerVersion(wsInitResponse),
                ServerUrl = wsUri.ToString()
            };
        }
        else
        {
            // -------------------------------------------------------------------
            // Subprocess mode (default): spawn dotcraft app-server as a subprocess
            // -------------------------------------------------------------------
            AnsiConsole.MarkupLine("[grey][[CLI]][/] Starting AppServer subprocess...");

            var dotcraftBin = cliConfig.AppServerBin;
            _appServerProcess = await AppServerProcess.StartAsync(
                dotcraftBin: dotcraftBin,
                workspacePath: paths.WorkspacePath,
                ct: cancellationToken);

            _appServerProcess.OnCrashed += () =>
                AnsiConsole.MarkupLine("[red][[CLI]][/] AppServer subprocess exited unexpectedly.");

            wire = _appServerProcess.Wire;
            cliSession = new WireCliSession(wire, tokenUsageStore);
            backendInfo = new CliBackendInfo
            {
                ServerVersion = _appServerProcess.ServerVersion,
                ProcessId = _appServerProcess.ProcessId
            };

            dashBoardUrl = _appServerProcess.DashboardUrl;
        }

        var cronService = sp.GetService<CronService>();

        var customCommandLoader = sp.GetService<CustomCommandLoader>();
        var repl = new ReplHost(skillsLoader, cliSession,
            paths.WorkspacePath, paths.CraftPath,
            cronService: cronService,
            mcpClientManager: mcpClientManager,
            dashBoardUrl: dashBoardUrl,
            customCommandLoader: customCommandLoader,
            hookRunner: hookRunner,
            backendInfo: backendInfo,
            wireClient: wire);

        // Background listener for out-of-band server notifications (e.g. system/jobResult).
        // Cron and heartbeat now run server-side; results arrive as wire notifications
        // while the REPL is idle. The loop drains system/jobResult and displays them.
        using var notifCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var notifTask = Task.Run(() => ListenForJobResultsAsync(wire, repl, notifCts.Token), notifCts.Token);

        await repl.RunAsync(cancellationToken);

        notifCts.Cancel();
        try { await notifTask; } catch (OperationCanceledException) { }

        await cliSession.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_appServerProcess != null)
            await _appServerProcess.DisposeAsync();

        if (_wsConnection != null)
            await _wsConnection.DisposeAsync();
    }

    /// <summary>
    /// Background loop that drains <c>system/jobResult</c> notifications arriving on the wire
    /// while the REPL is idle and prints them cleanly, suspending and restoring the prompt.
    /// </summary>
    private static async Task ListenForJobResultsAsync(
        AppServerWireClient wire, ReplHost repl, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var notif = await wire.WaitForJobResultAsync(
                timeout: TimeSpan.FromSeconds(5),
                ct: ct);

            if (notif == null)
                continue;

            try
            {
                var p = notif.RootElement.GetProperty("params");
                var source = p.TryGetProperty("source", out var s) ? s.GetString() : null;
                var jobName = p.TryGetProperty("jobName", out var n) ? n.GetString() : null;
                var result = p.TryGetProperty("result", out var r) ? r.GetString() : null;
                var error = p.TryGetProperty("error", out var e) ? e.GetString() : null;

                var tag = string.Equals(source, "heartbeat", StringComparison.OrdinalIgnoreCase)
                    ? "Heartbeat" : "Cron";

                repl.WriteExternalOutput(() =>
                {
                    // Header line: [Cron] JobName  (or [Heartbeat])
                    var header = jobName != null
                        ? $"[grey][[{tag}]][/] [bold]{Markup.Escape(jobName)}[/]"
                        : $"[grey][[{tag}]][/]";
                    AnsiConsole.MarkupLine(header);

                    if (error != null)
                        AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");
                    else if (!string.IsNullOrEmpty(result))
                        MarkdownConsoleRenderer.Render(result);
                });
            }
            catch
            {
                // Malformed notification — ignore and continue
            }
        }
    }

    private static string? TryGetServerVersion(JsonDocument response)
    {
        try
        {
            return response.RootElement
                .GetProperty("result")
                .GetProperty("serverInfo")
                .GetProperty("version")
                .GetString();
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetDashboardUrl(JsonDocument response)
    {
        try
        {
            var result = response.RootElement.GetProperty("result");
            if (!result.TryGetProperty("dashboardUrl", out var el))
                return null;
            return el.GetString();
        }
        catch
        {
            return null;
        }
    }
}
