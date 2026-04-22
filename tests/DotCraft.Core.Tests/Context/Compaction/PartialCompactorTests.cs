using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Context.Compaction;

public sealed class PartialCompactorTests
{
    [Fact]
    public void CalculateSplitIndex_SmallConversationKeepsEverything()
    {
        var cfg = new CompactionConfig
        {
            KeepRecentMinTokens = 100,
            KeepRecentMinGroups = 5,
            KeepRecentMaxTokens = 200,
        };
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "hi"),
            new(ChatRole.Assistant, "hello"),
        };

        Assert.Equal(0, PartialCompactor.CalculateSplitIndex(messages, cfg));
    }

    [Fact]
    public void CalculateSplitIndex_LargeConversationSplitsPreservingTail()
    {
        var cfg = new CompactionConfig
        {
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 2,
            KeepRecentMaxTokens = 100_000,
        };

        var messages = new List<ChatMessage>();
        for (var round = 0; round < 5; round++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user turn {round}"));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant turn {round}"));
        }

        var splitIndex = PartialCompactor.CalculateSplitIndex(messages, cfg);
        // KeepRecentMinGroups = 2 → preserve 2 groups of 2 messages each → split after 6 messages.
        Assert.Equal(6, splitIndex);
    }

    [Fact]
    public async Task CompactAsync_EmptyHistoryReturnsNull()
    {
        var cfg = new CompactionConfig();
        var partial = new PartialCompactor(new StubChatClient("summary"), cfg);

        var result = await partial.CompactAsync(Array.Empty<ChatMessage>());
        Assert.Null(result);
    }

    [Fact]
    public async Task CompactAsync_SummarizesPrefixAndRetainsTail()
    {
        var cfg = new CompactionConfig
        {
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
            KeepRecentMaxTokens = 100_000,
        };
        var client = new StubChatClient("<analysis>thinking</analysis><summary>important bits</summary>");
        var partial = new PartialCompactor(client, cfg);

        var messages = new List<ChatMessage>();
        for (var round = 0; round < 4; round++)
        {
            messages.Add(new ChatMessage(ChatRole.User, $"user turn {round}"));
            messages.Add(new ChatMessage(ChatRole.Assistant, $"assistant turn {round}"));
        }

        var result = await partial.CompactAsync(messages);
        Assert.NotNull(result);
        Assert.True(result!.SummarizedPrefix.Count > 0);
        Assert.True(result.PreservedTail.Count > 0);
        Assert.Contains("important bits", result.FormattedSummary);
        // analysis should be stripped from FormattedSummary.
        Assert.DoesNotContain("<analysis>", result.FormattedSummary);
        Assert.Equal("<analysis>thinking</analysis><summary>important bits</summary>", result.RawSummary);
    }

    [Fact]
    public async Task CompactAsync_NullOnChatClientFailure()
    {
        var cfg = new CompactionConfig
        {
            KeepRecentMinTokens = 1,
            KeepRecentMinGroups = 1,
        };
        var partial = new PartialCompactor(new StubChatClient(string.Empty, throwOnCall: true), cfg);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "u1"),
            new(ChatRole.Assistant, "a1"),
            new(ChatRole.User, "u2"),
            new(ChatRole.Assistant, "a2"),
        };

        var result = await partial.CompactAsync(messages);
        Assert.Null(result);
    }

    private sealed class StubChatClient : IChatClient
    {
        private readonly string _responseText;
        private readonly bool _throwOnCall;

        public StubChatClient(string responseText, bool throwOnCall = false)
        {
            _responseText = responseText;
            _throwOnCall = throwOnCall;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (_throwOnCall)
                throw new InvalidOperationException("boom");

            var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, _responseText));
            return Task.FromResult(response);
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
