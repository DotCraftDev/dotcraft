using System.Text.Json;
using DotCraft.Abstractions;
using Microsoft.Extensions.AI;

namespace DotCraft.Unity;

/// <summary>
/// Provides Unity-specific tools that use ACP extension methods (_unity/*).
/// These tools allow the AI agent to interact with the Unity Editor.
/// </summary>
public sealed class UnityAcpToolProvider : IAgentToolProvider
{
    /// <summary>
    /// Extension prefix that the client must advertise in <c>ClientCapabilities.Extensions</c>
    /// for these tools to be registered.
    /// </summary>
    internal const string ExtensionPrefix = "_unity";

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

        // Scene tools
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

        yield return AIFunctionFactory.Create(
            (string[] objectPaths, CancellationToken ct) =>
                CallExtensionAsync<UnityOperationResult>(proxy, "_unity/set_selection",
                    new { objectPaths }, ct),
            name: "unity_set_selection",
            description: "Set selection in Unity Editor. Pass array of object paths (e.g., ['/Main Camera', '/Directional Light']).");

        yield return AIFunctionFactory.Create(
            (string name, string? parentPath, string[]? components, float[]? position, CancellationToken ct) =>
                CallExtensionAsync<UnityCreateGameObjectResult>(proxy, "_unity/create_gameobject",
                    new { name, parentPath, components, position }, ct),
            name: "unity_create_gameobject",
            description: "Create a new GameObject in the Unity scene. " +
                         "'name' is required. Optionally specify 'parentPath', 'components' array (e.g., ['Rigidbody', 'BoxCollider']), " +
                         "and 'position' as [x, y, z].");

        yield return AIFunctionFactory.Create(
            (string objectPath, string componentType, Dictionary<string, object>? properties, CancellationToken ct) =>
                CallExtensionAsync<UnityOperationResult>(proxy, "_unity/modify_component",
                    new { objectPath, componentType, properties }, ct),
            name: "unity_modify_component",
            description: "Modify properties of a component on a GameObject. " +
                         "'objectPath' is the hierarchy path, 'componentType' is the full component name, " +
                         "'properties' is a dictionary of property names and values to set.");

        yield return AIFunctionFactory.Create(
            (string objectPath, CancellationToken ct) =>
                CallExtensionAsync<UnityOperationResult>(proxy, "_unity/delete_gameobject",
                    new { objectPath }, ct),
            name: "unity_delete_gameobject",
            description: "Delete a GameObject from the scene by its hierarchy path.");

        // Console tools
        yield return AIFunctionFactory.Create(
            (string[]? types, int limit, CancellationToken ct) =>
                CallExtensionAsync<UnityConsoleLogsResult>(proxy, "_unity/get_console_logs",
                    new { types, limit = limit > 0 ? limit : 50 }, ct),
            name: "unity_get_console_logs",
            description: "Get recent Unity Console log entries. " +
                         "'types' filters by log type: ['error', 'warning', 'log'] (default all). " +
                         "'limit' sets max entries to return (default 50).");

        // Editor tools
        yield return AIFunctionFactory.Create(
            (string menuPath, CancellationToken ct) =>
                CallExtensionAsync<UnityOperationResult>(proxy, "_unity/execute_menu_item",
                    new { menuPath }, ct),
            name: "unity_execute_menu_item",
            description: "Execute a Unity Editor menu item by its path. " +
                         "Example: 'GameObject/3D Object/Cube' or 'Assets/Create/C# Script'.");

        // Asset tools
        yield return AIFunctionFactory.Create(
            (string assetPath, CancellationToken ct) =>
                CallExtensionAsync<UnityAssetInfoResult>(proxy, "_unity/get_asset_info",
                    new { assetPath }, ct),
            name: "unity_get_asset_info",
            description: "Get metadata about a Unity asset at the specified path (e.g., 'Assets/Prefabs/Player.prefab'). " +
                         "Returns asset type, dependencies, and import settings.");

        yield return AIFunctionFactory.Create(
            (string assetPath, CancellationToken ct) =>
                CallExtensionAsync<UnityOperationResult>(proxy, "_unity/import_asset",
                    new { assetPath }, ct),
            name: "unity_import_asset",
            description: "Trigger AssetDatabase.ImportAsset for the specified asset path. " +
                         "Useful after modifying asset files externally.");

        yield return AIFunctionFactory.Create(
            (string filter, string[]? searchInFolders, CancellationToken ct) =>
                CallExtensionAsync<UnityFindAssetsResult>(proxy, "_unity/find_assets",
                    new { filter, searchInFolders }, ct),
            name: "unity_find_assets",
            description: "Search for Unity assets using AssetDatabase.FindAssets. " +
                         "'filter' is the search query (e.g., 't:Prefab', 'l:Audio'). " +
                         "'searchInFolders' limits search scope.");

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

    public sealed class UnityCreateGameObjectResult
    {
        public int InstanceId { get; set; }
        public string Path { get; set; } = "";
    }

    public sealed class UnityOperationResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
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

    public sealed class UnityAssetInfoResult
    {
        public string Path { get; set; } = "";
        public string Type { get; set; } = "";
        public long Size { get; set; }
        public List<string> Dependencies { get; set; } = new();
        public Dictionary<string, object>? ImportSettings { get; set; }
    }

    public sealed class UnityFindAssetsResult
    {
        public List<UnityAssetReference> Assets { get; set; } = new();
    }

    public sealed class UnityAssetReference
    {
        public string Path { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
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
