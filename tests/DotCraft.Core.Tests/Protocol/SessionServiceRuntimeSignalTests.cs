using System.Text.Json;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;
using DotCraft.Tools;
using DotCraft.Tracing;
using Microsoft.Agents.AI;
using Microsoft.Data.Sqlite;
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
    public async Task SubmitInputAsync_SubAgentJsonStringResult_PersistsSuccessfulToolResult()
    {
        const string resultJson = "{\"childThreadId\":\"thread_child\",\"status\":\"running\",\"profileName\":\"native\"}";
        IChatClient chatClient = new FakeChatClient([
            new ChatResponseUpdate(ChatRole.Assistant, [new FunctionResultContent("call-1", resultJson)])
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var loaded = await new ThreadStore(_tempDir).LoadThreadAsync(thread.Id);
        var turn = Assert.Single(loaded!.Turns);
        var resultItem = Assert.Single(turn.Items, item => item.Type == ItemType.ToolResult);
        var payload = Assert.IsType<ToolResultPayload>(resultItem.Payload);
        Assert.True(payload.Success);
        Assert.Equal("call-1", payload.CallId);
        Assert.Equal(resultJson, payload.Result);
        using var doc = JsonDocument.Parse(payload.Result);
        Assert.Equal("thread_child", doc.RootElement.GetProperty("childThreadId").GetString());
    }

    [Fact]
    public async Task SubAgentEdgeChanges_InvokeGraphChangedBroadcastHook()
    {
        IChatClient chatClient = new FakeChatClient([new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")])]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var parent = await svc.CreateThreadAsync(MakeIdentity(), threadId: "parent-1");
        var child = await svc.CreateThreadAsync(
            new SessionIdentity
            {
                ChannelName = SubAgentThreadOrigin.ChannelName,
                UserId = "u",
                ChannelContext = parent.Id,
                WorkspacePath = _tempDir
            },
            threadId: "child-1",
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = parent.Id,
                RootThreadId = parent.Id,
                Depth = 1
            }));
        var seen = new List<(string parentThreadId, string childThreadId)>();
        svc.SubAgentGraphChangedForBroadcast = (parentThreadId, childThreadId) =>
            seen.Add((parentThreadId, childThreadId));

        await svc.UpsertThreadSpawnEdgeAsync(new ThreadSpawnEdge
        {
            ParentThreadId = parent.Id,
            ChildThreadId = child.Id,
            Status = ThreadSpawnEdgeStatus.Open
        });
        await svc.SetThreadSpawnEdgeStatusAsync(
            parent.Id,
            child.Id,
            ThreadSpawnEdgeStatus.Closed);

        Assert.Equal(
            [("parent-1", "child-1"), ("parent-1", "child-1")],
            seen);
    }

    [Fact]
    public async Task CreateThreadAsync_TopLevelThread_SetsAgentControlToolsFullInToolContext()
    {
        IChatClient chatClient = new FakeChatClient([new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")])]);
        var recorder = new RecordingToolProvider();
        await using var agentFactory = CreateAgentFactory(chatClient, [recorder]);
        var svc = CreateService(agentFactory, chatClient);

        var thread = await svc.CreateThreadAsync(
            MakeIdentity(),
            config: new ThreadConfiguration(),
            threadId: "top-policy");

        var seen = Assert.Single(recorder.Contexts, context => context.CurrentThreadId == thread.Id);
        Assert.Equal(AgentControlToolAccess.Full, seen.AgentControlToolAccess);
    }

    [Fact]
    public async Task CreateThreadAsync_SubAgentThread_SetsAgentControlToolsDisabledInToolContext()
    {
        IChatClient chatClient = new FakeChatClient([new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")])]);
        var recorder = new RecordingToolProvider();
        await using var agentFactory = CreateAgentFactory(chatClient, [recorder]);
        var svc = CreateService(agentFactory, chatClient);

        var child = await svc.CreateThreadAsync(
            new SessionIdentity
            {
                ChannelName = SubAgentThreadOrigin.ChannelName,
                UserId = "u",
                ChannelContext = "parent-policy",
                WorkspacePath = _tempDir
            },
            config: new ThreadConfiguration(),
            threadId: "child-policy",
            source: ThreadSource.ForSubAgent(new SubAgentThreadSource
            {
                ParentThreadId = "parent-policy",
                RootThreadId = "parent-policy",
                Depth = 1
            }));

        var seen = Assert.Single(recorder.Contexts, context => context.CurrentThreadId == child.Id);
        Assert.Equal(AgentControlToolAccess.Disabled, seen.AgentControlToolAccess);
    }

    [Fact]
    public async Task SubmitInputAsync_RecordsServerManagedUsage_InTokenUsageStore()
    {
        IChatClient chatClient = new FakeChatClient(
        [
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")]),
            new ChatResponseUpdate
            {
                Role = ChatRole.Assistant,
                Contents =
                [
                    new UsageContent(new UsageDetails
                    {
                        InputTokenCount = 12,
                        OutputTokenCount = 8
                    })
                ]
            }
        ]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var tokenUsageStore = new TokenUsageStore(_tempDir);
        var svc = CreateService(agentFactory, chatClient, tokenUsageStore);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        await DrainAsync(svc.SubmitInputAsync(
            thread.Id,
            [new TextContent("hello")],
            new SenderContext
            {
                SenderId = "user-42",
                SenderName = "Alice",
                GroupId = "group-9"
            }));

        var summary = Assert.Single(tokenUsageStore.GetSourceSummaries());
        Assert.Equal("test", summary.SourceId);
        Assert.Equal(TokenUsageSourceModes.ServerManaged, summary.SourceMode);
        Assert.Equal(TokenUsageSubjectKinds.User, summary.SubjectKind);
        Assert.Equal(TokenUsageContextKinds.Group, summary.ContextKind);
        Assert.Equal(20, summary.TotalTokens);

        var subject = Assert.Single(tokenUsageStore.GetSubjectBreakdown("test"));
        Assert.Equal("user-42", subject.Id);
        Assert.Equal("Alice", subject.Label);
        Assert.Equal(20, subject.TotalTokens);

        var context = Assert.Single(tokenUsageStore.GetContextBreakdown("test"));
        Assert.Equal("group-9", context.Id);
        Assert.Equal("group-9", context.Label);
        Assert.Equal(1, context.RelatedSubjectCount);
    }

    [Fact]
    public async Task SubmitInputAsync_AppendsSenderRuntimeContext_AndPersistsInitiator()
    {
        var chatClient = new RecordingChatClient("ok");
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(new SessionIdentity
        {
            ChannelName = "qq",
            UserId = "10001",
            ChannelContext = "group:123456",
            WorkspacePath = _tempDir
        });

        await DrainAsync(svc.SubmitInputAsync(
            thread.Id,
            [new TextContent("hello")],
            new SenderContext
            {
                SenderId = "10001",
                SenderName = "Alice",
                SenderRole = "admin",
                GroupId = "123456"
            }));

        var userMessage = Assert.Single(chatClient.LastMessages, message => message.Role == ChatRole.User);
        var modelInput = string.Concat(userMessage.Contents.OfType<TextContent>().Select(content => content.Text));
        Assert.Contains("[Runtime Context]", modelInput);
        Assert.Contains("Channel: qq", modelInput);
        Assert.Contains("Channel Context: group:123456", modelInput);
        Assert.Contains("Sender QQ: 10001", modelInput);
        Assert.Contains("Sender Name: Alice", modelInput);
        Assert.Contains("QQ Group ID: 123456", modelInput);

        var persistedThread = await svc.GetThreadAsync(thread.Id);
        var turn = Assert.Single(persistedThread.Turns);
        Assert.Equal("qq", turn.Initiator?.ChannelName);
        Assert.Equal("10001", turn.Initiator?.UserId);
        Assert.Equal("Alice", turn.Initiator?.UserName);
        Assert.Equal("group:123456", turn.Initiator?.ChannelContext);
        Assert.Equal("123456", turn.Initiator?.GroupId);

        Assert.NotNull(turn.Input);
        var payload = turn.Input!.AsUserMessage;
        Assert.NotNull(payload);
        Assert.Equal("10001", payload!.SenderId);
        Assert.Equal("Alice", payload.SenderName);
        Assert.Equal("admin", payload.SenderRole);
        Assert.Equal("group:123456", payload.ChannelContext);
        Assert.Equal("123456", payload.GroupId);
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

    [Fact]
    public async Task DeleteThreadPermanentlyAsync_DuringRunningTurn_DoesNotRecreateThreadArtifacts()
    {
        IChatClient chatClient = new BlockingChatClient();
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());

        var drainTask = DrainAsync(svc.SubmitInputAsync(thread.Id, [new TextContent("hello")]));
        await Task.Delay(50);

        await svc.DeleteThreadPermanentlyAsync(thread.Id);
        await drainTask;

        var store = new ThreadStore(_tempDir);
        Assert.Null(await store.LoadThreadAsync(thread.Id));
        Assert.DoesNotContain(await store.LoadIndexAsync(), summary => summary.Id == thread.Id);
        Assert.False(File.Exists(Path.Combine(_tempDir, "threads", "active", $"{thread.Id}.jsonl")));
        Assert.False(File.Exists(Path.Combine(_tempDir, "threads", "archived", $"{thread.Id}.jsonl")));
    }

    [Fact]
    public async Task SubmitInputAsync_FirstCompletedTurn_PersistsThreadBeforeSessionRow()
    {
        var firstChatClient = new RecordingChatClient("first answer");
        await using var firstFactory = CreateAgentFactory(firstChatClient);
        var firstService = CreateService(firstFactory, firstChatClient);
        var thread = await firstService.CreateThreadAsync(MakeIdentity());

        await DrainAsync(firstService.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var store = new ThreadStore(_tempDir);
        Assert.NotNull(await store.LoadThreadAsync(thread.Id));
        Assert.True(store.SessionFileExists(thread.Id));
        Assert.True(ThreadRowExists(thread.Id));
    }

    [Fact]
    public async Task SubmitInputAsync_AcrossFreshServiceInstance_RestoresPriorConversation()
    {
        var firstChatClient = new RecordingChatClient("first answer");
        await using var firstFactory = CreateAgentFactory(firstChatClient);
        var firstService = CreateService(firstFactory, firstChatClient);
        var thread = await firstService.CreateThreadAsync(MakeIdentity());

        await DrainAsync(firstService.SubmitInputAsync(thread.Id, [new TextContent("hello")]));

        var secondChatClient = new RecordingChatClient("second answer");
        await using var secondFactory = CreateAgentFactory(secondChatClient);
        var secondService = CreateService(secondFactory, secondChatClient);
        await secondService.ResumeThreadAsync(thread.Id);

        await DrainAsync(secondService.SubmitInputAsync(thread.Id, [new TextContent("follow up")]));

        Assert.Equal(
            ["user:hello", "assistant:first answer", "user:follow up"],
            secondChatClient.LastMessages.Select(FormatMessage).ToList());
    }

    [Fact]
    public async Task SubmitInputAsync_WhenLegacyThreadSessionRowIsMissing_RebuildsHistoryFromRollout()
    {
        var firstChatClient = new RecordingChatClient("first answer");
        await using var firstFactory = CreateAgentFactory(firstChatClient);
        var firstService = CreateService(firstFactory, firstChatClient);
        var thread = await firstService.CreateThreadAsync(MakeIdentity());

        await DrainAsync(firstService.SubmitInputAsync(thread.Id, [new TextContent("hello")]));
        DeleteSessionRow(thread.Id);

        var secondChatClient = new RecordingChatClient("second answer");
        await using var secondFactory = CreateAgentFactory(secondChatClient);
        var secondService = CreateService(secondFactory, secondChatClient);
        await secondService.ResumeThreadAsync(thread.Id);

        await DrainAsync(secondService.SubmitInputAsync(thread.Id, [new TextContent("follow up")]));

        Assert.Equal(
            ["user:hello", "assistant:first answer", "user:follow up"],
            secondChatClient.LastMessages.Select(FormatMessage).ToList());
    }

    [Fact]
    public async Task QueuedInputOperations_AreSerializedPerThread()
    {
        IChatClient chatClient = new FakeChatClient([new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("ok")])]);
        await using var agentFactory = CreateAgentFactory(chatClient);
        var svc = CreateService(agentFactory, chatClient);
        var thread = await svc.CreateThreadAsync(MakeIdentity());
        thread.Turns.Add(new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        });

        var queuedInputs = new List<QueuedTurnInput>();
        for (var i = 0; i < 40; i++)
        {
            var text = $"queued {i}";
            queuedInputs.Add(await svc.EnqueueTurnInputAsync(
                thread.Id,
                [new TextContent(text)],
                inputSnapshot: new SessionInputSnapshot
                {
                    NativeInputParts = [new SessionWireInputPart { Type = "text", Text = text }],
                    MaterializedInputParts = [new SessionWireInputPart { Type = "text", Text = text }],
                    DisplayText = text
                }));
        }

        var operations = queuedInputs.Select(async (queued, index) =>
        {
            if (index % 2 == 0)
                await svc.RemoveQueuedTurnInputAsync(thread.Id, queued.Id);
            else
                await svc.SteerTurnAsync(thread.Id, "turn_001", queued.Id);
        }).ToArray();

        await Task.WhenAll(operations);

        var reloaded = await new ThreadStore(_tempDir).LoadThreadAsync(thread.Id);
        Assert.NotNull(reloaded);
        Assert.Equal(20, reloaded.QueuedInputs.Count);
        Assert.Equal(20, reloaded.QueuedInputs.Select(q => q.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(reloaded.QueuedInputs, queued => Assert.Equal("guidancePending", queued.Status));
        Assert.Equal(
            queuedInputs.Where((_, index) => index % 2 == 1).Select(q => q.Id).ToArray(),
            reloaded.QueuedInputs.Select(q => q.Id).ToArray());
    }

    private SessionService CreateService(
        AgentFactory agentFactory,
        IChatClient chatClient,
        TokenUsageStore? tokenUsageStore = null)
    {
        var defaultAgent = chatClient.AsAIAgent(new ChatClientAgentOptions());
        return new SessionService(
            agentFactory,
            defaultAgent,
            new SessionPersistenceService(new ThreadStore(_tempDir)),
            new SessionGate(),
            tokenUsageStore: tokenUsageStore);
    }

    private AgentFactory CreateAgentFactory(
        IChatClient chatClientFactory,
        IReadOnlyList<IAgentToolProvider>? toolProviders = null)
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
            toolProviders: toolProviders ?? Array.Empty<IAgentToolProvider>());
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

    private static string FormatMessage(ChatMessage message)
    {
        var text = string.Concat(message.Contents.OfType<TextContent>().Select(content => content.Text));
        var runtimeContextIndex = text.IndexOf("\n[Runtime Context]", StringComparison.Ordinal);
        if (runtimeContextIndex >= 0)
            text = text[..runtimeContextIndex];
        return $"{message.Role}:{text.Trim()}";
    }

    private bool ThreadRowExists(string threadId)
    {
        using var connection = OpenStateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM threads WHERE thread_id = $thread_id LIMIT 1";
        command.Parameters.AddWithValue("$thread_id", threadId);
        return command.ExecuteScalar() != null;
    }

    private void DeleteSessionRow(string threadId)
    {
        using var connection = OpenStateConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM thread_sessions WHERE thread_id = $thread_id";
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenStateConnection()
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(_tempDir, "state.db"),
                Mode = SqliteOpenMode.ReadWrite
            }.ToString());
        connection.Open();
        return connection;
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

    private sealed class RecordingChatClient(string responseText) : IChatClient
    {
        public IReadOnlyList<ChatMessage> LastMessages { get; private set; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent(responseText)])]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = chatMessages.ToList();
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new TextContent(responseText)]);
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingToolProvider : IAgentToolProvider
    {
        public int Priority => 10;

        public List<ToolProviderContext> Contexts { get; } = [];

        public IEnumerable<AITool> CreateTools(ToolProviderContext context)
        {
            Contexts.Add(context);
            return [];
        }
    }
}
