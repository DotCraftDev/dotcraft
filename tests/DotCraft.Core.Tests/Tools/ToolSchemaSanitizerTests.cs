using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Tools;

public sealed class ToolSchemaSanitizerTests
{
    [Fact]
    public void SanitizeTool_RewritesNullableStringAndRemovesNullDefault()
    {
        var function = AIFunctionFactory.Create(NullableStringTool);
        var sanitized = Assert.IsType<ToolSchemaSanitizingFunction>(ToolSchemaSanitizer.SanitizeTool(function));

        var optional = GetPropertySchema(sanitized.JsonSchema, "optional");

        Assert.Equal(JsonValueKind.String, optional.GetProperty("type").ValueKind);
        Assert.Equal("string", optional.GetProperty("type").GetString());
        Assert.False(optional.TryGetProperty("default", out _));
        Assert.Equal("Optional nullable string.", optional.GetProperty("description").GetString());
    }

    [Fact]
    public void SanitizeTool_LeavesRequiredAndNonNullableStringsUsable()
    {
        var function = AIFunctionFactory.Create(NullableStringTool);
        var sanitized = Assert.IsType<ToolSchemaSanitizingFunction>(ToolSchemaSanitizer.SanitizeTool(function));

        var required = GetPropertySchema(sanitized.JsonSchema, "required");
        var nonNullableDefault = GetPropertySchema(sanitized.JsonSchema, "nonNullableDefault");

        Assert.Equal("string", required.GetProperty("type").GetString());
        Assert.Equal("string", nonNullableDefault.GetProperty("type").GetString());
        Assert.Equal("", nonNullableDefault.GetProperty("default").GetString());
    }

    [Fact]
    public void SanitizeTool_DoesNotRewriteNullableNonStringTypes()
    {
        var function = AIFunctionFactory.Create(NullableStringTool);
        var sanitized = Assert.IsType<ToolSchemaSanitizingFunction>(ToolSchemaSanitizer.SanitizeTool(function));

        var count = GetPropertySchema(sanitized.JsonSchema, "count");
        var enabled = GetPropertySchema(sanitized.JsonSchema, "enabled");

        Assert.Equal(JsonValueKind.Array, count.GetProperty("type").ValueKind);
        Assert.Contains("integer", EnumerateTypeValues(count));
        Assert.Contains("null", EnumerateTypeValues(count));
        Assert.True(count.TryGetProperty("default", out var countDefault));
        Assert.Equal(JsonValueKind.Null, countDefault.ValueKind);

        Assert.Equal(JsonValueKind.Array, enabled.GetProperty("type").ValueKind);
        Assert.Contains("boolean", EnumerateTypeValues(enabled));
        Assert.Contains("null", EnumerateTypeValues(enabled));
    }

    [Fact]
    public async Task SanitizedFunction_InvokesInnerFunction()
    {
        var function = AIFunctionFactory.Create(NullableStringTool);
        var sanitized = Assert.IsType<ToolSchemaSanitizingFunction>(ToolSchemaSanitizer.SanitizeTool(function));

        var result = await sanitized.InvokeAsync(new AIFunctionArguments
        {
            ["required"] = "hello",
            ["optional"] = "world"
        });

        var json = Assert.IsType<JsonElement>(result);
        Assert.Equal("hello:world", json.GetString());
    }

    [Fact]
    public void SanitizeTool_IsIdempotent()
    {
        var function = AIFunctionFactory.Create(NullableStringTool);
        var sanitized = ToolSchemaSanitizer.SanitizeTool(function);

        Assert.Same(sanitized, ToolSchemaSanitizer.SanitizeTool(sanitized));
    }

    [Fact]
    public void DeferredRegistry_CanStoreSanitizedDeferredTools()
    {
        var rawTool = AIFunctionFactory.Create(NullableStringTool, name: "NullableStringTool");
        var sanitizedTools = ToolSchemaSanitizer.SanitizeTools([rawTool]);
        var registry = new DeferredToolRegistry(sanitizedTools);

        var results = registry.SearchAndActivate("NullableStringTool");

        Assert.Single(results);
        var activated = Assert.IsType<ToolSchemaSanitizingFunction>(Assert.Single(registry.ActivatedToolsList));
        Assert.Equal("string", GetPropertySchema(activated.JsonSchema, "optional").GetProperty("type").GetString());
    }

    [Fact]
    public async Task DynamicToolInjection_InjectsSanitizedActivatedTools()
    {
        var rawTool = AIFunctionFactory.Create(NullableStringTool, name: "NullableStringTool");
        var registry = new DeferredToolRegistry(ToolSchemaSanitizer.SanitizeTools([rawTool]));
        registry.SearchAndActivate("NullableStringTool");

        var inner = new CapturingChatClient();
        var client = new DynamicToolInjectionChatClient(inner, registry);

        await foreach (var _ in client.GetStreamingResponseAsync(
                           [new ChatMessage(ChatRole.User, "hello")],
                           new ChatOptions()))
        {
        }

        var options = Assert.Single(inner.Options);
        var tool = Assert.IsType<ToolSchemaSanitizingFunction>(Assert.Single(options?.Tools ?? []));
        Assert.Equal("string", GetPropertySchema(tool.JsonSchema, "optional").GetProperty("type").GetString());
    }

    private static string NullableStringTool(
        [System.ComponentModel.Description("Required string.")] string required,
        [System.ComponentModel.Description("Optional nullable string.")] string? optional = null,
        [System.ComponentModel.Description("Non-nullable string with default.")] string nonNullableDefault = "",
        int? count = null,
        bool? enabled = null) =>
        $"{required}:{optional ?? "null"}";

    private static JsonElement GetPropertySchema(JsonElement schema, string propertyName) =>
        schema.GetProperty("properties").GetProperty(propertyName);

    private static string[] EnumerateTypeValues(JsonElement schema) =>
        [.. schema.GetProperty("type").EnumerateArray().Select(value => value.GetString() ?? string.Empty)];

    private sealed class CapturingChatClient : IChatClient
    {
        public List<ChatOptions?> Options { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, "done")]));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> chatMessages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Options.Add(options);
            yield return new ChatResponseUpdate(ChatRole.Assistant, "done");
            await Task.CompletedTask;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
