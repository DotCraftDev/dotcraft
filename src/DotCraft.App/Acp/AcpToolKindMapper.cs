namespace DotCraft.Acp;

/// <summary>
/// Maps DotCraft tool names to ACP tool call kinds.
/// </summary>
public static class AcpToolKindMapper
{
    private static readonly Dictionary<string, string> ToolKindMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ReadFile"] = AcpToolKind.Read,
        ["WriteFile"] = AcpToolKind.Edit,
        ["EditFile"] = AcpToolKind.Edit,
        ["GrepFiles"] = AcpToolKind.Search,
        ["FindFiles"] = AcpToolKind.Search,
        ["Exec"] = AcpToolKind.Execute,
        ["WebSearch"] = AcpToolKind.Fetch,
        ["WebFetch"] = AcpToolKind.Fetch,
        ["SpawnAgent"] = AcpToolKind.Think,
        ["CreatePlan"] = AcpToolKind.Other,
        ["UpdateTodos"] = AcpToolKind.Other,
        // Unity tools
        ["unity_scene_query"] = AcpToolKind.Unity,
        ["unity_get_selection"] = AcpToolKind.Unity,
        ["unity_set_selection"] = AcpToolKind.Unity,
        ["unity_create_gameobject"] = AcpToolKind.Unity,
        ["unity_modify_component"] = AcpToolKind.Unity,
        ["unity_delete_gameobject"] = AcpToolKind.Unity,
        ["unity_get_console_logs"] = AcpToolKind.Unity,
        ["unity_execute_menu_item"] = AcpToolKind.Unity,
        ["unity_get_asset_info"] = AcpToolKind.Unity,
        ["unity_import_asset"] = AcpToolKind.Unity,
        ["unity_find_assets"] = AcpToolKind.Unity,
        ["unity_get_project_info"] = AcpToolKind.Unity
    };

    /// <summary>
    /// Gets the ACP tool kind for the given DotCraft tool name.
    /// </summary>
    public static string GetKind(string? toolName)
    {
        // Check for Unity tool prefix as fallback
        if (toolName != null && toolName.StartsWith("unity_", StringComparison.OrdinalIgnoreCase))
            return AcpToolKind.Unity;

        return ToolKindMap.GetValueOrDefault(toolName!, AcpToolKind.Other);
    }

    /// <summary>
    /// Extracts file paths from tool call arguments for reporting file locations.
    /// </summary>
    public static List<string>? ExtractFilePaths(string toolName, IDictionary<string, object?>? arguments)
    {
        if (arguments == null) return null;

        var kind = GetKind(toolName);
        if (kind is not (AcpToolKind.Read or AcpToolKind.Edit or AcpToolKind.Delete))
            return null;

        var paths = new List<string>();
        foreach (var key in new[] { "path", "filePath", "file_path", "file" })
        {
            if (arguments.TryGetValue(key, out var val) && val is string s && !string.IsNullOrEmpty(s))
                paths.Add(s);
        }

        return paths.Count > 0 ? paths : null;
    }
}
