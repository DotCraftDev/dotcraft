using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tracing;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerBrowserUseTests
{
    [Fact]
    public async Task ThreadStart_WithBrowserUseCapability_BindsThreadAndRefreshesAgent()
    {
        var proxy = new WireBrowserUseProxy();
        using var harness = new AppServerTestHarness(wireBrowserUseProxy: proxy);
        await harness.InitializeAsync(browserUse: true);

        var msg = harness.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = harness.Identity.WorkspacePath }
        });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();
        var threadId = response.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;

        Assert.Contains(threadId, harness.Service.RefreshedThreadAgents);
        AssertThreadBrowserUseAvailable(proxy, threadId);
    }

    [Fact]
    public async Task ThreadResume_WithBrowserUseCapability_BindsExistingThreadAndRefreshesAgent()
    {
        var proxy = new WireBrowserUseProxy();
        using var harness = new AppServerTestHarness(wireBrowserUseProxy: proxy);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        await harness.InitializeAsync(browserUse: true);

        var msg = harness.BuildRequest(AppServerMethods.ThreadResume, new { threadId = thread.Id });
        await harness.ExecuteRequestAsync(msg);

        Assert.Contains(thread.Id, harness.Service.RefreshedThreadAgents);
        AssertThreadBrowserUseAvailable(proxy, thread.Id);
    }

    private static void AssertThreadBrowserUseAvailable(WireBrowserUseProxy proxy, string threadId)
    {
        var previous = TracingChatClient.CurrentSessionKey;
        try
        {
            TracingChatClient.CurrentSessionKey = threadId;
            Assert.True(proxy.IsAvailable);
        }
        finally
        {
            TracingChatClient.CurrentSessionKey = previous;
        }
    }
}
