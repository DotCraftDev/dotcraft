using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Automations;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Local;
using DotCraft.Automations.Orchestrator;
using DotCraft.Automations.Protocol;
using DotCraft.Automations.Workspace;
using DotCraft.Hosting;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.GitHubTracker.Tests.GitHub;

/// <summary>
/// Regression tests for the orchestrator-layer readiness gate that defers bound automation tasks
/// until the target thread's origin channel runtime has completed its handshake.
/// </summary>
/// <remarks>
/// Root cause recap: <c>AutomationOrchestrator</c> would dispatch bound tasks before an external
/// channel adapter (e.g. Feishu) finished the JSON-RPC <c>initialize</c>/<c>initialized</c> handshake.
/// <c>ExternalChannelToolProvider</c> then returned an empty tool list and the agent ran without
/// channel-specific tools. The gate returns false while the runtime is missing or <see cref="IChannelRuntime.IsReady"/>
/// is false so the next poll cycle can retry cleanly.
/// </remarks>
public sealed class AutomationOrchestratorBoundChannelReadinessTests
{
    private const string BoundThreadId = "thread_20260422_ua67h1";
    private const string OriginChannel = "feishu";

    [Fact]
    public async Task IsBoundChannelReadyAsync_ReturnsTrue_WhenRegistryNotWired()
    {
        var orchestrator = CreateOrchestrator(channelRuntimeRegistry: null);
        orchestrator.SetSessionClient(CreateSessionClient(new StubSessionService(MakeThread(OriginChannel))));

        var ready = await orchestrator.IsBoundChannelReadyAsync(MakeBinding(), CancellationToken.None);

        Assert.True(ready);
    }

    [Fact]
    public async Task IsBoundChannelReadyAsync_ReturnsFalse_WhenOriginChannelRuntimeNotRegistered()
    {
        var registry = new ChannelRuntimeRegistry();
        var orchestrator = CreateOrchestrator(registry);
        orchestrator.SetSessionClient(CreateSessionClient(new StubSessionService(MakeThread(OriginChannel))));

        var ready = await orchestrator.IsBoundChannelReadyAsync(MakeBinding(), CancellationToken.None);

        Assert.False(ready);
    }

    [Fact]
    public async Task IsBoundChannelReadyAsync_ReturnsFalse_WhenRuntimeNotReady()
    {
        var registry = new ChannelRuntimeRegistry();
        registry.Register(new FakeChannelRuntime(OriginChannel, isReady: false));
        var orchestrator = CreateOrchestrator(registry);
        orchestrator.SetSessionClient(CreateSessionClient(new StubSessionService(MakeThread(OriginChannel))));

        var ready = await orchestrator.IsBoundChannelReadyAsync(MakeBinding(), CancellationToken.None);

        Assert.False(ready);
    }

    [Fact]
    public async Task IsBoundChannelReadyAsync_ReturnsTrue_WhenRuntimeReady()
    {
        var registry = new ChannelRuntimeRegistry();
        registry.Register(new FakeChannelRuntime(OriginChannel, isReady: true));
        var orchestrator = CreateOrchestrator(registry);
        orchestrator.SetSessionClient(CreateSessionClient(new StubSessionService(MakeThread(OriginChannel))));

        var ready = await orchestrator.IsBoundChannelReadyAsync(MakeBinding(), CancellationToken.None);

        Assert.True(ready);
    }

    [Fact]
    public async Task IsBoundChannelReadyAsync_ReturnsTrue_WhenThreadMissing()
    {
        // Missing thread is surfaced with a clearer "bound thread missing" failure in DispatchTaskAsync;
        // the gate must not swallow it into an infinite defer loop.
        var registry = new ChannelRuntimeRegistry();
        var orchestrator = CreateOrchestrator(registry);
        orchestrator.SetSessionClient(CreateSessionClient(new StubSessionService(threadToReturn: null)));

        var ready = await orchestrator.IsBoundChannelReadyAsync(MakeBinding(), CancellationToken.None);

        Assert.True(ready);
    }

    [Fact]
    public async Task IsBoundChannelReadyAsync_ReturnsTrue_WhenOriginChannelMissing()
    {
        var registry = new ChannelRuntimeRegistry();
        var orchestrator = CreateOrchestrator(registry);
        orchestrator.SetSessionClient(CreateSessionClient(new StubSessionService(MakeThread(originChannel: ""))));

        var ready = await orchestrator.IsBoundChannelReadyAsync(MakeBinding(), CancellationToken.None);

        Assert.True(ready);
    }

    [Fact]
    public async Task PollOnceAsync_DoesNotBeginBoundTask_WhenChannelRuntimeNotReady()
    {
        var registry = new ChannelRuntimeRegistry();
        registry.Register(new FakeChannelRuntime(OriginChannel, isReady: false));

        var source = new RecordingBoundTaskSource(
            MakeBoundTask("task-bound-1", scheduleDueNow: true));

        var orchestrator = CreateOrchestrator(registry, [source]);
        orchestrator.SetSessionClient(CreateSessionClient(new StubSessionService(MakeThread(OriginChannel))));

        await orchestrator.TriggerImmediatePollAsync(CancellationToken.None);

        Assert.Equal(1, source.PendingCalls);
        Assert.Equal(0, source.StatusChangeCalls);
    }

    [Fact]
    public async Task PollOnceAsync_DoesNotBeginBoundTask_WhenChannelRuntimeNotRegistered()
    {
        var registry = new ChannelRuntimeRegistry();

        var source = new RecordingBoundTaskSource(
            MakeBoundTask("task-bound-2", scheduleDueNow: true));

        var orchestrator = CreateOrchestrator(registry, [source]);
        orchestrator.SetSessionClient(CreateSessionClient(new StubSessionService(MakeThread(OriginChannel))));

        await orchestrator.TriggerImmediatePollAsync(CancellationToken.None);

        Assert.Equal(1, source.PendingCalls);
        Assert.Equal(0, source.StatusChangeCalls);
    }

    private static AutomationOrchestrator CreateOrchestrator(
        IChannelRuntimeRegistry? channelRuntimeRegistry,
        IEnumerable<IAutomationSource>? sources = null)
    {
        var config = new AutomationsConfig
        {
            WorkspaceRoot = Path.Combine(Path.GetTempPath(), "dotcraft-gate-tests"),
            PollingInterval = TimeSpan.FromSeconds(30),
            MaxConcurrentTasks = 1,
        };

        return new AutomationOrchestrator(
            config,
            new AutomationWorkspaceManager(config, NullLogger<AutomationWorkspaceManager>.Instance),
            new LocalWorkflowLoader(NullLogger<LocalWorkflowLoader>.Instance),
            new ToolProfileRegistry(),
            NullLogger<AutomationOrchestrator>.Instance,
            sources ?? [],
            channelRuntimeRegistry);
    }

    private static AutomationSessionClient CreateSessionClient(ISessionService sessionService)
    {
        var paths = new DotCraftPaths
        {
            WorkspacePath = Path.Combine(Path.GetTempPath(), "dotcraft-gate-tests-workspace"),
            CraftPath = Path.Combine(Path.GetTempPath(), "dotcraft-gate-tests-workspace", ".craft")
        };
        return new AutomationSessionClient(sessionService, paths);
    }

    private static SessionThread MakeThread(string originChannel) => new()
    {
        Id = BoundThreadId,
        WorkspacePath = Path.GetTempPath(),
        OriginChannel = originChannel,
    };

    private static AutomationThreadBinding MakeBinding() => new()
    {
        ThreadId = BoundThreadId,
    };

    private static LocalAutomationTask MakeBoundTask(string id, bool scheduleDueNow)
    {
        var task = new LocalAutomationTask
        {
            TaskDirectory = Path.Combine(Path.GetTempPath(), id),
            Id = id,
            Title = id,
            Status = AutomationTaskStatus.Pending,
            SourceName = "local",
            Description = "test",
            ThreadBinding = MakeBinding(),
        };

        if (scheduleDueNow)
        {
            task.Schedule = new DotCraft.Cron.CronSchedule { Kind = "every", EveryMs = 60_000 };
            task.NextRunAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        }

        return task;
    }

    private sealed class FakeChannelRuntime(string name, bool isReady) : IChannelRuntime
    {
        public string Name { get; } = name;
        public bool IsReady { get; } = isReady;

        public Task<ExtChannelSendResult> DeliverAsync(
            string target,
            ChannelOutboundMessage message,
            object? metadata = null,
            CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class RecordingBoundTaskSource(LocalAutomationTask task) : IAutomationSource
    {
        private readonly LocalAutomationTask _task = task;

        public string Name => "local";
        public string ToolProfileName => "default";
        public int PendingCalls { get; private set; }
        public int StatusChangeCalls { get; private set; }

        public void RegisterToolProfile(IToolProfileRegistry registry) { }

        public Task<IReadOnlyList<AutomationTask>> GetPendingTasksAsync(CancellationToken ct)
        {
            PendingCalls++;
            return Task.FromResult<IReadOnlyList<AutomationTask>>([_task]);
        }

        public Task<IReadOnlyList<AutomationTask>> GetAllTasksAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AutomationTask>>([_task]);

        public Task ReconcileExpiredResourcesAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<AutomationWorkflowDefinition> GetWorkflowAsync(AutomationTask task, CancellationToken ct) =>
            Task.FromException<AutomationWorkflowDefinition>(new NotSupportedException(
                "Gate should have deferred this task before dispatch tried to load its workflow."));

        public Task OnStatusChangedAsync(AutomationTask task, AutomationTaskStatus newStatus, CancellationToken ct)
        {
            StatusChangeCalls++;
            return Task.CompletedTask;
        }

        public Task OnAgentCompletedAsync(AutomationTask task, string agentSummary, CancellationToken ct) =>
            Task.CompletedTask;

        public Task ApproveTaskAsync(string taskId, CancellationToken ct) => Task.CompletedTask;

        public Task RejectTaskAsync(string taskId, string? reason, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubSessionService(SessionThread? threadToReturn) : ISessionService
    {
        private readonly SessionThread? _threadToReturn = threadToReturn;

        public Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }
        public Action<string>? ThreadDeletedForBroadcast { get; set; }
        public Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }
        public Action<string, SessionThreadRuntimeSignal>? ThreadRuntimeSignalForBroadcast { get; set; }

        public Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default)
            => _threadToReturn != null
                ? Task.FromResult(_threadToReturn)
                : Task.FromException<SessionThread>(new KeyNotFoundException(threadId));

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
        public Task<SessionThread> RollbackThreadAsync(string threadId, int numTurns, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<QueuedTurnInput> EnqueueTurnInputAsync(string threadId, IList<AIContent> content, SenderContext? sender = null, CancellationToken ct = default, SessionInputSnapshot? inputSnapshot = null) => throw new NotImplementedException();
        public Task<IReadOnlyList<QueuedTurnInput>> RemoveQueuedTurnInputAsync(string threadId, string queuedInputId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> SteerTurnAsync(string threadId, string expectedTurnId, IList<AIContent> content, SenderContext? sender = null, CancellationToken ct = default, SessionInputSnapshot? inputSnapshot = null) => throw new NotImplementedException();
        public Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateThreadConfigurationAsync(string threadId, ThreadConfiguration config, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default) => throw new NotImplementedException();
        public ContextUsageSnapshot? TryGetContextUsageSnapshot(string threadId) => null;
    }
}
