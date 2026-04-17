using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using OpenAiStreamingUpdate = OpenAI.Chat.StreamingChatCompletionUpdate;

namespace DotCraft.Agents;

/// <summary>
/// Injects tool-call argument delta content during streaming by inspecting
/// provider-native updates in <see cref="ChatResponseUpdate.RawRepresentation"/>.
/// </summary>
public sealed class StreamingToolCallPreviewChatClient(IChatClient innerClient)
    : DelegatingChatClient(innerClient)
{
    /// <summary>
    /// Optional predicate that decides whether argument deltas should be emitted for a tool.
    /// When <see langword="null"/> (default) all tools are eligible.
    /// </summary>
    public Func<string, bool>? IsStreamableTool { get; set; }

    /// <summary>
    /// Tool names that should emit argument delta previews. Used as a fallback when
    /// <see cref="IsStreamableTool"/> is not set. When both are <see langword="null"/>,
    /// all tools are eligible.
    /// </summary>
    public IReadOnlySet<string>? StreamableToolNames { get; set; }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Dictionary<int, ToolCallTracker>? trackers = null;

        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (var delta in ExtractDeltas(update.RawRepresentation))
            {
                trackers ??= [];

                // FunctionInvokingChatClient may stream multiple model rounds in one enumerable.
                // OpenAI resets tool-call indices per round, so reset trackers on index-0 CallId rollover.
                if (delta.Index == 0
                    && delta.CallId is not null
                    && trackers.TryGetValue(0, out var existingTracker)
                    && existingTracker.CallId != delta.CallId)
                {
                    trackers.Clear();
                }

                if (!trackers.TryGetValue(delta.Index, out var tracker))
                {
                    tracker = new ToolCallTracker();
                    trackers[delta.Index] = tracker;
                }

                tracker.CallId ??= delta.CallId;
                tracker.ToolName ??= delta.ToolName;

                if (string.IsNullOrEmpty(delta.ArgumentsDelta))
                    continue;
                if (tracker.ToolName is null)
                    continue;
                if (!IsEligible(tracker.ToolName))
                    continue;

                var isFirst = !tracker.FirstChunkEmitted;
                tracker.FirstChunkEmitted = true;
                update.Contents.Add(new ToolCallArgumentsDeltaContent
                {
                    ToolCallIndex = delta.Index,
                    ToolName = isFirst ? tracker.ToolName : null,
                    CallId = isFirst ? tracker.CallId : null,
                    ArgumentsDelta = delta.ArgumentsDelta
                });
            }

            yield return update;
        }
    }

    private bool IsEligible(string toolName)
    {
        if (IsStreamableTool is not null)
            return IsStreamableTool(toolName);
        if (StreamableToolNames is not null)
            return StreamableToolNames.Contains(toolName);
        return true;
    }

    internal static IEnumerable<ToolCallDeltaChunk> ExtractDeltas(object? rawRepresentation)
    {
        if (rawRepresentation is OpenAiStreamingUpdate openAiUpdate
            && openAiUpdate.ToolCallUpdates is { Count: > 0 } toolCallUpdates)
        {
            foreach (var toolCallUpdate in toolCallUpdates)
            {
                yield return new ToolCallDeltaChunk(
                    toolCallUpdate.Index,
                    toolCallUpdate.FunctionName,
                    toolCallUpdate.ToolCallId,
                    toolCallUpdate.FunctionArgumentsUpdate?.ToString());
            }

            yield break;
        }

        if (rawRepresentation is IToolCallDeltaChunkSource source)
        {
            foreach (var chunk in source.GetToolCallDeltaChunks())
                yield return chunk;
        }
    }

    private sealed class ToolCallTracker
    {
        public string? ToolName { get; set; }

        public string? CallId { get; set; }

        public bool FirstChunkEmitted { get; set; }
    }
}

/// <summary>
/// Internal test seam for providing tool-call chunks without constructing provider SDK types.
/// </summary>
internal interface IToolCallDeltaChunkSource
{
    IEnumerable<ToolCallDeltaChunk> GetToolCallDeltaChunks();
}

/// <summary>
/// Normalized tool-call chunk extracted from provider-native streaming payload.
/// </summary>
internal readonly record struct ToolCallDeltaChunk(
    int Index,
    string? ToolName,
    string? CallId,
    string? ArgumentsDelta);
