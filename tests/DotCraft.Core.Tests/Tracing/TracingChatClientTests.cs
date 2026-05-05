using System.Runtime.CompilerServices;
using DotCraft.State;
using DotCraft.Tracing;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Tracing;

public sealed class TracingChatClientTests
{
    [Fact]
    public async Task StreamingReasoningChunks_RecordOneThinkingSegment()
    {
        var store = await RunStreamingAsync([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("The ")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("user")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent(string.Empty)])
        ], "trace-reasoning");

        var thinking = EventsOfType(store, "trace-reasoning", TraceEventType.Thinking);
        var session = store.GetSession("trace-reasoning");

        var evt = Assert.Single(thinking);
        Assert.Equal("The user", evt.Content);
        Assert.Equal(1, session?.ThinkingCount);
    }

    [Fact]
    public async Task StreamingTextChunks_RecordOneResponseSegment()
    {
        var store = await RunStreamingAsync([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("Hello ")])
            {
                ResponseId = "resp-1",
                ModelId = "model-a"
            },
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("world")])
            {
                ResponseId = "resp-1",
                MessageId = "msg-1",
                ModelId = "model-a"
            },
            new ChatResponseUpdate(ChatRole.Assistant, [])
            {
                FinishReason = ChatFinishReason.Stop
            }
        ], "trace-response");

        var responses = EventsOfType(store, "trace-response", TraceEventType.Response);
        var session = store.GetSession("trace-response");

        var evt = Assert.Single(responses);
        Assert.Equal("Hello world", evt.Content);
        Assert.Equal("resp-1", evt.ResponseId);
        Assert.Equal("msg-1", evt.MessageId);
        Assert.Equal("model-a", evt.ModelId);
        Assert.Equal(ChatFinishReason.Stop.ToString(), evt.FinishReason);
        Assert.Equal(1, session?.ResponseCount);
    }

    [Fact]
    public async Task StreamingAlternatesThinkingAndResponse_RecordsOrderedSegments()
    {
        var store = await RunStreamingAsync([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("first thought")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("answer")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("second thought")])
        ], "trace-alternating");

        var events = NonRequestEvents(store, "trace-alternating");

        Assert.Equal(
            [TraceEventType.Thinking, TraceEventType.Response, TraceEventType.Thinking],
            events.Select(e => e.Type).ToArray());
        Assert.Equal(
            ["first thought", "answer", "second thought"],
            events.Select(e => e.Content ?? string.Empty).ToArray());
    }

    [Fact]
    public async Task StreamingToolBoundary_SplitsResponseSegmentsAroundToolEvent()
    {
        var store = await RunStreamingAsync([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("before tool")]),
            new ChatResponseUpdate(ChatRole.Assistant, [
                new FunctionCallContent("call-1", "ReadFile", new Dictionary<string, object?> { ["path"] = "a.txt" })
            ]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("after tool")])
        ], "trace-tool-boundary");

        var events = NonRequestEvents(store, "trace-tool-boundary");
        var session = store.GetSession("trace-tool-boundary");

        Assert.Equal(
            [TraceEventType.Response, TraceEventType.ToolCallStarted, TraceEventType.Response],
            events.Select(e => e.Type).ToArray());
        Assert.Equal("before tool", events[0].Content);
        Assert.Equal("ReadFile", events[1].ToolName);
        Assert.Equal("after tool", events[2].Content);
        Assert.Equal(2, session?.ResponseCount);
    }

    [Fact]
    public async Task StreamingException_FlushesPendingSegmentsBeforeError()
    {
        var store = await RunStreamingAsync([
            new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("thinking")]),
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("partial answer")])
        ], "trace-exception", new InvalidOperationException("boom"));

        var events = NonRequestEvents(store, "trace-exception");

        Assert.Equal(
            [TraceEventType.Thinking, TraceEventType.Response, TraceEventType.Error],
            events.Select(e => e.Type).ToArray());
        Assert.Equal("thinking", events[0].Content);
        Assert.Equal("partial answer", events[1].Content);
        Assert.Equal("boom", events[2].Content);
    }

    [Fact]
    public async Task StreamingSegments_PersistAndReloadWithSegmentCounts()
    {
        var root = Path.Combine(Path.GetTempPath(), "tracing-chat-client-tests", Guid.NewGuid().ToString("N"));
        var craftPath = Path.Combine(root, ".craft");
        var tracingPath = Path.Combine(craftPath, "tracing");
        Directory.CreateDirectory(tracingPath);

        try
        {
            var stateRuntime = new StateRuntime(craftPath);
            var writer = new TraceStore(tracingPath, 5000, false, stateRuntime);
            await RunStreamingAsync([
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("one ")]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("two")]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("answer ")]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("segment")]),
                new ChatResponseUpdate(ChatRole.Assistant, [new TextReasoningContent("after")])
            ], "trace-persisted", store: writer);
            writer.WaitForPendingPersistence();

            var reader = new TraceStore(tracingPath, 5000, false, stateRuntime);
            reader.LoadFromDisk();

            var session = reader.GetSession("trace-persisted");
            var events = NonRequestEvents(reader, "trace-persisted");

            Assert.Equal(2, session?.ThinkingCount);
            Assert.Equal(1, session?.ResponseCount);
            Assert.Equal(
                [TraceEventType.Thinking, TraceEventType.Response, TraceEventType.Thinking],
                events.Select(e => e.Type).ToArray());
            Assert.Equal(
                ["one two", "answer segment", "after"],
                events.Select(e => e.Content ?? string.Empty).ToArray());
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best-effort cleanup; SQLite can briefly hold the file on Windows.
            }
        }
    }

    private static async Task<TraceStore> RunStreamingAsync(
        ChatResponseUpdate[] updates,
        string sessionKey,
        Exception? throwAfterUpdates = null,
        TraceStore? store = null)
    {
        store ??= new TraceStore();
        var collector = new TraceCollector(store);
        var client = new TracingChatClient(new FakeStreamingChatClient(updates, throwAfterUpdates), collector);
        var previousSessionKey = TracingChatClient.CurrentSessionKey;

        TracingChatClient.ResetCallState(sessionKey);
        TracingChatClient.CurrentSessionKey = sessionKey;
        try
        {
            if (throwAfterUpdates == null)
            {
                await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
                {
                }
            }
            else
            {
                await Assert.ThrowsAsync(throwAfterUpdates.GetType(), async () =>
                {
                    await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "hi")]))
                    {
                    }
                });
            }

            return store;
        }
        finally
        {
            TracingChatClient.ResetCallState(sessionKey);
            TracingChatClient.CurrentSessionKey = previousSessionKey;
        }
    }

    private static IReadOnlyList<TraceEvent> NonRequestEvents(TraceStore store, string sessionKey)
        => store.GetEvents(sessionKey).Where(e => e.Type != TraceEventType.Request).ToList();

    private static IReadOnlyList<TraceEvent> EventsOfType(
        TraceStore store,
        string sessionKey,
        TraceEventType type)
        => store.GetEvents(sessionKey).Where(e => e.Type == type).ToList();

    private sealed class FakeStreamingChatClient(
        ChatResponseUpdate[] updates,
        Exception? throwAfterUpdates = null) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var update in updates)
                yield return update;

            if (throwAfterUpdates != null)
                throw throwAfterUpdates;

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
