using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

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
    public async Task ThreadStart_OmitsWorkspacePath_NormalizesToHostWorkspace()
    {
        var msg = _h.BuildRequest(AppServerMethods.ThreadStart, new
        {
            identity = new { channelName = "appserver", userId = "test_user_no_ws" }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var thread = response.RootElement.GetProperty("result").GetProperty("thread");
        Assert.Equal(_h.Identity.WorkspacePath, thread.GetProperty("workspacePath").GetString());
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

    /// <summary>
    /// When <see cref="AppServerRequestContext.CurrentTransport"/> matches the client transport,
    /// a broadcast hook must skip that transport (mirrors <c>AppServerHost.BroadcastThreadStarted</c>)
    /// so the initiator does not receive a duplicate <c>thread/started</c> from broadcast + handler.
    /// </summary>
    [Fact]
    public async Task ThreadStart_BroadcastHookSkipsCurrentTransportWhenContextSet()
    {
        var previous = AppServerRequestContext.CurrentTransport;
        AppServerRequestContext.CurrentTransport = _h.Transport;
        try
        {
            _h.Service.ThreadCreatedForBroadcast = thread =>
            {
                var skip = AppServerRequestContext.CurrentTransport;
                if (skip != null && ReferenceEquals(_h.Transport, skip))
                    return;
                _h.Transport.WriteMessageAsync(new
                {
                    jsonrpc = "2.0",
                    method = AppServerMethods.ThreadStarted,
                    @params = new { thread = thread.ToWire() }
                }, default).GetAwaiter().GetResult();
            };

            var msg = _h.BuildRequest(AppServerMethods.ThreadStart, new
            {
                identity = new { channelName = "appserver", userId = "test_user", workspacePath = _h.Identity.WorkspacePath }
            });
            await _h.ExecuteRequestAsync(msg);

            await _h.Transport.ReadNextSentAsync();
            await _h.Transport.ReadNextSentAsync();
            Assert.Null(_h.Transport.TryReadSent());
        }
        finally
        {
            _h.Service.ThreadCreatedForBroadcast = null;
            AppServerRequestContext.CurrentTransport = previous;
        }
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

    [Fact]
    public async Task ThreadPause_NotificationIncludesPreviousStatus()
    {
        // Gap B: previousStatus must be present in thread/statusChanged notification
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        Assert.Equal(ThreadStatus.Active, thread.Status);

        var msg = _h.BuildRequest(AppServerMethods.ThreadPause, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        await _h.Transport.ReadNextSentAsync(); // response
        var notification = await _h.Transport.ReadNextSentAsync();

        var @params = notification.RootElement.GetProperty("params");
        Assert.Equal("active", @params.GetProperty("previousStatus").GetString());
        Assert.Equal("paused", @params.GetProperty("newStatus").GetString());
    }

    [Fact]
    public async Task ThreadPause_WhenSubscribed_SendsOnlyResponse_NoDuplicateNotification()
    {
        // Gap C: if the connection has an active subscription to the thread, the handler
        // must not send an inline notification (the broker/dispatcher path handles it).
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        // Subscribe to the thread first
        var subscribeMsg = _h.BuildRequest(AppServerMethods.ThreadSubscribe, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(subscribeMsg);
        await _h.Transport.ReadNextSentAsync(); // drain subscribe response

        // Now pause — should produce exactly one message (the response), not two
        var pauseMsg = _h.BuildRequest(AppServerMethods.ThreadPause, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(pauseMsg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        // Verify no additional message was sent (no duplicate notification)
        await Task.Delay(20); // small delay to let any fire-and-forget tasks settle
        var extra = _h.Transport.TryReadSent();
        Assert.Null(extra);
    }

    [Fact]
    public async Task ThreadPause_AlreadyPaused_SendsOnlyResponse_NoNotification()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        await _h.Service.PauseThreadAsync(thread.Id); // pre-pause

        var msg = _h.BuildRequest(AppServerMethods.ThreadPause, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        await Task.Delay(20);
        Assert.Null(_h.Transport.TryReadSent());
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

    [Fact]
    public async Task ThreadArchive_NotificationIncludesPreviousStatus()
    {
        // Gap B: previousStatus must be present in thread/statusChanged notification
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadArchive, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        await _h.Transport.ReadNextSentAsync(); // response
        var notification = await _h.Transport.ReadNextSentAsync();

        var @params = notification.RootElement.GetProperty("params");
        Assert.Equal("active", @params.GetProperty("previousStatus").GetString());
        Assert.Equal("archived", @params.GetProperty("newStatus").GetString());
    }

    [Fact]
    public async Task ThreadArchive_WhenSubscribed_SendsOnlyResponse_NoDuplicateNotification()
    {
        // Gap C: subscribed connection should not receive a duplicate statusChanged
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var subscribeMsg = _h.BuildRequest(AppServerMethods.ThreadSubscribe, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(subscribeMsg);
        await _h.Transport.ReadNextSentAsync(); // drain subscribe response

        var archiveMsg = _h.BuildRequest(AppServerMethods.ThreadArchive, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(archiveMsg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        await Task.Delay(20);
        Assert.Null(_h.Transport.TryReadSent());
    }

    // -------------------------------------------------------------------------
    // thread/resume — resumedBy (Gap D)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ThreadResume_NotificationIncludesClientNameAsResumedBy()
    {
        // Gap D: resumedBy must use the client's declared name from initialize, not hardcoded "appserver"
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.ThreadResume, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(msg);

        await _h.Transport.ReadNextSentAsync(); // response
        var notification = await _h.Transport.ReadNextSentAsync();

        AppServerTestHarness.AssertIsNotification(notification, AppServerMethods.ThreadResumed);
        // The harness initializes with clientInfo.name = "test-client"
        Assert.Equal("test-client",
            notification.RootElement.GetProperty("params").GetProperty("resumedBy").GetString());
    }

    [Fact]
    public async Task ThreadResume_WhenSubscribed_SendsOnlyResponse_NoDuplicateNotification()
    {
        // Gap C: subscribed connection should not receive a duplicate thread/resumed notification
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var subscribeMsg = _h.BuildRequest(AppServerMethods.ThreadSubscribe, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(subscribeMsg);
        await _h.Transport.ReadNextSentAsync(); // drain subscribe response

        var resumeMsg = _h.BuildRequest(AppServerMethods.ThreadResume, new { threadId = thread.Id });
        await _h.ExecuteRequestAsync(resumeMsg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        await Task.Delay(20);
        Assert.Null(_h.Transport.TryReadSent());
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
