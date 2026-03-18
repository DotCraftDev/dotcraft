using DotCraft.Configuration;
using DotCraft.DashBoard;
using DotCraft.Tracing;
using DotCraft.Hosting;
using DotCraft.Localization;
using Spectre.Console;

namespace DotCraft.Setup;

/// <summary>
/// Lightweight setup-only host that exposes the Dashboard config UI before the
/// normal agent runtime can start.
/// </summary>
public sealed class SetupHost(AppConfig config, DotCraftPaths paths)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var setupConfig = CloneConfigForSetup(config);
        var traceStore = new TraceStore();

        await using var dashBoardServer = new DashBoardServer();
        dashBoardServer.Start(traceStore, setupConfig, paths, setupMode: true,
            configTypes: ConfigSchemaRegistrations.GetAllConfigTypes());

        var url = $"http://{setupConfig.DashBoard.Host}:{setupConfig.DashBoard.Port}/dashboard";
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[yellow]{Strings.SetupMode}[/]");
        AnsiConsole.MarkupLine(Strings.SetupOpenBrowser(url));
        AnsiConsole.MarkupLine(Strings.SetupAfterSave);

        await WaitForShutdownSignalAsync(cancellationToken);
    }

    private static AppConfig CloneConfigForSetup(AppConfig source)
    {
        return new AppConfig
        {
            ApiKey = source.ApiKey,
            Model = source.Model,
            EndPoint = source.EndPoint,
            Reasoning = new AppConfig.ReasoningConfig
            {
                Enabled = source.Reasoning.Enabled,
                Effort = source.Reasoning.Effort,
                Output = source.Reasoning.Output
            },
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
            Security = source.Security,
            Heartbeat = source.Heartbeat,
            Cron = source.Cron,
            Hooks = source.Hooks,
            Logging = source.Logging,
            McpServers = [.. source.McpServers],
            // Copy module extension data (QQBot, WeCom, WeComBot, Api, AgUi, Acp, GitHubTracker, etc.)
            ExtensionData = source.ExtensionData != null
                ? new Dictionary<string, System.Text.Json.JsonElement>(source.ExtensionData)
                : null,
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
