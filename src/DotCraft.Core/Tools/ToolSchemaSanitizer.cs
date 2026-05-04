using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Plugins;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// Normalizes tool JSON schemas for providers with narrower schema support.
/// </summary>
internal static class ToolSchemaSanitizer
{
    public static List<AITool> SanitizeTools(IEnumerable<AITool> tools) =>
        [.. tools.Select(SanitizeTool)];

    public static AITool SanitizeTool(AITool tool) =>
        tool switch
        {
            ToolSchemaSanitizingFunction => tool,
            AIFunction function => new ToolSchemaSanitizingFunction(function),
            _ => tool
        };

    public static JsonElement SanitizeJsonSchema(JsonElement schema)
    {
        var node = JsonNode.Parse(schema.GetRawText()) ?? new JsonObject();
        SanitizeNode(node);
        return JsonSerializer.SerializeToElement(node);
    }

    private static void SanitizeNode(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            // Some LLM providers do not support nullable string parameters, so the application filters parameter schemas before sending them.
            if (TryNormalizeNullableStringType(obj) &&
                obj.TryGetPropertyValue("default", out var defaultNode) &&
                defaultNode is null)
            {
                obj.Remove("default");
            }

            foreach (var property in obj.ToArray())
                SanitizeNode(property.Value);
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array.ToArray())
                SanitizeNode(item);
        }
    }

    private static bool TryNormalizeNullableStringType(JsonObject obj)
    {
        if (!obj.TryGetPropertyValue("type", out var typeNode) || typeNode is not JsonArray typeArray)
            return false;

        var values = typeArray
            .Select(item => item?.GetValue<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (values.Length != 2 ||
            !values.Contains("string", StringComparer.OrdinalIgnoreCase) ||
            !values.Contains("null", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        obj["type"] = "string";
        return true;
    }
}

/// <summary>
/// Wraps an <see cref="AIFunction"/> while exposing a provider-compatible input schema.
/// </summary>
internal sealed class ToolSchemaSanitizingFunction(AIFunction innerFunction)
    : DelegatingAIFunction(innerFunction), IPluginFunctionTool
{
    private readonly JsonElement _jsonSchema = ToolSchemaSanitizer.SanitizeJsonSchema(innerFunction.JsonSchema);

    public override JsonElement JsonSchema => _jsonSchema;

    public PluginFunctionDescriptor? PluginFunctionDescriptor =>
        InnerFunction is IPluginFunctionTool pluginFunction
            ? pluginFunction.PluginFunctionDescriptor
            : null;
}
