using DotCraft.Agents;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Agents;

public sealed partial class StreamingFunctionInvokingChatClientTests
{
    [Fact]
    public async Task GetStreamingResponseAsync_DrainsGuidanceBeforeNextModelRequest()
    {
        var inner = new RoundTripFakeChatClient();
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(inner)
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

    [Fact]
    public async Task GetStreamingResponseAsync_RunsPreSamplingCompactionBeforeModelRequest()
    {
        var inner = new SingleReplyFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner);
        var callbackCalls = 0;
        var replacement = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "compacted summary"),
            new(ChatRole.User, "latest user")
        };

        using var scope = PreSamplingCompactionRuntimeScope.Set(new PreSamplingCompactionRuntimeContext
        {
            TryCompactAsync = (messages, _) =>
            {
                callbackCalls++;
                Assert.Contains(messages, message => message.Role == ChatRole.User && message.Text == "start");
                return Task.FromResult<IReadOnlyList<ChatMessage>?>(replacement);
            }
        });

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        Assert.Equal(1, callbackCalls);
        var call = Assert.Single(inner.Calls);
        Assert.Equal(["assistant:compacted summary", "user:latest user"], call.Select(m => $"{m.Role}:{m.Text}"));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_LimitsGuidanceContinuationsAfterTermination()
    {
        var inner = new AlwaysCallsToolFakeChatClient();
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            MaximumIterationsPerRequest = 1,
            MaximumGuidanceContinuationsPerRequest = 2
        };
        var drains = 0;

        using var scope = TurnGuidanceRuntimeScope.Set(new TurnGuidanceRuntimeContext
        {
            ThreadId = "thread_1",
            TurnId = "turn_1",
            TryDrainGuidanceMessageAsync = _ =>
            {
                drains++;
                return Task.FromResult<ChatMessage?>(new ChatMessage(ChatRole.User, $"guidance {drains}"));
            }
        });

        await foreach (var _ in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "start")],
            new ChatOptions { Tools = [tool] }))
        {
        }

        Assert.Equal(3, drains);
        Assert.Equal(4, inner.Calls.Count);
        Assert.Contains(inner.Calls[1], message => message.Role == ChatRole.User && message.Text == "guidance 1");
        Assert.Contains(inner.Calls[2], message => message.Role == ChatRole.User && message.Text == "guidance 2");
        Assert.Contains(inner.Calls[3], message => message.Role == ChatRole.User && message.Text == "guidance 3");
        Assert.DoesNotContain(inner.Calls[3], message => message.Role == ChatRole.User && message.Text == "guidance 4");
    }

    [Fact]
    public async Task GetStreamingResponseAsync_UnknownToolCreatesFunctionResultByDefault()
    {
        var inner = new UnknownToolFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner);

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        Assert.Equal(2, inner.Calls.Count);
        var result = Assert.Single(inner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Equal("call-1", result.CallId);
        Assert.Contains("not found", result.Result?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_TerminateOnUnknownCallsLeavesCallForCaller()
    {
        var inner = new UnknownToolFakeChatClient();
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            TerminateOnUnknownCalls = true
        };

        var updates = await CollectAsync(client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]));

        Assert.Single(inner.Calls);
        Assert.Contains(updates, update => update.Contents.OfType<FunctionCallContent>().Any(call => call.Name == "Missing"));
        Assert.DoesNotContain(updates, update => update.Contents.OfType<FunctionResultContent>().Any());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_RemovesFunctionToolsOnLastIteration()
    {
        var inner = new AlwaysCallsToolFakeChatClient();
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            MaximumIterationsPerRequest = 1
        };

        await foreach (var _ in client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "start")],
            new ChatOptions { Tools = [tool] }))
        {
        }

        Assert.Equal(2, inner.Options.Count);
        Assert.NotEmpty(inner.Options[0]?.Tools ?? []);
        Assert.Empty(inner.Options[1]?.Tools ?? []);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_PropagatesConversationIdAndSendsOnlyToolResults()
    {
        var inner = new ConversationIdFakeChatClient();
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool]
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        Assert.Equal(2, inner.Calls.Count);
        Assert.Equal("conv-1", inner.Options[1]?.ConversationId);
        Assert.DoesNotContain(inner.Calls[1], message => message.Role == ChatRole.User);
        Assert.Contains(inner.Calls[1], message => message.Role == ChatRole.Tool);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_UsesGenericToolErrorUnlessDetailedErrorsAreEnabled()
    {
        var genericInner = new FailingToolFakeChatClient();
        var genericClient = new StreamingFunctionInvokingChatClient(genericInner)
        {
            AdditionalTools = [AIFunctionFactory.Create(ThrowBoom, name: "Fail")]
        };

        await foreach (var _ in genericClient.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        var genericResult = Assert.Single(genericInner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Equal("Error: Function failed.", genericResult.Result);

        var detailedInner = new FailingToolFakeChatClient();
        var detailedClient = new StreamingFunctionInvokingChatClient(detailedInner)
        {
            AdditionalTools = [AIFunctionFactory.Create(ThrowBoom, name: "Fail")],
            IncludeDetailedErrors = true
        };

        await foreach (var _ in detailedClient.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        var detailedResult = Assert.Single(detailedInner.Calls[1].SelectMany(message => message.Contents).OfType<FunctionResultContent>());
        Assert.Contains("boom", detailedResult.Result?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_ExposesCurrentInvocationContext()
    {
        var inner = new RoundTripFakeChatClient();
        var tool = AIFunctionFactory.Create(() => "tool ok", name: "GetStatus");
        FunctionInvocationContext? captured = null;
        var client = new StreamingFunctionInvokingChatClient(inner)
        {
            AdditionalTools = [tool],
            FunctionInvoker = (context, _) =>
            {
                captured = StreamingFunctionInvokingChatClient.CurrentContext;
                return ValueTask.FromResult<object?>("tool ok");
            }
        };

        await foreach (var _ in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "start")]))
        {
        }

        Assert.NotNull(captured);
        Assert.Equal("GetStatus", captured.Function.Name);
        Assert.Null(StreamingFunctionInvokingChatClient.CurrentContext);
    }

    private static string ThrowBoom() => throw new InvalidOperationException("boom");

    private static async Task<List<ChatResponseUpdate>> CollectAsync(IAsyncEnumerable<ChatResponseUpdate> updates)
    {
        var result = new List<ChatResponseUpdate>();
        await foreach (var update in updates)
            result.Add(update);
        return result;
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

    private sealed class SingleReplyFakeChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "done")]));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Calls.Add(chatMessages.ToList());
            yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class UnknownToolFakeChatClient : IChatClient
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
                    new FunctionCallContent("call-1", "Missing", new Dictionary<string, object?>())
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

    private sealed class AlwaysCallsToolFakeChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];
        public List<ChatOptions?> Options { get; } = [];

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
            Options.Add(options);
            if (Options.Count == 1)
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

    private sealed class ConversationIdFakeChatClient : IChatClient
    {
        public List<List<ChatMessage>> Calls { get; } = [];
        public List<ChatOptions?> Options { get; } = [];

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
            Options.Add(options);
            if (Calls.Count == 1)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, [
                    new FunctionCallContent("call-1", "GetStatus", new Dictionary<string, object?>())
                ])
                {
                    ConversationId = "conv-1"
                };
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

    private sealed class FailingToolFakeChatClient : IChatClient
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
                    new FunctionCallContent("call-1", "Fail", new Dictionary<string, object?>())
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

