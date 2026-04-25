using DotCraft.Agents;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Agents;

public sealed class SteerableFunctionInvokingChatClientTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_DrainsGuidanceBeforeNextModelRequest()
    {
        var inner = new RoundTripFakeChatClient();
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new SteerableFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool]
        };
        var drained = false;

        using var scope = TurnGuidanceRuntimeScope.Set(new TurnGuidanceRuntimeContext
        {
            ThreadId = "thread_1",
            TurnId = "turn_1",
            TryDrainGuidanceMessageAsync = _ =>
            {
                if (drained)
                    return Task.FromResult<ChatMessage?>(null);
                drained = true;
                return Task.FromResult<ChatMessage?>(new ChatMessage(ChatRole.User, "guidance text"));
            }
        });

        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
            updates.Add(update);

        Assert.True(drained);
        Assert.Equal(2, inner.Calls.Count);
        Assert.Contains(inner.Calls[1], message => message.Role == ChatRole.User && message.Text == "guidance text");
        Assert.Contains(updates, update => update.Contents.OfType<FunctionResultContent>().Any());
    }

    private sealed class RoundTripFakeChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "ok")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("call-1", "GetStatus", new Dictionary<string, object?>())
                ]);
            }
            else
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            }

            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
