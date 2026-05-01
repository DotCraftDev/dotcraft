using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceMemoryConsolidationTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceMemoryConsolidationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MemoryConsolidation_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task SubmitInputAsync_WhenConsolidationIsSkipped_EmitsSkippedWithoutConsolidated()
    {
        var consolidator = new FakeMemoryConsolidator(MemoryConsolidationResult.Skipped("save_memory_not_called"));
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient, consolidator);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        var subscription = CollectThreadEventsAsync(
            svc,
            thread.Id,
            events => events.Any(IsConsolidationTerminal));

        var turnEvents = await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("remember blue")]));
        var threadEvents = await subscription;

        Assert.Contains(turnEvents, e => IsSystemEvent(e, "consolidating"));
        Assert.Contains(threadEvents, e => IsSystemEvent(e, "consolidationSkipped"));
        Assert.DoesNotContain(threadEvents, e => IsSystemEvent(e, "consolidated"));
    }

    [Fact]
    public async Task SubmitInputAsync_WhenConsolidationSucceeds_EmitsConsolidatedAndPersistentNotice()
    {
        var startSawPersistedTurn = false;
        var threadStore = new ThreadStore(_tempDir);
        string? completedThreadId = null;
        var consolidator = new FakeMemoryConsolidator(
            MemoryConsolidationResult.Succeeded(memoryWritten: true, historyWritten: true),
            async () =>
            {
                var persisted = completedThreadId == null
                    ? null
                    : await threadStore.LoadThreadAsync(completedThreadId);
                startSawPersistedTurn = persisted?.Turns.SingleOrDefault()?.Status == TurnStatus.Completed;
            });
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient, consolidator);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());
        completedThreadId = thread.Id;

        var subscription = CollectThreadEventsAsync(
            svc,
            thread.Id,
            events => events.Any(e => IsSystemEvent(e, "consolidated"))
                && events.Any(e => IsMemoryNotice(e)));

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("remember blue")]));
        var threadEvents = await subscription;

        Assert.Contains(threadEvents, e => IsSystemEvent(e, "consolidated"));
        Assert.Contains(threadEvents, IsMemoryNotice);
        Assert.True(startSawPersistedTurn);

        var reloaded = await svc.GetThreadAsync(thread.Id);
        var notice = Assert.Single(reloaded.Turns.Single().Items, item => item.Type == ItemType.SystemNotice);
        Assert.Equal("memoryConsolidated", notice.AsSystemNotice?.Kind);
    }

    [Fact]
    public async Task SubmitInputAsync_WhenConsolidationFails_EmitsFailedWithoutNotice()
    {
        var consolidator = new FakeMemoryConsolidator(MemoryConsolidationResult.Failed("provider unavailable"));
        var chatClient = new StaticChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient, consolidator);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        var subscription = CollectThreadEventsAsync(
            svc,
            thread.Id,
            events => events.Any(IsConsolidationTerminal));

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("remember blue")]));
        var threadEvents = await subscription;

        Assert.Contains(threadEvents, e => IsSystemEvent(e, "consolidationFailed"));
        Assert.DoesNotContain(threadEvents, IsMemoryNotice);
    }

    private SessionService CreateService(AgentFactory agentFactory, IChatClient chatClient)
    {
        var defaultAgent = chatClient.AsAIAgent(new ChatClientAgentOptions());
        return new SessionService(
            agentFactory,
            defaultAgent,
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate());
    }

    private AgentFactory CreateAgentFactory(IChatClient chatClient, IMemoryConsolidator consolidator)
    {
        var config = new AppConfig
        {
            ApiKey = "sk-test-not-used-for-network",
            EndPoint = "https://127.0.0.1:9/v1",
            Memory = { ConsolidateEveryNTurns = 1 }
        };
        var memory = new MemoryStore(_tempDir);
        var skills = new SkillsLoader(_tempDir);
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: memory,
            skillsLoader: skills,
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            toolProviders: Array.Empty<IAgentToolProvider>(),
            memoryConsolidator: consolidator);
    }

    private SessionIdentity MakeIdentity() => new()
    {
        ChannelName = "test",
        UserId = "u",
        WorkspacePath = _tempDir
    };

    private static async Task<List<SessionEvent>> DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        var collected = new List<SessionEvent>();
        await foreach (var evt in events)
            collected.Add(evt);
        return collected;
    }

    private static async Task<List<SessionEvent>> CollectThreadEventsAsync(
        ISessionService svc,
        string threadId,
        Func<List<SessionEvent>, bool> done)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var collected = new List<SessionEvent>();
        await foreach (var evt in svc.SubscribeThreadAsync(threadId, replayRecent: true, cts.Token))
        {
            collected.Add(evt);
            if (done(collected))
                break;
        }

        return collected;
    }

    private static bool IsConsolidationTerminal(SessionEvent evt) =>
        IsSystemEvent(evt, "consolidated")
        || IsSystemEvent(evt, "consolidationSkipped")
        || IsSystemEvent(evt, "consolidationFailed");

    private static bool IsSystemEvent(SessionEvent evt, string kind) =>
        evt.EventType == SessionEventType.SystemEvent
        && evt.Payload is SystemEventPayload payload
        && payload.Kind == kind;

    private static bool IsMemoryNotice(SessionEvent evt) =>
        evt.EventType == SessionEventType.ItemCompleted
        && evt.Payload is SessionItem { Payload: SystemNoticePayload { Kind: "memoryConsolidated" } };

    private sealed class FakeMemoryConsolidator(
        MemoryConsolidationResult result,
        Func<Task>? onStart = null) : IMemoryConsolidator
    {
        public async Task<MemoryConsolidationResult> ConsolidateAsync(
            IReadOnlyList<ChatMessage> messagesToArchive,
            CancellationToken cancellationToken = default)
        {
            if (onStart != null)
                await onStart();
            return result;
        }
    }

    private sealed class StaticChatClient(string responseText) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent(responseText)])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(responseText)]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
