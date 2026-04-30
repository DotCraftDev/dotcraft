using System.Text.Json;
using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerModelCatalogRuntimeConfigTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"model_catalog_runtime_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;

    public AppServerModelCatalogRuntimeConfigTests()
    {
        _workspaceCraftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task ModelList_UsesRuntimeConfigMonitorInsteadOfReloadingWorkspaceConfig()
    {
        await File.WriteAllTextAsync(
            Path.Combine(_workspaceCraftPath, "config.json"),
            """
            {
              "ApiKey": "disk-key",
              "EndPoint": "not-a-url"
            }
            """);

        var monitor = new AppConfigMonitor(new AppConfig
        {
            ApiKey = "",
            EndPoint = "http://127.0.0.1:8317/v1"
        });
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            appConfigMonitor: monitor);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.ModelList, new { }));

        var sent = await harness.Transport.WaitAndDrainAsync(1, TimeSpan.FromSeconds(5));
        var response = Assert.Single(sent);
        var result = response.RootElement.GetProperty("result");
        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Equal("MissingApiKey", result.GetProperty("errorCode").GetString());
        Assert.Contains("http://127.0.0.1:8317/v1", result.GetProperty("errorMessage").GetString());
    }

    [Fact]
    public async Task WorkspaceConfigUpdate_PreservesManagedProxyRuntimeOverrides()
    {
        var monitor = new AppConfigMonitor(new AppConfig
        {
            ApiKey = "proxy-key",
            EndPoint = "http://127.0.0.1:8317/v1"
        });
        using var harness = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            appConfigMonitor: monitor);
        await harness.InitializeAsync();

        var env = new Dictionary<string, string?>
        {
            [RuntimeConfigOverrides.ManagedProxyEndpoint] = "http://127.0.0.1:8317/v1",
            [RuntimeConfigOverrides.ManagedProxyApiKey] = "proxy-key"
        };

        await WithEnvironmentAsync(env, async () =>
        {
            await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.WorkspaceConfigUpdate, new
            {
                model = "gpt-4o-mini",
                apiKey = "disk-key",
                endPoint = "https://disk.example/v1"
            }));
        });

        Assert.Equal("gpt-4o-mini", monitor.Current.Model);
        Assert.Equal("proxy-key", monitor.Current.ApiKey);
        Assert.Equal("http://127.0.0.1:8317/v1", monitor.Current.EndPoint);

        var persisted = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_workspaceCraftPath, "config.json")));
        Assert.Equal("disk-key", persisted.RootElement.GetProperty("ApiKey").GetString());
        Assert.Equal("https://disk.example/v1", persisted.RootElement.GetProperty("EndPoint").GetString());
    }

    private static async Task WithEnvironmentAsync(IReadOnlyDictionary<string, string?> values, Func<Task> action)
    {
        var previous = values.ToDictionary(
            pair => pair.Key,
            pair => Environment.GetEnvironmentVariable(pair.Key),
            StringComparer.Ordinal);
        try
        {
            foreach (var (key, value) in values)
                Environment.SetEnvironmentVariable(key, value);

            await action();
        }
        finally
        {
            foreach (var (key, value) in previous)
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
