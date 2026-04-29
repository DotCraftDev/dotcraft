using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using DotCraft.Common;
using DotCraft.Hosting;

namespace DotCraft.Hub;

/// <summary>
/// Workspace-independent local Hub host.
/// </summary>
public sealed class HubHost : IDotCraftHost
{
    private readonly HubConfig _config;
    private readonly HubPaths _paths;
    private readonly CancellationTokenSource _shutdownCts = new();
    private WebApplication? _app;
    private HubLockFile? _lockFile;
    private bool _disposed;

    /// <summary>
    /// Creates a new Hub host.
    /// </summary>
    public HubHost(HubConfig config, HubPaths paths)
    {
        _config = config;
        _paths = paths;
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (!HubLockFile.TryAcquire(_paths, out _lockFile, out var existingInfo))
        {
            var hint = existingInfo is null
                ? "Hub already running."
                : $"Hub already running at {existingInfo.ApiBaseUrl} (pid {existingInfo.Pid}).";
            AnsiConsole.MarkupLine($"[yellow][[Hub]][/] {Markup.Escape(hint)}");
            return;
        }

        var lockFile = _lockFile;
        if (lockFile is null)
            throw new InvalidOperationException("Hub lock acquisition returned no lock file.");

        if (existingInfo is not null && !existingInfo.IsProcessAlive())
        {
            AnsiConsole.MarkupLine("[grey][[Hub]][/] Recovered stale hub.lock");
        }

        var port = _config.Port == 0 ? HubPortAllocator.AllocateLoopbackPort() : _config.Port;
        var host = NormalizeHost(_config.Host);
        var apiBaseUrl = $"http://{host}:{port}";
        var token = CreateToken();
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            _app = BuildApp(apiBaseUrl, token, startedAt);
            _app.Urls.Add(apiBaseUrl);
            await _app.StartAsync(cancellationToken);

            var lockInfo = new HubLockInfo(
                Pid: Environment.ProcessId,
                ApiBaseUrl: apiBaseUrl,
                Token: token,
                StartedAt: startedAt,
                Version: AppVersion.Informational);
            lockFile.Publish(lockInfo);

            AnsiConsole.MarkupLine($"[green][[Hub]][/] DotCraft Hub started at {Markup.Escape(apiBaseUrl)}");

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, linkedCts.Token);
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
            {
            }
        }
        finally
        {
            if (_app is not null)
            {
                await _app.StopAsync(CancellationToken.None);
                await _app.DisposeAsync();
                _app = null;
            }

            _lockFile?.DeleteAfterDispose();
            _lockFile = null;
            AnsiConsole.MarkupLine("[grey][[Hub]][/] Hub stopped");
        }
    }

    private WebApplication BuildApp(string apiBaseUrl, string token, DateTimeOffset startedAt)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        var app = builder.Build();

        app.MapGet("/v1/status", () => Results.Json(CreateStatus(apiBaseUrl, startedAt), HubJson.Options));
        app.MapPost("/v1/shutdown", (HttpRequest request) =>
        {
            if (!IsAuthorized(request, token))
            {
                return Results.Json(
                    new HubErrorResponse(new HubError("unauthorized", "Missing or invalid Hub token.")),
                    HubJson.Options,
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            _shutdownCts.Cancel();
            return Results.Json(new { ok = true }, HubJson.Options);
        });

        return app;
    }

    private HubStatusResponse CreateStatus(string apiBaseUrl, DateTimeOffset startedAt)
        => new(
            HubVersion: AppVersion.Informational,
            Pid: Environment.ProcessId,
            StartedAt: startedAt,
            StatePath: _paths.HubStatePath,
            ApiBaseUrl: apiBaseUrl,
            Capabilities: new HubCapabilities(
                AppServerManagement: false,
                PortManagement: false,
                Events: false,
                Notifications: false,
                Tray: false));

    private static bool IsAuthorized(HttpRequest request, string token)
    {
        var authorization = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && string.Equals(authorization[prefix.Length..], token, StringComparison.Ordinal);
    }

    private static string NormalizeHost(string host)
        => string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _shutdownCts.Cancel();
        if (_app is not null)
        {
            await _app.StopAsync(CancellationToken.None);
            await _app.DisposeAsync();
            _app = null;
        }

        _lockFile?.DeleteAfterDispose();
        _lockFile = null;
        _shutdownCts.Dispose();
    }
}

/// <summary>
/// Hub status response.
/// </summary>
public sealed record HubStatusResponse(
    string HubVersion,
    int Pid,
    DateTimeOffset StartedAt,
    string StatePath,
    string ApiBaseUrl,
    HubCapabilities Capabilities);

/// <summary>
/// M1 Hub capability flags.
/// </summary>
public sealed record HubCapabilities(
    bool AppServerManagement,
    bool PortManagement,
    bool Events,
    bool Notifications,
    bool Tray);

/// <summary>
/// Hub API error response.
/// </summary>
public sealed record HubErrorResponse(HubError Error);

/// <summary>
/// Hub API error payload.
/// </summary>
public sealed record HubError(string Code, string Message);
