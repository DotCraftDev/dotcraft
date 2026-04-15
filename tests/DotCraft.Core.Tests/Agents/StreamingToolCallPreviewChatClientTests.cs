using DotCraft.Agents;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Agents;

public sealed class StreamingToolCallPreviewChatClientTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_TextOnly_NoInjectedDelta()
    {
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("hello")])
        };
        var client = new StreamingToolCallPreviewChatClient(new FakeChatClient(streamUpdates: updates));

        var collected = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            collected.Add(update);

        Assert.Single(collected);
        Assert.DoesNotContain(collected[0].Contents, c => c is ToolCallArgumentsDeltaContent);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_SingleToolCall_InjectsChunks()
    {
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("a")])
            {
                RawRepresentation = new FakeRawDeltaSource(
                    new ToolCallDeltaChunk(0, "WriteFile", "call-1", "{\"path\":\"a.txt\",\"content\":\"hel"))
            },
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("b")])
            {
                RawRepresentation = new FakeRawDeltaSource(
                    new ToolCallDeltaChunk(0, null, null, "lo\"}"))
            }
        };
        var client = new StreamingToolCallPreviewChatClient(new FakeChatClient(streamUpdates: updates))
        {
            StreamableToolNames = new HashSet<string>(["WriteFile"], StringComparer.Ordinal)
        };

        var deltas = new List<ToolCallArgumentsDeltaContent>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            deltas.AddRange(update.Contents.OfType<ToolCallArgumentsDeltaContent>());

        Assert.Equal(2, deltas.Count);
        Assert.Equal("WriteFile", deltas[0].ToolName);
        Assert.Equal("call-1", deltas[0].CallId);
        Assert.Equal("{\"path\":\"a.txt\",\"content\":\"hel", deltas[0].ArgumentsDelta);
        Assert.Null(deltas[1].ToolName);
        Assert.Null(deltas[1].CallId);
        Assert.Equal("lo\"}", deltas[1].ArgumentsDelta);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_FilteredTool_DoesNotInject()
    {
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("a")])
            {
                RawRepresentation = new FakeRawDeltaSource(
                    new ToolCallDeltaChunk(0, "ReadFile", "call-1", "{\"path\":\"a.txt\"}"))
            }
        };
        var client = new StreamingToolCallPreviewChatClient(new FakeChatClient(streamUpdates: updates))
        {
            StreamableToolNames = new HashSet<string>(["WriteFile"], StringComparer.Ordinal)
        };

        var collected = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            collected.Add(update);

        Assert.Single(collected);
        Assert.DoesNotContain(collected[0].Contents, c => c is ToolCallArgumentsDeltaContent);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ParallelIndexes_AreTrackedIndependently()
    {
        var updates = new[]
        {
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("a")])
            {
                RawRepresentation = new FakeRawDeltaSource(
                    new ToolCallDeltaChunk(0, "WriteFile", "call-1", "{\"content\":\"a"),
                    new ToolCallDeltaChunk(1, "EditFile", "call-2", "{\"old\":\"x"))
            },
            new ChatResponseUpdate(ChatRole.Assistant, [new TextContent("b")])
            {
                RawRepresentation = new FakeRawDeltaSource(
                    new ToolCallDeltaChunk(0, null, null, "b\"}"),
                    new ToolCallDeltaChunk(1, null, null, "y\"}"))
            }
        };
        var client = new StreamingToolCallPreviewChatClient(new FakeChatClient(streamUpdates: updates));

        var deltas = new List<ToolCallArgumentsDeltaContent>();
        await foreach (var update in client.GetStreamingResponseAsync([]))
            deltas.AddRange(update.Contents.OfType<ToolCallArgumentsDeltaContent>());

        Assert.Equal(4, deltas.Count);
        Assert.Equal(0, deltas[0].ToolCallIndex);
        Assert.Equal("WriteFile", deltas[0].ToolName);
        Assert.Equal(1, deltas[1].ToolCallIndex);
        Assert.Equal("EditFile", deltas[1].ToolName);
        Assert.Equal(0, deltas[2].ToolCallIndex);
        Assert.Null(deltas[2].ToolName);
        Assert.Equal(1, deltas[3].ToolCallIndex);
        Assert.Null(deltas[3].ToolName);
    }

    [Fact]
    public async Task GetResponseAsync_PassthroughUnchanged()
    {
        var expected = new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])]);
        var client = new StreamingToolCallPreviewChatClient(new FakeChatClient(response: expected));

        var actual = await client.GetResponseAsync([]);

        Assert.Same(expected, actual);
    }

    private sealed class FakeRawDeltaSource(params ToolCallDeltaChunk[] chunks) : IToolCallDeltaChunkSource
    {
        public IEnumerable<ToolCallDeltaChunk> GetToolCallDeltaChunks() => chunks;
    }

    private sealed class FakeChatClient : IChatClient
    {
        private readonly ChatResponse? _response;
        private readonly ChatResponseUpdate[] _streamUpdates;

        public FakeChatClient(ChatResponse? response = null, ChatResponseUpdate[]? streamUpdates = null)
        {
            _response = response;
            _streamUpdates = streamUpdates ?? [];
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_response ?? new ChatResponse([new ChatMessage(ChatRole.Assistant, [new TextContent("ok")])]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            foreach (var update in _streamUpdates)
                yield return update;
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
