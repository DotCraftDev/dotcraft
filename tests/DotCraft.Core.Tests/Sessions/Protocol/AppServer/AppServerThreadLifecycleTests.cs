using DotCraft.Sessions.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Tests for thread/* methods (spec Section 4).
/// Verifies response shapes and the post-response notifications emitted
/// after thread/start (→ thread/started), thread/resume (→ thread/resumed),
/// thread/pause and thread/archive (→ thread/statusChanged).
/// </summary>
public sealed class AppServerThreadLifecycleTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public AppServerThreadLifecycleTests()
    {
        // All thread tests need a ready connection
        _h.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _h.Dispose();

    // -------------------------------------------------------------------------
    // thread/start (spec Section 4.1)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadStart_ReturnsThreadInResult()
    {
        var msg = _h.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath }
        });
        await _h.ExecuteRequestAsync(msg);

        // thread/start sends response inline; read it from transport
        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var thread = response.RootElement.GetProperty("result").GetProperty("thread");
        Assert.StartsWith("thread_", thread.GetProperty("id").GetString()!);
        Assert.Equal("active", thread.GetProperty("status").GetString());
    }

    [Fact]
    public async Task ThreadStart_EmitsThreadStartedNotification()
    {
        var msg = _h.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();  // response
        var notification = await _h.Transport.ReadNextSentAsync(); // notification

        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadStarted);
        Assert.StartsWith("thread_", notification.RootElement
            .GetProperty("params").GetProperty("thread")
            .GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task ThreadStart_ResponseBeforeNotification_Ordering()
    {
        var msg = _h.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath }
        });
        await _h.ExecuteRequestAsync(msg);

        var first = await _h.Transport.ReadNextSentAsync();
        var second = await _h.Transport.ReadNextSentAsync();

        // First message must be the response (has 'result'), second must be the notification (has 'method')
        Assert.True(first.RootElement.TryGetProperty("result", out _),
            "Response (with 'result') must arrive before the notification");
        Assert.True(second.RootElement.TryGetProperty("method", out _),
            "Notification (with 'method') must arrive after the response");
    }

    [Fact]
    public async Task ThreadStart_WithHistoryModeClient_ThreadHasClientHistoryMode()
    {
        var msg = _h.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath },
            historyMode = "client"
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        await _h.Transport.ReadNextSentAsync(); // drain notification
        var thread = response.RootElement.GetProperty("result").GetProperty("thread");
        Assert.Equal("client", thread.GetProperty("historyMode").GetString());
    }

    // -------------------------------------------------------------------------
    // thread/resume (spec Section 4.2)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadResume_ReturnsThread_EmitsResumedNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadResume, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.Equal(thread.Id, response.RootElement
            .GetProperty("result").GetProperty("thread").GetProperty("id").GetString());

        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadResumed);
    }

    // -------------------------------------------------------------------------
    // thread/pause (spec Section 4)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadPause_EmitsStatusChangedNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadPause, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadStatusChanged);
        Assert.Equal("paused",
            notification.RootElement.GetProperty("params").GetProperty("newStatus").GetString());
    }

    // -------------------------------------------------------------------------
    // thread/archive
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadArchive_EmitsStatusChangedNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadArchive, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        var notification = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsSuccessResponse(response);
        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadStatusChanged);
        Assert.Equal("archived",
            notification.RootElement.GetProperty("params").GetProperty("newStatus").GetString());
    }

    // -------------------------------------------------------------------------
    // thread/list
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadList_ReturnsCreatedThreads()
    {
        await _h.Service.CreateThreadAsync(_h.Identity);
        await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadList, new
        {
            identity = new
            {
                channelName = _h.Identity.ChannelName,
                userId = _h.Identity.UserId,
                workspacePath = _h.Identity.WorkspacePath
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        var data = doc.RootElement.GetProperty("result").GetProperty("data");
        Assert.Equal(2, data.GetArrayLength());
    }

    [Fact]
    public async Task ThreadList_EmptyWorkspace_ReturnsEmpty()
    {
        var msg = _h.BuildRequest(AppServerMethods.ThreadList, new
        {
            identity = new
            {
                channelName = "appserver",
                userId = "nobody",
                workspacePath = _h.Identity.WorkspacePath
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        Assert.Equal(0, doc.RootElement.GetProperty("result").GetProperty("data").GetArrayLength());
    }

    // -------------------------------------------------------------------------
    // thread/read
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadRead_ReturnsThreadById()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadRead, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
        Assert.Equal(thread.Id,
            doc.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString());
    }

    // -------------------------------------------------------------------------
    // thread/delete
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadDelete_RemovesThread()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadDelete, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);
    }
}
