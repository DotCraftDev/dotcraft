using System.Text.Json;
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

    /// <summary>
    /// Formats the unified JSON result from WebSearch into human-readable lines.
    /// Returns null if the result is empty or unparseable (caller falls back to generic truncation).
    /// </summary>
    public static IReadOnlyList<string>? WebSearchResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;

        try
        {
            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            // Handle double-encoded JSON (AG-UI SDK wraps string results in quotes)
            if (root.ValueKind == JsonValueKind.String)
            {
                var inner = root.GetString();
                if (inner == null) return null;
                using var innerDoc = JsonDocument.Parse(inner);
                return ParseWebSearchResult(innerDoc.RootElement);
            }

            return ParseWebSearchResult(root);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string>? ParseWebSearchResult(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        // Error shape
        if (root.TryGetProperty("error", out var errorProp))
        {
            var msg = errorProp.GetString();
            return string.IsNullOrWhiteSpace(msg) ? null : [$"Error: {msg}"];
        }

        if (!root.TryGetProperty("results", out var resultsProp) ||
            resultsProp.ValueKind != JsonValueKind.Array)
            return null;

        var lines = new List<string>();
        var count = resultsProp.GetArrayLength();

        if (count == 0)
        {
            lines.Add("No results found.");
            return lines;
        }

        lines.Add($"{count} result{(count == 1 ? "" : "s")}:");

        var i = 1;
        foreach (var item in resultsProp.EnumerateArray())
        {
            var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
            var url = item.TryGetProperty("url", out var u) ? u.GetString() : null;

            string domain = "";
            if (!string.IsNullOrWhiteSpace(url))
            {
                try { domain = new Uri(url).Host; }
                catch { domain = url; }
            }

            var titleText = ToolDisplayHelpers.Truncate(title ?? url ?? "?", 70);
            var line = string.IsNullOrWhiteSpace(domain)
                ? $"{i}. {titleText}"
                : $"{i}. {titleText} — {domain}";
            lines.Add(line);
            i++;
        }

        return lines;
    }

    /// <summary>
    /// Formats the unified JSON result from WebFetch into a single human-readable line.
    /// Returns null if the result is empty or unparseable.
    /// </summary>
    public static IReadOnlyList<string>? WebFetchResult(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return null;

        try
        {
            using var doc = JsonDocument.Parse(result);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.String)
            {
                var inner = root.GetString();
                if (inner == null) return null;
                using var innerDoc = JsonDocument.Parse(inner);
                return ParseWebFetchResult(innerDoc.RootElement);
            }

            return ParseWebFetchResult(root);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string>? ParseWebFetchResult(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        if (root.TryGetProperty("error", out var errorProp))
        {
            var msg = errorProp.GetString();
            return string.IsNullOrWhiteSpace(msg) ? null : [$"Error: {msg}"];
        }

        var parts = new List<string>();

        if (root.TryGetProperty("status", out var statusProp) && statusProp.ValueKind == JsonValueKind.Number)
            parts.Add(statusProp.GetInt32().ToString());

        if (root.TryGetProperty("length", out var lenProp) && lenProp.ValueKind == JsonValueKind.Number)
            parts.Add($"{lenProp.GetInt64():N0} chars");

        if (root.TryGetProperty("extractor", out var extProp))
        {
            var ext = extProp.GetString();
            if (!string.IsNullOrWhiteSpace(ext)) parts.Add(ext);
        }

        if (root.TryGetProperty("truncated", out var truncProp) &&
            truncProp.ValueKind == JsonValueKind.True)
            parts.Add("truncated");

        return parts.Count == 0 ? null : [string.Join(" · ", parts)];
    }

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

}
