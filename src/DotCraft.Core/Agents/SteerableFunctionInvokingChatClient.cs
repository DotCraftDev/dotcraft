// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses the referenced Microsoft.Extensions.AI source to you under the MIT license.
// DotCraft adaptation: owns a compact streaming tool loop so same-turn guidance can be inserted at safe boundaries.

using System.Runtime.CompilerServices;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Agents;

/// <summary>
/// DotCraft-owned function invocation loop with a safe-boundary hook for
/// same-turn guidance injection.
/// </summary>
public sealed class SteerableFunctionInvokingChatClient(IChatClient innerClient, IServiceProvider? services = null)
    : DelegatingChatClient(innerClient)
{
    /// <summary>
    /// Extra tools that may be invoked even when they are not sent in the current
    /// request's <see cref="ChatOptions.Tools"/> list.
    /// </summary>
    public IList<AITool>? AdditionalTools { get; set; }

    /// <summary>
    /// Allows multiple tool calls from one model response to run concurrently.
    /// </summary>
    public bool AllowConcurrentInvocation { get; set; }

    /// <summary>
    /// Maximum number of model/tool rounds to run for one request.
    /// </summary>
    public int MaximumIterationsPerRequest { get; set; } = 40;

    /// <summary>
    /// Custom invocation hook matching Microsoft.Extensions.AI's public surface.
    /// </summary>
    public Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>>? FunctionInvoker { get; set; }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        await GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var originalMessages = messages.ToList();
        var currentMessages = (IEnumerable<ChatMessage>)originalMessages;
        var augmentedHistory = new List<ChatMessage>();
        var consecutiveErrorCount = 0;

        for (var iteration = 0; iteration <= MaximumIterationsPerRequest; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var updates = new List<ChatResponseUpdate>();
            var functionCalls = new List<FunctionCallContent>();
            var serverHandledCallIds = new HashSet<string>(StringComparer.Ordinal);

            await foreach (var update in base.GetStreamingResponseAsync(currentMessages, options, cancellationToken))
            {
                updates.Add(update);
                CopyFunctionCalls(update.Contents, functionCalls);
                CopyFunctionResults(update.Contents, serverHandledCallIds);
                yield return update;
            }

            if (updates.Count == 0)
                yield break;

            var response = updates.ToChatResponse();
            FixupHistory(originalMessages, ref currentMessages, augmentedHistory, response);
            if (serverHandledCallIds.Count > 0)
                functionCalls.RemoveAll(call => serverHandledCallIds.Contains(call.CallId));

            if (functionCalls.Count == 0 || iteration >= MaximumIterationsPerRequest)
            {
                if (await TryAppendGuidanceAsync(augmentedHistory, cancellationToken))
                {
                    currentMessages = augmentedHistory;
                    continue;
                }

                yield break;
            }

            var toolMessages = await InvokeFunctionsAsync(
                augmentedHistory,
                options,
                functionCalls,
                iteration,
                consecutiveErrorCount,
                cancellationToken);

            var anyTerminated = false;
            foreach (var message in toolMessages.Messages)
            {
                augmentedHistory.Add(message);
                yield return new ChatResponseUpdate
                {
                    Role = message.Role,
                    Contents = message.Contents,
                    MessageId = message.MessageId,
                    ConversationId = response.ConversationId,
                    AdditionalProperties = message.AdditionalProperties
                };
            }

            consecutiveErrorCount = toolMessages.ConsecutiveErrorCount;
            anyTerminated = toolMessages.ShouldTerminate;

            if (anyTerminated)
                yield break;

            await TryAppendGuidanceAsync(augmentedHistory, cancellationToken);
            currentMessages = augmentedHistory;
        }
    }

    private static void FixupHistory(
        IReadOnlyList<ChatMessage> originalMessages,
        ref IEnumerable<ChatMessage> currentMessages,
        List<ChatMessage> augmentedHistory,
        ChatResponse response)
    {
        if (augmentedHistory.Count == 0)
            augmentedHistory.AddRange(originalMessages);

        augmentedHistory.AddMessages(response);
        currentMessages = augmentedHistory;
    }

    private static void CopyFunctionCalls(IList<AIContent> contents, List<FunctionCallContent> calls)
    {
        foreach (var content in contents)
        {
            if (content is FunctionCallContent { InformationalOnly: false } functionCall)
                calls.Add(functionCall);
        }
    }

    private static void CopyFunctionResults(IList<AIContent> contents, HashSet<string> callIds)
    {
        foreach (var content in contents)
        {
            if (content is FunctionResultContent result)
                callIds.Add(result.CallId);
        }
    }

    private async Task<bool> TryAppendGuidanceAsync(List<ChatMessage> augmentedHistory, CancellationToken cancellationToken)
    {
        var context = TurnGuidanceRuntimeScope.Current;
        if (context == null)
            return false;

        var guidanceMessage = await context.TryDrainGuidanceMessageAsync(cancellationToken);
        if (guidanceMessage == null)
            return false;

        augmentedHistory.Add(guidanceMessage);
        return true;
    }

    private async Task<FunctionInvocationBatch> InvokeFunctionsAsync(
        List<ChatMessage> messages,
        ChatOptions? options,
        List<FunctionCallContent> functionCalls,
        int iteration,
        int consecutiveErrorCount,
        CancellationToken cancellationToken)
    {
        var results = AllowConcurrentInvocation && functionCalls.Count > 1
            ? await Task.WhenAll(functionCalls.Select((call, index) => InvokeFunctionAsync(
                messages,
                options,
                call,
                iteration,
                index,
                functionCalls.Count,
                captureExceptions: true,
                cancellationToken)))
            : await InvokeFunctionsSeriallyAsync(
                messages,
                options,
                functionCalls,
                iteration,
                consecutiveErrorCount,
                cancellationToken);

        var contents = new List<AIContent>();
        var shouldTerminate = false;

        foreach (var result in results)
        {
            shouldTerminate |= result.ShouldTerminate;
            if (result.Exception == null)
            {
                consecutiveErrorCount = 0;
            }
            else
            {
                consecutiveErrorCount++;
            }

            contents.Add(new FunctionResultContent(result.Call.CallId, result.Value)
            {
                Exception = result.Exception
            });
        }

        return new FunctionInvocationBatch(
            contents.Count == 0 ? [] : [new ChatMessage(ChatRole.Tool, contents)],
            shouldTerminate,
            consecutiveErrorCount);
    }

    private async Task<FunctionInvocationOutcome[]> InvokeFunctionsSeriallyAsync(
        List<ChatMessage> messages,
        ChatOptions? options,
        List<FunctionCallContent> functionCalls,
        int iteration,
        int consecutiveErrorCount,
        CancellationToken cancellationToken)
    {
        var outcomes = new List<FunctionInvocationOutcome>(functionCalls.Count);
        for (var index = 0; index < functionCalls.Count; index++)
        {
            var outcome = await InvokeFunctionAsync(
                messages,
                options,
                functionCalls[index],
                iteration,
                index,
                functionCalls.Count,
                captureExceptions: consecutiveErrorCount > 0,
                cancellationToken);
            outcomes.Add(outcome);
            if (outcome.ShouldTerminate)
                break;
        }

        return outcomes.ToArray();
    }

    private async Task<FunctionInvocationOutcome> InvokeFunctionAsync(
        List<ChatMessage> messages,
        ChatOptions? options,
        FunctionCallContent call,
        int iteration,
        int index,
        int count,
        bool captureExceptions,
        CancellationToken cancellationToken)
    {
        var tool = FindTool(call.Name, options);
        if (tool is not AIFunction function)
            return new FunctionInvocationOutcome(call, $"Tool '{call.Name}' was not found.", null, false);

        var arguments = new AIFunctionArguments(call.Arguments)
        {
            Services = services
        };
        var context = new FunctionInvocationContext
        {
            Function = function,
            Arguments = arguments,
            CallContent = call,
            Messages = messages,
            Options = options,
            Iteration = iteration + 1,
            FunctionCallIndex = index,
            FunctionCount = count,
            IsStreaming = true
        };

        try
        {
            var value = FunctionInvoker == null
                ? await function.InvokeAsync(arguments, cancellationToken)
                : await FunctionInvoker(context, cancellationToken);
            return new FunctionInvocationOutcome(call, value, null, context.Terminate);
        }
        catch (Exception ex) when (captureExceptions && ex is not OperationCanceledException)
        {
            return new FunctionInvocationOutcome(call, ex.Message, ex, false);
        }
    }

    private AITool? FindTool(string name, ChatOptions? options)
    {
        static AITool? FindIn(IEnumerable<AITool>? tools, string toolName) =>
            tools?.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.Ordinal));

        return FindIn(options?.Tools, name) ?? FindIn(AdditionalTools, name);
    }

    private sealed record FunctionInvocationBatch(
        IReadOnlyList<ChatMessage> Messages,
        bool ShouldTerminate,
        int ConsecutiveErrorCount);

    private sealed record FunctionInvocationOutcome(
        FunctionCallContent Call,
        object? Value,
        Exception? Exception,
        bool ShouldTerminate);
}
