using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol;

/// <summary>
/// Verifies that <see cref="SessionService"/> emits runtime broadcast signals for turn lifecycle transitions.
/// </summary>
public sealed class SessionServiceRuntimeSignalTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceRuntimeSignalTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RuntimeSignal_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task SubmitInputAsync_EmitsTurnStartedThenCompleted()
    {
        IChatClient chatClient = new FakeChatClient([new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")])]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());
        var seen = new List<SessionThreadRuntimeSignal>();
        svc.ThreadRuntimeSignalForBroadcast = (threadId, signal) =>
        {
            if (threadId == thread.Id)
                seen.Add(signal);
        };

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.Equal(
            [SessionThreadRuntimeSignal.TurnStarted, SessionThreadRuntimeSignal.TurnCompleted],
            seen);
    }

    [Fact]
    public async Task SubmitInputAsync_WhenAgentThrows_EmitsTurnStartedThenFailed()
    {
        IChatClient chatClient = new ThrowingChatClient(new InvalidOperationException("boom"));
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());
        var seen = new List<SessionThreadRuntimeSignal>();
        svc.ThreadRuntimeSignalForBroadcast = (threadId, signal) =>
        {
            if (threadId == thread.Id)
                seen.Add(signal);
        };

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        Assert.Equal(
            [SessionThreadRuntimeSignal.TurnStarted, SessionThreadRuntimeSignal.TurnFailed],
            seen);
    }

    [Fact]
    public async Task CancelTurnAsync_EmitsTurnStartedThenCancelled()
    {
        IChatClient chatClient = new BlockingChatClient();
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());
        var seen = new List<SessionThreadRuntimeSignal>();
        svc.ThreadRuntimeSignalForBroadcast = (threadId, signal) =>
        {
            if (threadId == thread.Id)
                seen.Add(signal);
        };

        var drainTask = DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));
        await Task.Delay(50);
        await svc.CancelTurnAsync(thread.Id, "turn_001");
        await drainTask;

        Assert.Equal(
            [SessionThreadRuntimeSignal.TurnStarted, SessionThreadRuntimeSignal.TurnCancelled],
            seen);
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

    private AgentFactory CreateAgentFactory(IChatClient chatClientFactory)
    {
        var config = new AppConfig
        {
            ApiKey = "sk-test-not-used-for-network",
            EndPoint = "https://127.0.0.1:9/v1"
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
            toolProviders: Array.Empty<IAgentToolProvider>());
    }

    private SessionIdentity MakeIdentity() => new()
    {
        ChannelName = "test",
        UserId = "u",
        WorkspacePath = _tempDir
    };

    private static async Task DrainAsync(IAsyncEnumerable<SessionEvent> events)
    {
        await foreach (var _ in events)
        {
        }
    }

    private sealed class FakeChatClient(ChatResponseUpdate[] streamUpdates) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in streamUpdates)
                yield return update;
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingChatClient(Exception exception) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw exception;

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (Environment.TickCount < 0)
                yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(string.Empty)]);
            await Task.Yield();
            throw exception;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class BlockingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
