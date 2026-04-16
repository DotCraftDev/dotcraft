using DotCraft.Configuration;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class ExternalChannelManagementHooksTests : IDisposable
{
    private readonly string _workspaceCraftPath;
    private AppServerTestHarness? _h;

    public ExternalChannelManagementHooksTests()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"ExternalChannelHooks_{Guid.NewGuid():N}");
        _workspaceCraftPath = Path.Combine(tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
    }

    public void Dispose()
    {
        _h?.Dispose();
        try
        {
            var root = Directory.GetParent(_workspaceCraftPath)?.FullName;
            if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }

    [Fact]
    public async Task ExternalChannelUpsert_InvokesHookAfterPersist()
    {
        var hookCalls = 0;
        var persistedBeforeHook = false;
        _h = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            onExternalChannelUpserted: (entry, _) =>
            {
                hookCalls++;
                var configPath = Path.Combine(_workspaceCraftPath, "config.json");
                var channels = AppConfig.Load(configPath).ExternalChannels;
                persistedBeforeHook = channels.Any(c =>
                    string.Equals(c.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
                return Task.CompletedTask;
            });

        await _h.InitializeAsync();
        var request = _h.BuildRequest(AppServerMethods.ExternalChannelUpsert, new
        {
            channel = new
            {
                name = "weixin",
                enabled = true,
                transport = "websocket"
            }
        });

        await _h.ExecuteRequestAsync(request);
        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        Assert.Equal(1, hookCalls);
        Assert.True(persistedBeforeHook);
    }

    [Fact]
    public async Task ExternalChannelRemove_InvokesHookAfterPersist()
    {
        var removedName = string.Empty;
        var removedFromConfigBeforeHook = false;
        _h = new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            onExternalChannelRemoved: (name, _) =>
            {
                removedName = name;
                var configPath = Path.Combine(_workspaceCraftPath, "config.json");
                var channels = AppConfig.Load(configPath).ExternalChannels;
                removedFromConfigBeforeHook = channels.All(c =>
                    !string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
                return Task.CompletedTask;
            });

        await _h.InitializeAsync();

        var upsert = _h.BuildRequest(AppServerMethods.ExternalChannelUpsert, new
        {
            channel = new
            {
                name = "weixin",
                enabled = true,
                transport = "websocket"
            }
        });
        await _h.ExecuteRequestAsync(upsert);
        _ = await _h.Transport.ReadNextSentAsync();

        var remove = _h.BuildRequest(AppServerMethods.ExternalChannelRemove, new
        {
            name = "weixin"
        });
        await _h.ExecuteRequestAsync(remove);
        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        Assert.Equal("weixin", removedName);
        Assert.True(removedFromConfigBeforeHook);
    }
}
