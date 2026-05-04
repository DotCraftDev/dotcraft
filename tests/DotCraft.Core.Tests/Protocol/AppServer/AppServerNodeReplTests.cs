using DotCraft.Protocol.AppServer;
using DotCraft.Tracing;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerNodeReplTests
{
    [Fact]
    public async Task ThreadStart_WithNodeReplAndBrowserUseCapabilities_BindsThreadAndRefreshesAgent()
    {
        var proxy = new WireNodeReplProxy();
        using var harness = new AppServerTestHarness(wireNodeReplProxy: proxy);
        await harness.InitializeAsync(nodeReplBrowserUse: true);

        var msg = harness.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = harness.Identity.WorkspacePath }
        });
        await harness.ExecuteRequestAsync(msg);

        var response = await harness.Transport.ReadNextSentAsync();
        var threadId = response.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()!;

        Assert.Contains(threadId, harness.Service.RefreshedThreadAgents);
        AssertThreadNodeReplAvailable(proxy, threadId);
    }

    [Fact]
    public async Task ThreadResume_WithNodeReplAndBrowserUseCapabilities_BindsExistingThreadAndRefreshesAgent()
    {
        var proxy = new WireNodeReplProxy();
        using var harness = new AppServerTestHarness(wireNodeReplProxy: proxy);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        await harness.InitializeAsync(nodeReplBrowserUse: true);

        var msg = harness.BuildRequest(AppServerMethods.ThreadResume, new { threadId = thread.Id });
        await harness.ExecuteRequestAsync(msg);

        Assert.Contains(thread.Id, harness.Service.RefreshedThreadAgents);
        AssertThreadNodeReplAvailable(proxy, thread.Id);
    }

    private static void AssertThreadNodeReplAvailable(WireNodeReplProxy proxy, string threadId)
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
