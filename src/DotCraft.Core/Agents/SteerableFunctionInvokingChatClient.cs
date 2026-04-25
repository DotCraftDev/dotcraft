// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses the referenced Microsoft.Extensions.AI source to you under the MIT license.
// DotCraft adaptation: owns a compact streaming tool loop so same-turn guidance can be inserted at safe boundaries.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

#pragma warning disable MEAI001 // Mirrors upstream FunctionInvokingChatClient handling for provider-managed continuations.

namespace DotCraft.Agents;

/// <summary>
/// DotCraft-owned function invocation loop with a safe-boundary hook for
/// same-turn guidance injection.
/// </summary>
public sealed class SteerableFunctionInvokingChatClient(IChatClient innerClient, IServiceProvider? services = null)
    : DelegatingChatClient(innerClient)
{
    private static readonly AsyncLocal<FunctionInvocationContext?> CurrentInvocationContext = new();

    /// <summary>
    /// Gets the function invocation context currently flowing through this client.
    /// </summary>
    public static FunctionInvocationContext? CurrentContext => CurrentInvocationContext.Value;

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
    /// Includes raw exception messages in generated function result content.
    /// </summary>
    public bool IncludeDetailedErrors { get; set; }

    /// <summary>
    /// Maximum number of model/tool rounds to run for one request.
    /// </summary>
    public int MaximumIterationsPerRequest
    {
        get;
        set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), "Maximum iterations must be at least one.");

            field = value;
        }
    } = 40;

    /// <summary>
    /// Maximum consecutive function-call iterations allowed to fail before the
    /// original exception is rethrown.
    /// </summary>
    public int MaximumConsecutiveErrorsPerRequest
    {
        get;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Maximum consecutive errors cannot be negative.");

            field = value;
        }
    } = 3;

    /// <summary>
    /// Terminates the loop when a requested function is not available locally.
    /// </summary>
    public bool TerminateOnUnknownCalls { get; set; }

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
        List<ChatMessage>? augmentedHistory = null;
        List<ChatMessage>? responseMessages = null;
        var consecutiveErrorCount = 0;
        var lastIterationHadConversationId = false;
        var toolMessageId = Guid.NewGuid().ToString("N");

        for (var iteration = 0; ; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (iteration >= MaximumIterationsPerRequest)
                PrepareOptionsForLastIteration(ref options);

            var updates = new List<ChatResponseUpdate>();
            var functionCalls = new List<FunctionCallContent>();
            var lastYieldedUpdateIndex = 0;

            await foreach (var update in base.GetStreamingResponseAsync(currentMessages, options, cancellationToken))
            {
                updates.Add(update);
                CopyFunctionCalls(update.Contents, functionCalls);

                if (functionCalls.Count == 0)
                {
                    lastYieldedUpdateIndex++;
                    yield return update;
                }
            }

            MarkServerHandledFunctionCalls(updates, functionCalls);

            for (; lastYieldedUpdateIndex < updates.Count; lastYieldedUpdateIndex++)
            {
                var update = updates[lastYieldedUpdateIndex];
                yield return update;
            }

            if (updates.Count == 0)
                yield break;

            var response = updates.ToChatResponse();
            (responseMessages ??= []).AddRange(response.Messages);

            if (iteration >= MaximumIterationsPerRequest || ShouldTerminateLoopBasedOnHandleableFunctions(functionCalls, options))
            {
                FixupHistories(
                    originalMessages,
                    ref currentMessages,
                    ref augmentedHistory,
                    response,
                    responseMessages,
                    ref lastIterationHadConversationId);

                var history = augmentedHistory ?? throw new InvalidOperationException("Augmented history was not initialized.");
                if (await TryAppendGuidanceAsync(history, cancellationToken))
                {
                    currentMessages = history;
                    UpdateOptionsForNextIteration(ref options, response.ConversationId);
                    continue;
                }

                yield break;
            }

            FixupHistories(
                originalMessages,
                ref currentMessages,
                ref augmentedHistory,
                response,
                responseMessages,
                ref lastIterationHadConversationId);
            var nextHistory = augmentedHistory ?? throw new InvalidOperationException("Augmented history was not initialized.");

            var toolMessages = await InvokeFunctionsAsync(
                nextHistory,
                options,
                functionCalls,
                iteration,
                consecutiveErrorCount,
                cancellationToken);

            var anyTerminated = false;
            foreach (var message in toolMessages.Messages)
            {
                nextHistory.Add(message);
                responseMessages.Add(message);
                yield return new ChatResponseUpdate
                {
                    Role = message.Role,
                    Contents = message.Contents,
                    MessageId = message.MessageId ?? toolMessageId,
                    ResponseId = message.MessageId ?? toolMessageId,
                    ConversationId = response.ConversationId,
                    CreatedAt = DateTimeOffset.UtcNow,
                    AdditionalProperties = message.AdditionalProperties
                };
            }

            consecutiveErrorCount = toolMessages.ConsecutiveErrorCount;
            anyTerminated = toolMessages.ShouldTerminate;

            if (anyTerminated)
                yield break;

            await TryAppendGuidanceAsync(nextHistory, cancellationToken);
            UpdateOptionsForNextIteration(ref options, response.ConversationId);
            currentMessages = nextHistory;
        }
    }

    private static void FixupHistories(
        IEnumerable<ChatMessage> originalMessages,
        ref IEnumerable<ChatMessage> currentMessages,
        ref List<ChatMessage>? augmentedHistory,
        ChatResponse response,
        List<ChatMessage> allTurnsResponseMessages,
        ref bool lastIterationHadConversationId)
    {
        if (response.ConversationId is not null)
        {
            (augmentedHistory ??= []).Clear();
            lastIterationHadConversationId = true;
        }
        else if (lastIterationHadConversationId)
        {
            augmentedHistory ??= [];
            augmentedHistory.Clear();
            augmentedHistory.AddRange(originalMessages);
            augmentedHistory.AddRange(allTurnsResponseMessages);
            lastIterationHadConversationId = false;
        }
        else
        {
            augmentedHistory ??= originalMessages.ToList();
            augmentedHistory.AddMessages(response);
            lastIterationHadConversationId = false;
        }

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

    private static void MarkServerHandledFunctionCalls(List<ChatResponseUpdate> updates, List<FunctionCallContent> functionCalls)
    {
        if (functionCalls.Count == 0)
            return;

        HashSet<string>? resultCallIds = null;
        foreach (var update in updates)
        {
            foreach (var content in update.Contents)
            {
                if (content is FunctionResultContent result)
                    (resultCallIds ??= []).Add(result.CallId);
            }
        }

        if (resultCallIds == null)
            return;

        for (var i = functionCalls.Count - 1; i >= 0; i--)
        {
            if (!resultCallIds.Contains(functionCalls[i].CallId))
                continue;

            functionCalls[i].InformationalOnly = true;
            functionCalls.RemoveAt(i);
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

    private bool ShouldTerminateLoopBasedOnHandleableFunctions(List<FunctionCallContent> functionCalls, ChatOptions? options)
    {
        if (functionCalls.Count == 0)
            return true;

        if (!HasAnyTools(options?.Tools, AdditionalTools))
            return TerminateOnUnknownCalls;

        foreach (var call in functionCalls)
        {
            var tool = FindToolDeclaration(call.Name, options);
            if (tool is not null)
            {
                if (tool is not AIFunction)
                    return true;
            }
            else if (TerminateOnUnknownCalls)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<FunctionInvocationBatch> InvokeFunctionsAsync(
        List<ChatMessage> messages,
        ChatOptions? options,
        List<FunctionCallContent> functionCalls,
        int iteration,
        int consecutiveErrorCount,
        CancellationToken cancellationToken)
    {
        var captureExceptions = consecutiveErrorCount < MaximumConsecutiveErrorsPerRequest;
        var results = AllowConcurrentInvocation && functionCalls.Count > 1
            ? await Task.WhenAll(functionCalls.Select((call, index) => InvokeFunctionAsync(
                messages,
                options,
                call,
                iteration,
                index,
                functionCalls.Count,
                captureExceptions,
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
        var exceptions = new List<Exception>();
        var anyException = false;

        foreach (var result in results)
        {
            shouldTerminate |= result.ShouldTerminate;
            result.Call.InformationalOnly = true;

            var content = CreateFunctionResultContent(result);
            contents.Add(content);

            if (content.Exception != null)
            {
                anyException = true;
                exceptions.Add(content.Exception);
            }
        }

        if (anyException)
        {
            consecutiveErrorCount++;
            if (consecutiveErrorCount > MaximumConsecutiveErrorsPerRequest)
                ThrowFunctionExceptions(exceptions);
        }
        else
        {
            consecutiveErrorCount = 0;
        }

        var messageId = Guid.NewGuid().ToString("N");
        return new FunctionInvocationBatch(
            contents.Count == 0 ? [] : [new ChatMessage(ChatRole.Tool, contents) { MessageId = messageId }],
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
                captureExceptions: consecutiveErrorCount < MaximumConsecutiveErrorsPerRequest,
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
            return new FunctionInvocationOutcome(call, FunctionInvocationStatus.NotFound, null, null, false);

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

        var previousContext = CurrentInvocationContext.Value;
        try
        {
            CurrentInvocationContext.Value = context;
            var value = FunctionInvoker == null
                ? await function.InvokeAsync(arguments, cancellationToken)
                : await FunctionInvoker(context, cancellationToken);
            return new FunctionInvocationOutcome(call, FunctionInvocationStatus.RanToCompletion, value, null, context.Terminate);
        }
        catch (Exception ex) when (captureExceptions && ex is not OperationCanceledException)
        {
            return new FunctionInvocationOutcome(call, FunctionInvocationStatus.Exception, null, ex, false);
        }
        finally
        {
            CurrentInvocationContext.Value = previousContext;
        }
    }

    private FunctionResultContent CreateFunctionResultContent(FunctionInvocationOutcome result)
    {
        if (result.Status == FunctionInvocationStatus.RanToCompletion)
        {
            if (result.Value is FunctionResultContent content && content.CallId == result.Call.CallId)
                return content;

            return new FunctionResultContent(result.Call.CallId, result.Value ?? "Success: Function completed.");
        }

        var message = result.Status switch
        {
            FunctionInvocationStatus.NotFound => $"Error: Requested function \"{result.Call.Name}\" not found.",
            FunctionInvocationStatus.Exception => "Error: Function failed.",
            _ => "Error: Unknown error."
        };

        if (IncludeDetailedErrors && result.Status == FunctionInvocationStatus.Exception && result.Exception != null)
            message = $"{message} Exception: {result.Exception.Message}";

        return new FunctionResultContent(result.Call.CallId, message)
        {
            Exception = result.Exception
        };
    }

    private AITool? FindTool(string name, ChatOptions? options)
    {
        static AITool? FindIn(IEnumerable<AITool>? tools, string toolName) =>
            tools?.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.Ordinal));

        return FindIn(options?.Tools, name) ?? FindIn(AdditionalTools, name);
    }

    private AIFunctionDeclaration? FindToolDeclaration(string name, ChatOptions? options)
    {
        static AIFunctionDeclaration? FindIn(IEnumerable<AITool>? tools, string toolName) =>
            tools?.OfType<AIFunctionDeclaration>().FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.Ordinal));

        return FindIn(options?.Tools, name) ?? FindIn(AdditionalTools, name);
    }

    private static bool HasAnyTools(params IList<AITool>?[] toolLists) =>
        toolLists.Any(tools => tools is { Count: > 0 });

    private static void PrepareOptionsForLastIteration(ref ChatOptions? options)
    {
        if (options?.Tools is not { Count: > 0 })
            return;

        List<AITool>? remainingTools = null;
        foreach (var tool in options.Tools)
        {
            if (tool is not AIFunctionDeclaration)
                (remainingTools ??= []).Add(tool);
        }

        var remainingCount = remainingTools?.Count ?? 0;
        if (remainingCount >= options.Tools.Count)
            return;

        options = options.Clone();
        options.Tools = remainingTools;
        if (remainingCount == 0)
            options.ToolMode = null;
    }

    private static void UpdateOptionsForNextIteration(ref ChatOptions? options, string? conversationId)
    {
        if (options == null)
        {
            if (conversationId != null)
                options = new ChatOptions { ConversationId = conversationId };
        }
        else if (options.ToolMode is RequiredChatToolMode)
        {
            options = options.Clone();
            options.ToolMode = null;
            options.ConversationId = conversationId;
        }
        else if (options.ConversationId != conversationId)
        {
            options = options.Clone();
            options.ConversationId = conversationId;
        }
        else if (options.ContinuationToken != null)
        {
            options = options.Clone();
        }

        if (options?.ContinuationToken != null)
            options.ContinuationToken = null;
    }

    private static void ThrowFunctionExceptions(List<Exception> exceptions)
    {
        if (exceptions.Count == 1)
            ExceptionDispatchInfo.Capture(exceptions[0]).Throw();

        throw new AggregateException(exceptions);
    }

    private sealed record FunctionInvocationBatch(
        IReadOnlyList<ChatMessage> Messages,
        bool ShouldTerminate,
        int ConsecutiveErrorCount);

    private sealed record FunctionInvocationOutcome(
        FunctionCallContent Call,
        FunctionInvocationStatus Status,
        object? Value,
        Exception? Exception,
        bool ShouldTerminate);

    private enum FunctionInvocationStatus
    {
        RanToCompletion,
        NotFound,
        Exception
    }
}
