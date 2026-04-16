using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using DotCraft.Protocol;

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
    private long _lastSnapshotInput;
    private long _lastSnapshotOutput;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(chatMessages, options, cancellationToken);

        if (response.Usage != null)
        {
            var curIn = response.Usage.InputTokenCount ?? 0;
            var curOut = response.Usage.OutputTokenCount ?? 0;
            if (curIn > 0 || curOut > 0)
            {
                UsageSnapshotDelta.Compute(
                    curIn,
                    curOut,
                    ref _lastSnapshotInput,
                    ref _lastSnapshotOutput,
                    out var deltaIn,
                    out var deltaOut);

                if (deltaIn > 0 || deltaOut > 0)
                    progressEntry.AddTokens(deltaIn, deltaOut);
            }
        }

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(chatMessages, options, cancellationToken)
                           .WithCancellation(cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                if (content is UsageContent usage)
                {
                    var curIn = usage.Details.InputTokenCount ?? 0;
                    var curOut = usage.Details.OutputTokenCount ?? 0;
                    if (curIn > 0 || curOut > 0)
                    {
                        UsageSnapshotDelta.Compute(
                            curIn,
                            curOut,
                            ref _lastSnapshotInput,
                            ref _lastSnapshotOutput,
                            out var deltaIn,
                            out var deltaOut);

                        if (deltaIn > 0 || deltaOut > 0)
                            progressEntry.AddTokens(deltaIn, deltaOut);
                    }
                }
            }

            yield return update;
        }
    }
}
