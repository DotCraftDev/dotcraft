using DotCraft.Configuration;
using DotCraft.DashBoard;
using DotCraft.Hosting;
using DotCraft.Localization;
using Spectre.Console;

namespace DotCraft.Setup;

/// <summary>
/// Lightweight setup-only host that exposes the Dashboard config UI before the
/// normal agent runtime can start.
/// </summary>
public sealed class SetupHost(AppConfig config, DotCraftPaths paths, LanguageService languageService)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var setupConfig = CloneConfigForSetup(config);
        var traceStore = new TraceStore();

        await using var dashBoardServer = new DashBoardServer();
        dashBoardServer.Start(traceStore, setupConfig, paths, setupMode: true);

        var url = $"http://{setupConfig.DashBoard.Host}:{setupConfig.DashBoard.Port}/dashboard";
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[yellow]{languageService.GetString("当前处于初始化配置模式。", "DotCraft is running in setup mode.")}[/]");
        AnsiConsole.MarkupLine(languageService.GetString(
            $"请在浏览器中打开 {url} 完成全局和工作区配置。",
            $"Open {url} in your browser to finish global and workspace configuration."));
        AnsiConsole.MarkupLine(languageService.GetString(
            "保存完成后，请按 Ctrl+C 停止当前进程，然后重新运行 `dotcraft`。",
            "After saving, press Ctrl+C to stop this process, then run `dotcraft` again."));

        await WaitForShutdownSignalAsync(cancellationToken);
    }

    private static AppConfig CloneConfigForSetup(AppConfig source)
    {
        return new AppConfig
        {
            ApiKey = source.ApiKey,
            Model = source.Model,
            EndPoint = source.EndPoint,
            Language = source.Language,
            MaxToolCallRounds = source.MaxToolCallRounds,
            SubagentMaxToolCallRounds = source.SubagentMaxToolCallRounds,
            SubagentMaxConcurrency = source.SubagentMaxConcurrency,
            MaxSessionQueueSize = source.MaxSessionQueueSize,
            CompactSessions = source.CompactSessions,
            MaxContextTokens = source.MaxContextTokens,
            MemoryWindow = source.MemoryWindow,
            DebugMode = source.DebugMode,
            EnabledTools = [.. source.EnabledTools],
            Tools = source.Tools,
            QQBot = source.QQBot,
            Security = source.Security,
            Heartbeat = source.Heartbeat,
            WeCom = source.WeCom,
            WeComBot = source.WeComBot,
            Cron = source.Cron,
            Api = source.Api,
            AgUi = source.AgUi,
            Acp = source.Acp,
            Hooks = source.Hooks,
            McpServers = [.. source.McpServers],
            DashBoard = new AppConfig.DashBoardConfig
            {
                Enabled = true,
                Host = "127.0.0.1",
                Port = source.DashBoard.Port,
                Username = string.Empty,
                Password = string.Empty
            }
        };
    }

    private static async Task WaitForShutdownSignalAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource();
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            tcs.TrySetResult();
        };

        Console.CancelKeyPress += handler;
        try
        {
            await using var reg = cancellationToken.Register(() => tcs.TrySetResult());
            await tcs.Task;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }
}
