using System.Runtime.CompilerServices;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerEventDispatcherDisconnectTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public async Task RunAsync_WhenNotificationWriteFails_DrainsRemainingTurnEvents(int failOnWriteAttempt)
    {
        using var harness = new AppServerTestHarness();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var events = AppServerTestHarness.BuildTurnEventSequence(thread.Id);
        var consumed = new List<SessionEventType>();
        var transport = new FailingTransport(failOnWriteAttempt: failOnWriteAttempt);

        var dispatcher = new AppServerEventDispatcher(
            TrackEvents(events, consumed),
            CreateReadyConnection(),
            transport,
            harness.Service);

        await dispatcher.RunAsync();

        Assert.Equal(events.Select(e => e.EventType), consumed);
        Assert.Equal(failOnWriteAttempt, transport.WriteAttempts);
    }

    [Fact]
    public async Task RunAsync_WhenTurnStartedResponseCallbackFails_DrainsRemainingTurnEvents()
    {
        using var harness = new AppServerTestHarness();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var events = AppServerTestHarness.BuildTurnEventSequence(thread.Id);
        var consumed = new List<SessionEventType>();
        var callbackCalled = false;
        var transport = new FailingTransport();

        var dispatcher = new AppServerEventDispatcher(
            TrackEvents(events, consumed),
            CreateReadyConnection(),
            transport,
            harness.Service,
            onTurnStarted: _ =>
            {
                callbackCalled = true;
                throw new IOException("client disconnected");
            });

        await dispatcher.RunAsync();

        Assert.True(callbackCalled);
        Assert.Equal(events.Select(e => e.EventType), consumed);
        Assert.Equal(0, transport.WriteAttempts);
    }

    [Fact]
    public async Task RunAsync_WhenApprovalRequestTransportFails_ResolvesWithNonInteractiveFallback()
    {
        using var harness = new AppServerTestHarness(
            defaultApprovalDecision: SessionApprovalDecision.Reject);
        var thread = await harness.Service.CreateThreadAsync(
            harness.Identity,
            new ThreadConfiguration { ApprovalPolicy = ApprovalPolicy.AutoApprove });
        var events = AppServerTestHarness.BuildApprovalEventSequence(thread.Id);
        var consumed = new List<SessionEventType>();
        var transport = new FailingTransport(clientRequestException: new IOException("client disconnected"));

        var dispatcher = new AppServerEventDispatcher(
            TrackEvents(events, consumed),
            CreateReadyConnection(),
            transport,
            harness.Service,
            defaultApprovalDecision: SessionApprovalDecision.Reject);

        await dispatcher.RunAsync();

        Assert.Equal(events.Select(e => e.EventType), consumed);
        var resolved = Assert.Single(harness.Service.ResolvedApprovals);
        Assert.Equal("req_001", resolved.requestId);
        Assert.Equal(SessionApprovalDecision.AcceptOnce, resolved.decision);
    }

    [Fact]
    public async Task TurnStart_UsesNonConnectionCancellationTokenForPersistedTurnExecution()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var events = AppServerTestHarness.BuildTurnEventSequence(thread.Id);
        harness.Service.EnqueueSubmitEvents(thread.Id, events);

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "keep running" } }
        }));

        Assert.False(harness.Service.LastSubmitCancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task TurnStart_WhenSubscribedAndConnectionCloses_DrainsActiveTurnStream()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        var events = AppServerTestHarness.BuildTurnEventSequence(thread.Id);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.ThreadSubscribe,
            new { threadId = thread.Id }));
        await harness.Transport.ReadNextSentAsync();

        harness.Service.EnqueueSubmitEvents(thread.Id, events);
        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "subscribed turn" } }
        }));

        harness.Connection.MarkClosed();
        harness.Connection.CancelAllSubscriptions();

        await WaitForAsync(() => harness.Service.YieldedSubmitEventTypes.Count >= events.Length);
        Assert.Equal(events.Select(e => e.EventType), harness.Service.YieldedSubmitEventTypes);
    }

    [Fact]
    public async Task TurnStart_WhenSubscribedAndConnectionClosesDuringApproval_UsesFallbackDecision()
    {
        using var harness = new AppServerTestHarness(
            defaultApprovalDecision: SessionApprovalDecision.Reject);
        await harness.InitializeAsync();
        var thread = await harness.Service.CreateThreadAsync(
            harness.Identity,
            new ThreadConfiguration { ApprovalPolicy = ApprovalPolicy.AutoApprove });
        var events = AppServerTestHarness.BuildApprovalEventSequence(thread.Id);

        await harness.ExecuteRequestAsync(harness.BuildRequest(
            AppServerMethods.ThreadSubscribe,
            new { threadId = thread.Id }));
        await harness.Transport.ReadNextSentAsync();

        harness.Service.EnqueueSubmitEvents(thread.Id, events);
        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "approval while subscribed" } }
        }));

        harness.Connection.MarkClosed();
        harness.Connection.CancelAllSubscriptions();

        await WaitForAsync(() => harness.Service.ResolvedApprovals.Count == 1);
        var resolved = Assert.Single(harness.Service.ResolvedApprovals);
        Assert.Equal("req_001", resolved.requestId);
        Assert.Equal(SessionApprovalDecision.AcceptOnce, resolved.decision);
    }

    [Fact]
    public void CancelAllSubscriptions_CancelsPassiveSubscriptionsOnDisconnect()
    {
        var connection = new AppServerConnection();
        var cts = new CancellationTokenSource();
        var cancelled = false;
        using var _ = cts.Token.Register(() => cancelled = true);

        Assert.True(connection.TryAddSubscription("thread_001", cts));

        connection.CancelAllSubscriptions();

        Assert.True(cancelled);
        Assert.False(connection.HasSubscription("thread_001"));
    }

    [Fact]
    public async Task MarkClosed_CompletesConnectionClosedTask()
    {
        var connection = new AppServerConnection();

        connection.MarkClosed();

        await connection.Closed.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(connection.IsClosed);
    }

    private static AppServerConnection CreateReadyConnection()
    {
        var connection = new AppServerConnection();
        Assert.True(connection.TryMarkInitialized(
            new AppServerClientInfo { Name = "desktop", Version = "0.0.1-test" },
            new AppServerClientCapabilities { ApprovalSupport = true, StreamingSupport = true }));
        connection.MarkClientReady();
        return connection;
    }

    private static async IAsyncEnumerable<SessionEvent> TrackEvents(
        IEnumerable<SessionEvent> events,
        List<SessionEventType> consumed,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();
            consumed.Add(evt.EventType);
            await Task.Yield();
            yield return evt;
        }
    }

    private sealed class FailingTransport(
        int? failOnWriteAttempt = null,
        Exception? clientRequestException = null) : IAppServerTransport
    {
        public int WriteAttempts { get; private set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default) =>
            Task.FromResult<AppServerIncomingMessage?>(null);

        public Task WriteMessageAsync(object message, CancellationToken ct = default)
        {
            WriteAttempts++;
            if (failOnWriteAttempt == WriteAttempts)
                throw new IOException("client disconnected");

            return Task.CompletedTask;
        }

        public Task<AppServerIncomingMessage> SendClientRequestAsync(
            string method,
            object? @params,
            CancellationToken ct = default,
            TimeSpan? timeout = null)
        {
            if (clientRequestException != null)
                throw clientRequestException;

            return Task.FromResult(InMemoryTransport.BuildClientResponse(1, new { decision = "accept" }));
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(10, cts.Token);
        }
    }
}
