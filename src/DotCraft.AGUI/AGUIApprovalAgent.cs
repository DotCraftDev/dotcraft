using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.AGUI;

/// <summary>
/// A delegating agent that handles function approval requests for the AG-UI channel.
/// Transforms FunctionApprovalRequestContent / FunctionApprovalResponseContent into
/// the standard "request_approval" tool call pattern so that CopilotKit on the frontend
/// can render an approval UI and return the user's decision.
/// </summary>
/// <remarks>
/// Adapted from AgentFramework Step04_HumanInLoop sample.
/// Flow:
///   Outgoing – FunctionApprovalRequestContent → TOOL_CALL_START(request_approval) via SSE
///   Incoming – request_approval tool result     → FunctionApprovalResponseContent → inner agent
/// </remarks>
internal sealed class AGUIApprovalAgent(AIAgent innerAgent, JsonSerializerOptions jsonSerializerOptions)
    : DelegatingAIAgent(innerAgent)
{
    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return RunCoreStreamingAsync(messages, session, options, cancellationToken)
            .ToAgentResponseAsync(cancellationToken);
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var processedMessages = ProcessIncomingApprovals(messages.ToList(), jsonSerializerOptions);

        await foreach (var update in InnerAgent
            .RunStreamingAsync(processedMessages, session, options, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return ProcessOutgoingApprovals(update, jsonSerializerOptions);
        }
    }

    // ── Outgoing: intercept FunctionApprovalRequestContent, replace with request_approval tool call ──

    private static AgentResponseUpdate ProcessOutgoingApprovals(
        AgentResponseUpdate update,
        JsonSerializerOptions jsonSerializerOptions)
    {
        IList<AIContent>? updatedContents = null;

        for (var i = 0; i < update.Contents.Count; i++)
        {
            var content = update.Contents[i];
#pragma warning disable MEAI001
            if (content is FunctionApprovalRequestContent request)
            {
                updatedContents ??= [.. update.Contents];

                var approvalData = new ApprovalRequest
                {
                    ApprovalId = request.Id,
                    FunctionName = request.FunctionCall.Name,
                    FunctionArguments = request.FunctionCall.Arguments,
                    Message = $"Approve execution of '{request.FunctionCall.Name}'?"
                };

                // Pre-serialize to JsonElement so the AGUI framework writes it as raw JSON
                // (avoids double-write bug when ApprovalRequest sits as object? in a Dictionary<string, object?>).
                var approvalElement = JsonSerializer.SerializeToElement(approvalData, jsonSerializerOptions);
                // Use a distinct callId so the frontend never concatenates this tool call's args
                // with the original tool call that shares the same LLM-generated ID.
                updatedContents[i] = new FunctionCallContent(
                    callId: $"approval_{request.Id}",
                    name: "request_approval",
                    arguments: new Dictionary<string, object?> { ["request"] = approvalElement });
            }
#pragma warning restore MEAI001
        }

        if (updatedContents is null)
            return update;

        var chatUpdate = update.AsChatResponseUpdate();
        return new AgentResponseUpdate(new ChatResponseUpdate
        {
            Role = chatUpdate.Role,
            Contents = updatedContents,
            MessageId = chatUpdate.MessageId,
            AuthorName = chatUpdate.AuthorName,
            CreatedAt = chatUpdate.CreatedAt,
            RawRepresentation = chatUpdate.RawRepresentation,
            ResponseId = chatUpdate.ResponseId,
            AdditionalProperties = chatUpdate.AdditionalProperties
        })
        {
            AgentId = update.AgentId,
            ContinuationToken = update.ContinuationToken
        };
    }

    // ── Incoming: convert request_approval tool call/result back to approval content types ──

    private static List<ChatMessage> ProcessIncomingApprovals(
        List<ChatMessage> messages,
        JsonSerializerOptions jsonSerializerOptions)
    {
        List<ChatMessage>? result = null;
#pragma warning disable MEAI001
        var trackedApprovals = new Dictionary<string, FunctionApprovalRequestContent>();

        for (var msgIdx = 0; msgIdx < messages.Count; msgIdx++)
        {
            var message = messages[msgIdx];
            List<AIContent>? transformedContents = null;

            for (var j = 0; j < message.Contents.Count; j++)
            {
                var content = message.Contents[j];
                AIContent? replacement = null;

                if (content is FunctionCallContent { Name: "request_approval" } toolCall)
                {
                    var approvalRequest = ConvertToolCallToApprovalRequest(toolCall, jsonSerializerOptions);
                    trackedApprovals[toolCall.CallId] = approvalRequest;
                    replacement = approvalRequest;
                }
                else if (content is FunctionResultContent toolResult &&
                         trackedApprovals.TryGetValue(toolResult.CallId, out var tracked))
                {
                    replacement = ConvertToolResultToApprovalResponse(toolResult, tracked, jsonSerializerOptions);
                }

                if (replacement != null)
                {
                    // Lazily copy preceding contents when we first encounter something to transform
                    if (transformedContents == null)
                    {
                        transformedContents = new List<AIContent>(message.Contents.Count);
                        for (var k = 0; k < j; k++)
                            transformedContents.Add(message.Contents[k]);
                    }
                    transformedContents.Add(replacement);
                }
                else
                {
                    transformedContents?.Add(content);
                }
            }

            if (transformedContents != null)
            {
                result ??= CopyMessages(messages, msgIdx);
                result.Add(BuildMessage(message, transformedContents));
            }
            else
            {
                result?.Add(message);
            }
        }
#pragma warning restore MEAI001

        return result ?? messages;
    }

#pragma warning disable MEAI001
    private static FunctionApprovalRequestContent ConvertToolCallToApprovalRequest(
        FunctionCallContent toolCall,
        JsonSerializerOptions jsonSerializerOptions)
    {
        if (toolCall.Name != "request_approval" || toolCall.Arguments == null)
            throw new InvalidOperationException("Invalid request_approval tool call.");

        var approvalRequest = toolCall.Arguments.TryGetValue("request", out var reqObj) &&
            reqObj is JsonElement element &&
            element.Deserialize(jsonSerializerOptions.GetTypeInfo(typeof(ApprovalRequest))) is ApprovalRequest r
            ? r : null;

        if (approvalRequest == null)
            throw new InvalidOperationException("Failed to deserialize ApprovalRequest from tool call.");

        return new FunctionApprovalRequestContent(
            id: approvalRequest.ApprovalId,
            new FunctionCallContent(
                callId: approvalRequest.ApprovalId,
                name: approvalRequest.FunctionName,
                arguments: approvalRequest.FunctionArguments));
    }

    private static FunctionApprovalResponseContent ConvertToolResultToApprovalResponse(
        FunctionResultContent result,
        FunctionApprovalRequestContent approval,
        JsonSerializerOptions jsonSerializerOptions)
    {
        var approvalResponse = result.Result is JsonElement je
            ? (ApprovalResponse?)je.Deserialize(jsonSerializerOptions.GetTypeInfo(typeof(ApprovalResponse)))
            : result.Result is string str
                ? (ApprovalResponse?)JsonSerializer.Deserialize(str, jsonSerializerOptions.GetTypeInfo(typeof(ApprovalResponse)))
                : result.Result as ApprovalResponse;

        if (approvalResponse == null)
            throw new InvalidOperationException("Failed to deserialize ApprovalResponse from tool result.");

        return approval.CreateResponse(approvalResponse.Approved);
    }
#pragma warning restore MEAI001

    private static List<ChatMessage> CopyMessages(List<ChatMessage> messages, int count)
    {
        var result = new List<ChatMessage>(count);
        for (var i = 0; i < count; i++)
            result.Add(messages[i]);
        return result;
    }

    private static ChatMessage BuildMessage(ChatMessage original, List<AIContent> contents)
        => new(original.Role, contents)
        {
            AuthorName = original.AuthorName,
            MessageId = original.MessageId,
            CreatedAt = original.CreatedAt,
            RawRepresentation = original.RawRepresentation,
            AdditionalProperties = original.AdditionalProperties
        };
}

/// <summary>
/// Approval request sent as the "request" parameter of the request_approval tool call.
/// </summary>
public sealed class ApprovalRequest
{
    [JsonPropertyName("approval_id")]
    public required string ApprovalId { get; init; }

    [JsonPropertyName("function_name")]
    public required string FunctionName { get; init; }

    [JsonPropertyName("function_arguments")]
    public IDictionary<string, object?>? FunctionArguments { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// Approval response returned by the frontend as the tool result for request_approval.
/// </summary>
public sealed class ApprovalResponse
{
    [JsonPropertyName("approval_id")]
    public required string ApprovalId { get; init; }

    [JsonPropertyName("approved")]
    public required bool Approved { get; init; }
}

[JsonSerializable(typeof(ApprovalRequest))]
[JsonSerializable(typeof(ApprovalResponse))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
public sealed partial class ApprovalJsonContext : JsonSerializerContext;
