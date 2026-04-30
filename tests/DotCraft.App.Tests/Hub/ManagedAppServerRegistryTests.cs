using DotCraft.Hub;

namespace DotCraft.Tests.Hub;

public sealed class ManagedAppServerRegistryTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "DotCraftManagedRegistry_" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void RequiresAppServerRestartForApiProxy_TracksRuntimeIdentity()
    {
        var binary = Touch("cliproxyapi.exe");
        var config = Touch("proxy.yaml");
        var otherBinary = Touch("cliproxyapi-next.exe");
        var otherConfig = Touch("proxy-next.yaml");
        var request = new HubApiProxySidecarRequest
        {
            Enabled = true,
            BinaryPath = binary,
            ConfigPath = config,
            Endpoint = "http://127.0.0.1:8317/v1",
            ApiKey = "proxy-key"
        };

        Assert.False(ManagedAppServerRegistry.RequiresAppServerRestartForApiProxy(
            request,
            currentEndpoint: "http://127.0.0.1:8317/v1",
            currentBinaryPath: binary,
            currentConfigPath: config,
            currentApiKey: "proxy-key"));

        Assert.True(ManagedAppServerRegistry.RequiresAppServerRestartForApiProxy(
            request,
            currentEndpoint: "http://127.0.0.1:8317/v1",
            currentBinaryPath: binary,
            currentConfigPath: config,
            currentApiKey: "old-proxy-key"));

        Assert.True(ManagedAppServerRegistry.RequiresAppServerRestartForApiProxy(
            request,
            currentEndpoint: "http://127.0.0.1:8317/v1",
            currentBinaryPath: otherBinary,
            currentConfigPath: config,
            currentApiKey: "proxy-key"));

        Assert.True(ManagedAppServerRegistry.RequiresAppServerRestartForApiProxy(
            request,
            currentEndpoint: "http://127.0.0.1:8317/v1",
            currentBinaryPath: binary,
            currentConfigPath: otherConfig,
            currentApiKey: "proxy-key"));
    }

    [Fact]
    public void RequiresAppServerRestartForApiProxy_StopsWhenProxyDisabled()
    {
        Assert.True(ManagedAppServerRegistry.RequiresAppServerRestartForApiProxy(
            apiProxy: null,
            currentEndpoint: "http://127.0.0.1:8317/v1",
            currentBinaryPath: null,
            currentConfigPath: null,
            currentApiKey: null));

        Assert.False(ManagedAppServerRegistry.RequiresAppServerRestartForApiProxy(
            apiProxy: null,
            currentEndpoint: null,
            currentBinaryPath: null,
            currentConfigPath: null,
            currentApiKey: null));
    }

    private string Touch(string fileName)
    {
        Directory.CreateDirectory(_tempDir);
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // ignored
        }
    }
}
