using DotCraft.AppServer;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.AppServer;

public sealed class HubTurnNotificationPolicyTests
{
    [Theory]
    [InlineData(SessionThreadRuntimeSignal.TurnCompleted, "turnCompleted", "success")]
    [InlineData(SessionThreadRuntimeSignal.TurnFailed, "turnFailed", "error")]
    public void GetSpec_ForNotifiableSignals_ReturnsHubNotificationSpec(
        SessionThreadRuntimeSignal signal,
        string kind,
        string severity)
    {
        var spec = HubTurnNotificationPolicy.GetSpec(signal);

        Assert.NotNull(spec);
        Assert.Equal(kind, spec.Kind);
        Assert.Equal(severity, spec.Severity);
    }

    [Theory]
    [InlineData(SessionThreadRuntimeSignal.TurnStarted)]
    [InlineData(SessionThreadRuntimeSignal.TurnCompletedAwaitingPlanConfirmation)]
    [InlineData(SessionThreadRuntimeSignal.TurnCancelled)]
    [InlineData(SessionThreadRuntimeSignal.ApprovalRequested)]
    [InlineData(SessionThreadRuntimeSignal.ApprovalResolved)]
    [InlineData(SessionThreadRuntimeSignal.ContextCompacted)]
    public void GetSpec_ForNonHubNotificationSignals_ReturnsNull(SessionThreadRuntimeSignal signal)
    {
        Assert.Null(HubTurnNotificationPolicy.GetSpec(signal));
    }

    [Fact]
    public async Task ResolveDecision_ForNormalThread_ReturnsDisplayName()
    {
        var service = new FakeSessionService(new SessionThread
        {
            Id = "thread_user",
            OriginChannel = "dotcraft-desktop",
            DisplayName = "  User task  "
        });

        var decision = await HubTurnNotificationPolicy.ResolveDecisionAsync(service, "thread_user");

        Assert.True(decision.ShouldNotify);
        Assert.Equal("User task", decision.DisplayName);
    }

    [Fact]
    public async Task ResolveDecision_ForInternalMetadataThread_SuppressesNotification()
    {
        var thread = new SessionThread
        {
            Id = "thread_internal",
            OriginChannel = "dotcraft-desktop",
            DisplayName = "[internal] Future helper"
        };
        thread.Metadata[ThreadVisibility.InternalMetadataKey] = "future-helper";
        var service = new FakeSessionService(thread);

        var decision = await HubTurnNotificationPolicy.ResolveDecisionAsync(service, "thread_internal");

        Assert.False(decision.ShouldNotify);
    }

    [Fact]
    public async Task ResolveDecision_ForKnownInternalOriginThread_SuppressesNotification()
    {
        var service = new FakeSessionService(new SessionThread
        {
            Id = "thread_welcome",
            OriginChannel = WelcomeSuggestionConstants.ChannelName,
            DisplayName = "[internal] Welcome suggestions"
        });

        var decision = await HubTurnNotificationPolicy.ResolveDecisionAsync(service, "thread_welcome");

        Assert.False(decision.ShouldNotify);
    }

    [Fact]
    public async Task ResolveDecision_WhenThreadLoadFails_FailsOpenWithDefaultDisplayName()
    {
        var service = new FakeSessionService(null, throwOnGet: true);

        var decision = await HubTurnNotificationPolicy.ResolveDecisionAsync(service, "missing_thread");

        Assert.True(decision.ShouldNotify);
        Assert.False(string.IsNullOrWhiteSpace(decision.DisplayName));
    }

    private sealed class FakeSessionService(SessionThread? thread, bool throwOnGet = false) : ISessionService
    {
        public Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }
        public Action<string>? ThreadDeletedForBroadcast { get; set; }
        public Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }
        public Action<string, SessionThreadRuntimeSignal>? ThreadRuntimeSignalForBroadcast { get; set; }

        public Task<SessionThread> CreateThreadAsync(SessionIdentity identity, ThreadConfiguration? config = null, HistoryMode historyMode = HistoryMode.Server, string? threadId = null, string? displayName = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ThreadResetResult> ResetConversationAsync(SessionIdentity identity, ThreadConfiguration? config = null, HistoryMode historyMode = HistoryMode.Server, string? displayName = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task PauseThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ArchiveThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UnarchiveThreadAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(SessionIdentity identity, bool includeArchived = false, IReadOnlyList<string>? crossChannelOrigins = null, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(string threadId, bool replayRecent = false, CancellationToken ct = default) => throw new NotImplementedException();
        public IAsyncEnumerable<SessionEvent> SubmitInputAsync(string threadId, IList<AIContent> content, SenderContext? sender = null, ChatMessage[]? messages = null, CancellationToken ct = default, SessionInputSnapshot? inputSnapshot = null) => throw new NotImplementedException();
        public Task ResolveApprovalAsync(string threadId, string turnId, string requestId, SessionApprovalDecision decision, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CancelTurnAsync(string threadId, string turnId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task CleanBackgroundTerminalsAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SessionThread> RollbackThreadAsync(string threadId, int numTurns, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<QueuedTurnInput> EnqueueTurnInputAsync(string threadId, IList<AIContent> content, SenderContext? sender = null, CancellationToken ct = default, SessionInputSnapshot? inputSnapshot = null) => throw new NotImplementedException();
        public Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(string threadId, string queuedInputId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<TurnSteerResult> SteerTurnAsync(string threadId, string expectedTurnId, string queuedInputId, CancellationToken ct = default, SenderContext? sender = null) => throw new NotImplementedException();
        public Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateThreadConfigurationAsync(string threadId, ThreadConfiguration config, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default) => throw new NotImplementedException();
        public ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId) => null;

        public Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default)
        {
            _ = threadId;
            _ = ct;
            if (throwOnGet || thread == null)
                throw new KeyNotFoundException("Thread not found.");
            return Task.FromResult(thread);
        }

        public Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default) =>
            GetThreadAsync(threadId, ct);
    }
}
