using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DotCraft.Hub;

namespace DotCraft.Tests.Hub;

public sealed class HubHostTests : IDisposable
{
    private readonly string _userProfile = Path.Combine(
        Path.GetTempPath(),
        "DotCraftHubHost_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RunAsync_StatusAndShutdownEndpointsWork()
    {
        var paths = HubPaths.Resolve(_userProfile);
        await using var host = new HubHost(new HubConfig { Port = 0 }, paths);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var runTask = host.RunAsync(cts.Token);
        var info = await WaitForLockAsync(paths.LockFilePath, runTask, cts.Token);

        using var http = new HttpClient();
        var status = await http.GetStringAsync($"{info.ApiBaseUrl}/v1/status", cts.Token);
        using var statusDoc = JsonDocument.Parse(status);
        var root = statusDoc.RootElement;

        Assert.Equal(Environment.ProcessId, root.GetProperty("pid").GetInt32());
        Assert.Equal(paths.HubStatePath, root.GetProperty("statePath").GetString());
        Assert.False(root.GetProperty("capabilities").GetProperty("appServerManagement").GetBoolean());
        Assert.False(root.GetProperty("capabilities").GetProperty("portManagement").GetBoolean());
        Assert.False(root.GetProperty("capabilities").GetProperty("events").GetBoolean());
        Assert.False(root.GetProperty("capabilities").GetProperty("notifications").GetBoolean());
        Assert.False(root.GetProperty("capabilities").GetProperty("tray").GetBoolean());

        var unauthorized = await http.PostAsync($"{info.ApiBaseUrl}/v1/shutdown", content: null, cts.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{info.ApiBaseUrl}/v1/shutdown");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", info.Token);
        var shutdown = await http.SendAsync(request, cts.Token);
        Assert.True(shutdown.IsSuccessStatusCode);

        await runTask.WaitAsync(cts.Token);
        Assert.False(File.Exists(paths.LockFilePath));
    }

    private static async Task<HubLockInfo> WaitForLockAsync(string lockFilePath, Task runTask, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (runTask.IsCompleted)
            {
                await runTask;
                throw new InvalidOperationException("Hub exited before publishing its lock file.");
            }

            var info = HubLockFile.TryRead(lockFilePath);
            if (info is not null)
                return info;

            await Task.Delay(50, ct);
        }

        throw new OperationCanceledException(ct);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_userProfile))
                Directory.Delete(_userProfile, recursive: true);
        }
        catch
        {
            // ignored
        }
    }
}
