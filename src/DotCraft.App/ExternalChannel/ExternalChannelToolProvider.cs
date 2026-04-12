using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.ExternalChannel;

internal sealed class ExternalChannelToolProvider(IChannelRuntimeRegistry registry) : IChannelRuntimeToolProvider
{
    public IReadOnlyList<AITool> CreateToolsForThread(
        SessionThread thread,
        IReadOnlySet<string> reservedToolNames)
    {
        if (string.IsNullOrWhiteSpace(thread.OriginChannel))
            return [];

        if (!registry.TryGet(thread.OriginChannel, out var runtime) || runtime == null)
            return [];

        var descriptors = FinalizeRuntimeRegistration(runtime, reservedToolNames);
        if (descriptors.Count == 0)
            return [];

        return CreateRuntimeTools(runtime, descriptors);
    }

    private static List<ChannelToolDescriptor> FinalizeRuntimeRegistration(
        IChannelRuntime runtime,
        IReadOnlySet<string> reservedToolNames)
    {
        var diagnostics = new List<ChannelToolRegistrationDiagnostic>();
        var registered = new List<ChannelToolDescriptor>();

        foreach (var descriptor in runtime.GetChannelTools())
        {
            if (reservedToolNames.Contains(descriptor.Name))
            {
                diagnostics.Add(new ChannelToolRegistrationDiagnostic
                {
                    ToolName = descriptor.Name,
                    Code = "ChannelToolNameConflict",
                    Message = $"Tool '{descriptor.Name}' conflicts with an existing runtime tool."
                });
                continue;
            }

            if (!TryValidateDescriptor(descriptor, out var message))
            {
                diagnostics.Add(new ChannelToolRegistrationDiagnostic
                {
                    ToolName = descriptor.Name,
                    Code = "InvalidChannelToolDescriptor",
                    Message = message
                });
                continue;
            }

            RegisterToolDisplay(descriptor);
            registered.Add(descriptor);
        }

        if (runtime is ExternalChannelHost host && host.AdapterConnection != null)
            host.AdapterConnection.SetChannelToolRegistration(registered, diagnostics);

        return registered;
    }

    private static void RegisterToolDisplay(ChannelToolDescriptor descriptor)
    {
        if (descriptor.Display != null)
        {
            ToolRegistry.RegisterDisplay(
                descriptor.Name,
                title: descriptor.Display.Title,
                subtitle: descriptor.Display.Subtitle,
                icon: descriptor.Display.Icon);
        }
    }

    private static IReadOnlyList<AITool> CreateRuntimeTools(
        IChannelRuntime runtime,
        IReadOnlyList<ChannelToolDescriptor> registeredTools)
        => registeredTools
            .Select(descriptor => (AITool)new ExternalChannelRuntimeFunction(runtime, descriptor))
            .ToArray();

    private static bool TryValidateDescriptor(ChannelToolDescriptor descriptor, out string message)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Name))
        {
            message = "Tool name is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(descriptor.Description))
        {
            message = $"Tool '{descriptor.Name}' must declare a description.";
            return false;
        }

        if (descriptor.InputSchema == null)
        {
            message = $"Tool '{descriptor.Name}' must declare inputSchema.";
            return false;
        }

        if (!ChannelToolSchemaValidator.TryValidateSchema(descriptor.InputSchema, out message))
        {
            message = $"Tool '{descriptor.Name}' has an invalid inputSchema: {message}";
            return false;
        }

        if (descriptor.OutputSchema != null
            && !ChannelToolSchemaValidator.TryValidateSchema(descriptor.OutputSchema, out message))
        {
            message = $"Tool '{descriptor.Name}' has an invalid outputSchema: {message}";
            return false;
        }

        message = string.Empty;
        return true;
    }
}

internal sealed class ExternalChannelRuntimeFunction : AIFunction
{
    private readonly IChannelRuntime _runtime;
    private readonly ChannelToolDescriptor _descriptor;
    private readonly JsonElement _jsonSchema;
    private readonly JsonElement? _returnJsonSchema;

    public ExternalChannelRuntimeFunction(IChannelRuntime runtime, ChannelToolDescriptor descriptor)
    {
        _runtime = runtime;
        _descriptor = descriptor;
        _jsonSchema = ToJsonElement(descriptor.InputSchema ?? new JsonObject());
        _returnJsonSchema = descriptor.OutputSchema != null ? ToJsonElement(descriptor.OutputSchema) : null;
    }

    public override string Name => _descriptor.Name;

    public override string Description => _descriptor.Description;

    public override JsonElement JsonSchema => _jsonSchema;

    public override JsonElement? ReturnJsonSchema => _returnJsonSchema;

    public override MethodInfo? UnderlyingMethod => null;

    public override JsonSerializerOptions JsonSerializerOptions => SessionWireJsonOptions.Default;

    protected override async ValueTask<object?> InvokeCoreAsync(
        AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        var scope = ExternalChannelToolExecutionScope.Current
            ?? throw new InvalidOperationException("External channel tools require an active turn scope.");

        var callId = $"exttool_{Guid.NewGuid():N}";
        var argsObject = ToJsonObject(arguments);
        var item = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(scope.NextItemSequence()),
            TurnId = scope.TurnId,
            Type = ItemType.ExternalChannelToolCall,
            Status = ItemStatus.Started,
            CreatedAt = DateTimeOffset.UtcNow,
            Payload = new ExternalChannelToolCallPayload
            {
                ToolName = _descriptor.Name,
                CallId = callId,
                ChannelName = _runtime.Name,
                RequiresChatContext = _descriptor.RequiresChatContext,
                Arguments = argsObject.DeepClone() as JsonObject
            }
        };
        scope.Turn.Items.Add(item);
        scope.EmitItemStarted(item);

        if (!ChannelToolSchemaValidator.TryValidateArguments(_descriptor.InputSchema ?? new JsonObject(), argsObject, out var validationError))
        {
            return FinalizeFailure(item, scope, callId, argsObject, "InvalidArguments", validationError);
        }

        if (_descriptor.RequiresChatContext
            && string.IsNullOrWhiteSpace(scope.ChannelContext)
            && string.IsNullOrWhiteSpace(scope.GroupId))
        {
            return FinalizeFailure(
                item,
                scope,
                callId,
                argsObject,
                "MissingChatContext",
                $"Tool '{_descriptor.Name}' requires channel chat context, but this turn does not have one.");
        }

        ExtChannelToolCallResult result;
        try
        {
            result = await _runtime.ExecuteToolAsync(
                new ExtChannelToolCallParams
                {
                    ThreadId = scope.ThreadId,
                    TurnId = scope.TurnId,
                    CallId = callId,
                    Tool = _descriptor.Name,
                    Arguments = argsObject,
                    Context = new ExtChannelToolCallContext
                    {
                        ChannelName = scope.OriginChannel,
                        ChannelContext = scope.ChannelContext,
                        SenderId = scope.SenderId,
                        GroupId = scope.GroupId
                    }
                },
                cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = new ExtChannelToolCallResult
            {
                Success = false,
                ErrorCode = "ExternalChannelToolTimeout",
                ErrorMessage = $"Tool '{_descriptor.Name}' timed out while waiting for adapter response."
            };
        }

        var resultText = FormatResultText(result);
        item.Status = ItemStatus.Completed;
        item.CompletedAt = DateTimeOffset.UtcNow;
        item.Payload = new ExternalChannelToolCallPayload
        {
            ToolName = _descriptor.Name,
            CallId = callId,
            ChannelName = _runtime.Name,
            RequiresChatContext = _descriptor.RequiresChatContext,
            Arguments = argsObject.DeepClone() as JsonObject,
            Result = resultText,
            Success = result.Success,
            ErrorCode = result.ErrorCode,
            ErrorMessage = result.ErrorMessage
        };
        scope.EmitItemCompleted(item);

        return MapToolResultToModelValue(result, resultText);
    }

    private static object MapToolResultToModelValue(ExtChannelToolCallResult result, string resultText)
    {
        if (result.ContentItems is { Count: > 0 } contentItems)
        {
            var aiContents = new List<AIContent>();
            foreach (var item in contentItems)
            {
                if (string.Equals(item.Type, "text", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.Text))
                {
                    aiContents.Add(new TextContent(item.Text));
                    continue;
                }

                if (string.Equals(item.Type, "image", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.DataBase64)
                    && !string.IsNullOrWhiteSpace(item.MediaType))
                {
                    try
                    {
                        aiContents.Add(new DataContent(Convert.FromBase64String(item.DataBase64), item.MediaType));
                    }
                    catch (FormatException)
                    {
                        // Fall back to the textual summary below.
                    }
                }
            }

            if (aiContents.Count > 0)
                return aiContents;
        }

        if (result.StructuredResult != null)
        {
            return new
            {
                success = result.Success,
                contentItems = result.ContentItems,
                structuredResult = result.StructuredResult,
                errorCode = result.ErrorCode,
                errorMessage = result.ErrorMessage
            };
        }

        return resultText;
    }

    private static string FormatResultText(ExtChannelToolCallResult result)
    {
        if (!result.Success)
        {
            var error = result.ErrorMessage ?? "Adapter tool call failed.";
            if (!string.IsNullOrWhiteSpace(result.ErrorCode))
                return $"{result.ErrorCode}: {error}";
            return error;
        }

        var lines = new List<string>();
        if (result.ContentItems is { Count: > 0 })
        {
            foreach (var item in result.ContentItems)
            {
                if (string.Equals(item.Type, "text", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.Text))
                {
                    lines.Add(item.Text);
                }
                else if (string.Equals(item.Type, "image", StringComparison.OrdinalIgnoreCase))
                {
                    lines.Add("[image]");
                }
            }
        }

        if (result.StructuredResult != null)
            lines.Add(result.StructuredResult.ToJsonString());

        return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "Tool completed.";
    }

    private static string CreateFailureResult(string errorCode, string errorMessage) =>
        JsonSerializer.Serialize(
            new
            {
                success = false,
                errorCode,
                errorMessage
            },
            SessionWireJsonOptions.Default);

    private string FinalizeFailure(
        SessionItem item,
        ExternalChannelToolExecutionContext scope,
        string callId,
        JsonObject argsObject,
        string errorCode,
        string errorMessage)
    {
        item.Status = ItemStatus.Completed;
        item.CompletedAt = DateTimeOffset.UtcNow;
        item.Payload = new ExternalChannelToolCallPayload
        {
            ToolName = _descriptor.Name,
            CallId = callId,
            ChannelName = _runtime.Name,
            RequiresChatContext = _descriptor.RequiresChatContext,
            Arguments = argsObject.DeepClone() as JsonObject,
            Result = errorMessage,
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
        scope.EmitItemCompleted(item);
        return CreateFailureResult(errorCode, errorMessage);
    }

    private static JsonObject ToJsonObject(AIFunctionArguments arguments)
    {
        var root = new JsonObject();
        foreach (var (key, value) in arguments)
            root[key] = value is JsonNode node ? node.DeepClone() : JsonSerializer.SerializeToNode(value, SessionWireJsonOptions.Default);
        return root;
    }

    private static JsonElement ToJsonElement(JsonNode node)
        => JsonSerializer.Deserialize<JsonElement>(node.ToJsonString(SessionWireJsonOptions.Default), SessionWireJsonOptions.Default);
}

internal static class ChannelToolSchemaValidator
{
    public static bool TryValidateSchema(JsonObject schema, out string message)
        => TryValidateSchemaNode(schema, "$", out message);

    public static bool TryValidateArguments(JsonObject schema, JsonObject arguments, out string message)
        => TryValidateValue(schema, arguments, "$", out message);

    private static bool TryValidateSchemaNode(JsonNode? schemaNode, string path, out string message)
    {
        if (schemaNode is not JsonObject schema)
        {
            message = $"{path} must be an object.";
            return false;
        }

        var type = schema["type"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(type))
        {
            message = $"{path}.type is required.";
            return false;
        }

        switch (type)
        {
            case "object":
                if (schema["properties"] is JsonNode propertiesNode
                    && propertiesNode is not JsonObject)
                {
                    message = $"{path}.properties must be an object.";
                    return false;
                }

                if (schema["required"] is JsonNode requiredNode
                    && requiredNode is not JsonArray)
                {
                    message = $"{path}.required must be an array.";
                    return false;
                }

                if (schema["required"] is JsonArray required)
                {
                    var props = schema["properties"] as JsonObject;
                    foreach (var item in required)
                    {
                        var name = item?.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            message = $"{path}.required entries must be strings.";
                            return false;
                        }

                        if (props != null && !props.ContainsKey(name))
                        {
                            message = $"{path}.required references unknown property '{name}'.";
                            return false;
                        }
                    }
                }

                if (schema["properties"] is JsonObject nestedProperties)
                {
                    foreach (var (propertyName, propertySchema) in nestedProperties)
                    {
                        if (!TryValidateSchemaNode(propertySchema, $"{path}.properties.{propertyName}", out message))
                            return false;
                    }
                }

                message = string.Empty;
                return true;

            case "array":
                if (schema["items"] is not JsonNode itemsNode)
                {
                    message = $"{path}.items is required for array schemas.";
                    return false;
                }

                return TryValidateSchemaNode(itemsNode, $"{path}.items", out message);

            case "string":
            case "number":
            case "integer":
            case "boolean":
                message = string.Empty;
                return true;

            default:
                message = $"{path}.type '{type}' is not supported.";
                return false;
        }
    }

    private static bool TryValidateValue(JsonObject schema, JsonNode? value, string path, out string message)
    {
        var type = schema["type"]?.GetValue<string>() ?? "object";
        switch (type)
        {
            case "object":
                if (value is not JsonObject objValue)
                {
                    message = $"{path} must be an object.";
                    return false;
                }

                var properties = schema["properties"] as JsonObject;
                if (schema["required"] is JsonArray required)
                {
                    foreach (var item in required)
                    {
                        var name = item?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(name) && !objValue.ContainsKey(name))
                        {
                            message = $"{path}.{name} is required.";
                            return false;
                        }
                    }
                }

                if (properties != null)
                {
                    foreach (var (propertyName, propertyValue) in objValue)
                    {
                        if (!properties.TryGetPropertyValue(propertyName, out var propertySchema))
                        {
                            message = $"{path}.{propertyName} is not declared by the tool schema.";
                            return false;
                        }

                        if (propertySchema is not JsonObject propertySchemaObject)
                        {
                            message = $"{path}.{propertyName} schema is invalid.";
                            return false;
                        }

                        if (!TryValidateValue(propertySchemaObject, propertyValue, $"{path}.{propertyName}", out message))
                            return false;
                    }
                }

                message = string.Empty;
                return true;

            case "array":
                if (value is not JsonArray arrayValue)
                {
                    message = $"{path} must be an array.";
                    return false;
                }

                if (schema["items"] is not JsonObject itemSchema)
                {
                    message = $"{path} array schema is missing items.";
                    return false;
                }

                for (int i = 0; i < arrayValue.Count; i++)
                {
                    if (!TryValidateValue(itemSchema, arrayValue[i], $"{path}[{i}]", out message))
                        return false;
                }

                message = string.Empty;
                return true;

            case "string":
                if (value == null || value.GetValueKind() != JsonValueKind.String)
                {
                    message = $"{path} must be a string.";
                    return false;
                }

                message = string.Empty;
                return true;

            case "number":
                if (value == null || value.GetValueKind() is not (JsonValueKind.Number))
                {
                    message = $"{path} must be a number.";
                    return false;
                }

                message = string.Empty;
                return true;

            case "integer":
                if (value == null || value.GetValueKind() != JsonValueKind.Number || !value.TryGetValue<long>(out _))
                {
                    message = $"{path} must be an integer.";
                    return false;
                }

                message = string.Empty;
                return true;

            case "boolean":
                if (value == null || value.GetValueKind() is not (JsonValueKind.True or JsonValueKind.False))
                {
                    message = $"{path} must be a boolean.";
                    return false;
                }

                message = string.Empty;
                return true;

            default:
                message = $"{path} uses unsupported schema type '{type}'.";
                return false;
        }
    }

    private static bool TryGetValue<T>(this JsonNode node, out T? value)
    {
        try
        {
            value = node.Deserialize<T>(SessionWireJsonOptions.Default);
            return value != null;
        }
        catch
        {
            value = default;
            return false;
        }
    }
}
