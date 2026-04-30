using DotCraft.Agui;
using DotCraft.Api;
using DotCraft.AppServer;
using DotCraft.Configuration;

namespace DotCraft.Tests.AppServer;

public sealed class ManagedAppServerEnvironmentTests
{
    [Fact]
    public void ApplyTo_AppliesRuntimeOverridesWithoutPersistingConfig()
    {
        var config = new AppConfig();
        config.SetSection("Api", new ApiConfig { Enabled = true, Host = "0.0.0.0", Port = 8080 });
        config.SetSection("AgUi", new AguiConfig { Enabled = true, Host = "0.0.0.0", Port = 5100, Path = "/ag-ui" });
        config.DashBoard.Host = "0.0.0.0";
        config.DashBoard.Port = 8080;

        var env = new Dictionary<string, string?>
        {
            [ManagedAppServerEnvironment.ManagedFlag] = "1",
            [ManagedAppServerEnvironment.WebSocketHost] = "127.0.0.1",
            [ManagedAppServerEnvironment.WebSocketPort] = "43101",
            [ManagedAppServerEnvironment.WebSocketToken] = "ws-token",
            [ManagedAppServerEnvironment.DashboardHost] = "127.0.0.1",
            [ManagedAppServerEnvironment.DashboardPort] = "43102",
            [ManagedAppServerEnvironment.ApiHost] = "127.0.0.1",
            [ManagedAppServerEnvironment.ApiPort] = "43103",
            [ManagedAppServerEnvironment.AguiHost] = "127.0.0.1",
            [ManagedAppServerEnvironment.AguiPort] = "43104",
            [ManagedAppServerEnvironment.ProxyEndpoint] = "http://127.0.0.1:8317/v1",
            [ManagedAppServerEnvironment.ProxyApiKey] = "proxy-key"
        };

        WithEnvironment(env, () => ManagedAppServerEnvironment.ApplyTo(config));

        var appServer = config.GetSection<AppServerConfig>("AppServer");
        Assert.Equal(AppServerMode.StdioAndWebSocket, appServer.Mode);
        Assert.Equal("127.0.0.1", appServer.WebSocket.Host);
        Assert.Equal(43101, appServer.WebSocket.Port);
        Assert.Equal("ws-token", appServer.WebSocket.Token);
        Assert.Equal("127.0.0.1", config.DashBoard.Host);
        Assert.Equal(43102, config.DashBoard.Port);

        var api = config.GetSection<ApiConfig>("Api");
        Assert.Equal("127.0.0.1", api.Host);
        Assert.Equal(43103, api.Port);

        var agui = config.GetSection<AguiConfig>("AgUi");
        Assert.Equal("127.0.0.1", agui.Host);
        Assert.Equal(43104, agui.Port);

        Assert.Equal("http://127.0.0.1:8317/v1", config.EndPoint);
        Assert.Equal("proxy-key", config.ApiKey);
    }

    private static void WithEnvironment(IReadOnlyDictionary<string, string?> values, Action action)
    {
        var previous = values.ToDictionary(
            pair => pair.Key,
            pair => Environment.GetEnvironmentVariable(pair.Key),
            StringComparer.Ordinal);
        try
        {
            foreach (var (key, value) in values)
                Environment.SetEnvironmentVariable(key, value);

            action();
        }
        finally
        {
            foreach (var (key, value) in previous)
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
