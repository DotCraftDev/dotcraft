using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// Lightweight <see cref="DelegatingChatClient"/> placed inside
/// <see cref="FunctionInvokingChatClient"/> in the SubAgent pipeline.
/// Accumulates token usage from LLM responses so the Live Table can
/// display per-SubAgent token counts. Tool activity tracking is handled
/// separately via <see cref="FunctionInvokingChatClient.FunctionInvoker"/>.
/// All stream content is passed through unchanged.
/// </summary>
internal sealed class SubAgentProgressChatClient(
    IChatClient innerClient,
    SubAgentProgressBridge.ProgressEntry progressEntry) : DelegatingChatClient(innerClient)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);

        if (response.Usage != null)
        {
            var input = response.Usage.InputTokenCount ?? 0;
            var output = response.Usage.OutputTokenCount ?? 0;
            if (input > 0 || output > 0)
                progressEntry.AddTokens(input, output);
        }

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        long inputTokens = 0, outputTokens = 0;

        await foreach (var update in base.GetStreamingResponseAsync(chatMessages, options, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                if (content is UsageContent usage)
                {
                    if (usage.Details.InputTokenCount.HasValue)
                        inputTokens = usage.Details.InputTokenCount.Value;
                    if (usage.Details.OutputTokenCount.HasValue)
                        outputTokens = usage.Details.OutputTokenCount.Value;
                }
            }

            yield return update;
        }

        if (inputTokens > 0 || outputTokens > 0)
            progressEntry.AddTokens(inputTokens, outputTokens);
    }
}
