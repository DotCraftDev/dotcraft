using DotCraft.Diagnostics;

namespace DotCraft.Tools;

/// <summary>
/// Human-readable display formatters for core tool calls.
/// Each method is referenced by the corresponding tool via <see cref="ToolAttribute.DisplayMethod"/>.
/// </summary>
public static class CoreToolDisplays
{
    public static string ReadFile(IDictionary<string, object?>? args)
    {
        var path = ToolDisplayHelpers.GetString(args, "path") ?? "?";
        var offset = ToolDisplayHelpers.GetInt(args, "offset");
        var limit = ToolDisplayHelpers.GetInt(args, "limit");

        if (offset > 0 && limit > 0)
            return $"Read {path} lines {offset}-{offset + limit - 1}";
        if (offset > 0)
            return $"Read {path} from line {offset}";
        return $"Read {path}";
    }

    public static string WriteFile(IDictionary<string, object?>? args)
        => $"Wrote {ToolDisplayHelpers.GetString(args, "path") ?? "file"}";

    public static string EditFile(IDictionary<string, object?>? args)
    {
        var path = ToolDisplayHelpers.GetString(args, "path") ?? "file";
        var startLine = ToolDisplayHelpers.GetInt(args, "startLine");
        var endLine = ToolDisplayHelpers.GetInt(args, "endLine");

        if (startLine > 0 && endLine > 0)
            return $"Edited {path} lines {startLine}-{endLine}";
        if (startLine > 0)
            return $"Edited {path} at line {startLine}";
        return $"Edited {path}";
    }

    public static string GrepFiles(IDictionary<string, object?>? args)
    {
        var pattern = ToolDisplayHelpers.GetString(args, "pattern") ?? "?";
        var path = ToolDisplayHelpers.GetString(args, "path");
        var include = ToolDisplayHelpers.GetString(args, "include");

        var desc = $"Grepped \"{ToolDisplayHelpers.Truncate(pattern, 60)}\"";
        if (!string.IsNullOrEmpty(path))
            desc += $" in {path}";
        if (!string.IsNullOrEmpty(include))
            desc += $" ({include})";
        return desc;
    }

    public static string FindFiles(IDictionary<string, object?>? args)
    {
        var pattern = ToolDisplayHelpers.GetString(args, "pattern") ?? "*";
        var path = ToolDisplayHelpers.GetString(args, "path");

        var desc = $"Found files \"{pattern}\"";
        if (!string.IsNullOrEmpty(path))
            desc += $" in {path}";
        return desc;
    }

    public static string Exec(IDictionary<string, object?>? args)
        => $"{ToolDisplayHelpers.Truncate(ToolDisplayHelpers.GetString(args, "command") ?? "command", 80)}";

    public static string WebSearch(IDictionary<string, object?>? args)
        => $"Searched \"{ToolDisplayHelpers.Truncate(ToolDisplayHelpers.GetString(args, "query") ?? "", 80)}\"";

    public static string WebFetch(IDictionary<string, object?>? args)
        => $"Fetched {ToolDisplayHelpers.Truncate(ToolDisplayHelpers.GetString(args, "url") ?? "URL", 80)}";

    public static string SpawnSubagent(IDictionary<string, object?>? args)
    {
        var label = ToolDisplayHelpers.GetString(args, "label")
                    ?? ToolDisplayHelpers.GetString(args, "task")
                    ?? "task";
        return $"Spawned subagent: {ToolDisplayHelpers.Truncate(label, 60)}";
    }

    public static string CreatePlan(IDictionary<string, object?>? args)
        => $"Created plan: {ToolDisplayHelpers.Truncate(ToolDisplayHelpers.GetString(args, "title") ?? "plan", 60)}";

    public static string UpdateTodos(IDictionary<string, object?>? args)
        => "Updated plan tasks";

    public static string TodoWrite(IDictionary<string, object?>? args)
        => "Updated task list";

    public static string ListModifiedFiles(IDictionary<string, object?>? args)
        => "Listed modified files";

    public static string SyncToHost(IDictionary<string, object?>? args)
        => $"Synced {ToolDisplayHelpers.GetString(args, "sandboxPath") ?? "file"} to host";
}
