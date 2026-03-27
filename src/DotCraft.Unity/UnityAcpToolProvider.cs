using System.Text.Json;
using DotCraft.Abstractions;
using Microsoft.Extensions.AI;

namespace DotCraft.Unity;

/// <summary>
/// Provides Unity-specific read-only tools that use ACP extension methods (_unity/*).
/// These tools allow the AI agent to understand the Unity project state.
/// For full Unity manipulation capabilities, install SkillsForUnity package.
/// </summary>
public sealed class UnityAcpToolProvider : IAgentToolProvider
{
    /// <summary>
    /// Extension prefix that the client must advertise in <c>ClientCapabilities.Extensions</c>
    /// for these tools to be registered.
    /// </summary>
    private const string ExtensionPrefix = "_unity";

    public int Priority => 150; // After IAgentToolProvider default (100)

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        var proxy = context.AcpExtensionProxy;
        if (proxy == null)
        {
            yield break;
        }

        if (!proxy.Extensions.Any(e => e.Equals(ExtensionPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            yield break;
        }

        // Scene tools (read-only)
        yield return AIFunctionFactory.Create(
            (string? query, bool includeComponents, int maxDepth, CancellationToken ct) =>
                CallExtensionAsync<UnitySceneQueryResult>(proxy, "_unity/scene_query",
                    new { query, includeComponents, maxDepth = maxDepth > 0 ? maxDepth : 10 }, ct),
            name: "unity_scene_query",
            description: "Query Unity scene hierarchy. Returns JSON describing GameObjects and their components. " +
                         "Use 'query' to filter by name, set 'includeComponents' to true for component details, " +
                         "'maxDepth' limits traversal depth (default 10).");

        yield return AIFunctionFactory.Create(
            (CancellationToken ct) =>
                CallExtensionAsync<UnitySelectionResult>(proxy, "_unity/get_selection", null, ct),
            name: "unity_get_selection",
            description: "Get currently selected objects in Unity Editor. Returns array of selected GameObjects with their paths and components.");

        // Console tools (read-only)
        yield return AIFunctionFactory.Create(
            (string[]? types, int limit, CancellationToken ct) =>
                CallExtensionAsync<UnityConsoleLogsResult>(proxy, "_unity/get_console_logs",
                    new { types, limit = limit > 0 ? limit : 50 }, ct),
            name: "unity_get_console_logs",
            description: "Get recent Unity Console log entries. " +
                         "'types' filters by log type: ['error', 'warning', 'log'] (default all). " +
                         "'limit' sets max entries to return (default 50).");

        // Project info (read-only)
        yield return AIFunctionFactory.Create(
            (CancellationToken ct) =>
                CallExtensionAsync<UnityProjectInfoResult>(proxy, "_unity/get_project_info", null, ct),
            name: "unity_get_project_info",
            description: "Get Unity project information including Unity version, project name, and installed packages.");
    }

    // ───── Shared helper ─────

    /// <summary>
    /// Sends an ACP extension request, deserializes the result, and wraps
    /// transport / serialization errors into a JSON error string so that
    /// the LLM can handle failures gracefully instead of seeing a raw exception.
    /// </summary>
    private static async Task<string> CallExtensionAsync<T>(
        IAcpExtensionProxy proxy, string method, object? @params, CancellationToken ct)
    {
        try
        {
            var result = await proxy.SendExtensionAsync<T>(method, @params, ct);
            return JsonSerializer.Serialize(result, JsonOptions);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(
                new { error = $"Unity extension call '{method}' failed: {ex.Message}" },
                JsonOptions);
        }
    }

    #region Result Types

    // These types match the JSON responses from Unity

    public sealed class UnitySceneQueryResult
    {
        public List<UnityGameObjectInfo> Objects { get; set; } = new();
    }

    public sealed class UnityGameObjectInfo
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public int InstanceId { get; set; }
        public bool Active { get; set; }
        public List<string> Components { get; set; } = new();
        public List<UnityGameObjectInfo> Children { get; set; } = new();
    }

    public sealed class UnitySelectionResult
    {
        public List<UnityGameObjectInfo> SelectedObjects { get; set; } = new();
    }

    public sealed class UnityConsoleLogsResult
    {
        public List<UnityConsoleLogEntry> Logs { get; set; } = new();
    }

    public sealed class UnityConsoleLogEntry
    {
        public string Type { get; set; } = "";
        public string Message { get; set; } = "";
        public string? StackTrace { get; set; }
        public int Count { get; set; }
    }

    public sealed class UnityProjectInfoResult
    {
        public string ProjectName { get; set; } = "";
        public string UnityVersion { get; set; } = "";
        public string ProjectPath { get; set; } = "";
        public List<string> Packages { get; set; } = new();
    }

    #endregion
}
