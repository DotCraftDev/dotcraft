using DotCraft.Configuration;
using DotCraft.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace DotCraft.DashBoard;

public sealed class DashBoardServer : IAsyncDisposable
{
    private WebApplication? _app;
    
    private Task? _runTask;

    public void Start(TraceStore traceStore, AppConfig config, DotCraftPaths paths, TokenUsageStore? tokenUsageStore = null)
    {
        var dashBoardConfig = config.DashBoard;
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();

        var app = builder.Build();

        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers.Append("Access-Control-Allow-Origin", "*");
            ctx.Response.Headers.Append("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS");
            ctx.Response.Headers.Append("Access-Control-Allow-Headers", "Content-Type");

            if (ctx.Request.Method == "OPTIONS")
            {
                ctx.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            await next();
        });

        app.MapDashBoardAuth(config);
        app.UseDashBoardAuth(config);
        app.MapDashBoard(traceStore, config, paths, tokenUsageStore);

        var url = $"http://{dashBoardConfig.Host}:{dashBoardConfig.Port}";
        _app = app;
        _runTask = app.RunAsync(url);

        AnsiConsole.MarkupLine($"[green]DashBoard started at[/] [link={url}/dashboard]{url}/dashboard[/]");
    }

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        if (_runTask != null)
        {
            try { await _runTask; } catch { /* ignore */ }
        }
    }
}
