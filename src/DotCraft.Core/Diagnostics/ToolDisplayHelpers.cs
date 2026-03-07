using System.Text.Json;

namespace DotCraft.Diagnostics;

/// <summary>
/// Shared helpers for tool display formatter methods.
/// Each tool module's static display class uses these to extract arguments.
/// </summary>
public static class ToolDisplayHelpers
{
    public static string? GetString(IDictionary<string, object?>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value) || value == null)
            return null;

        if (value is JsonElement je)
            return je.ValueKind == JsonValueKind.String ? je.GetString() : je.ToString();

        return value.ToString();
    }

    public static int GetInt(IDictionary<string, object?>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value) || value == null)
            return 0;

        if (value is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var i))
                return i;
            return 0;
        }

        if (value is int intVal) return intVal;
        if (value is long longVal) return (int)longVal;
        if (int.TryParse(value.ToString(), out var parsed)) return parsed;
        return 0;
    }

    public static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text[..maxLength] + "...";
    }

    /// <summary>
    /// Prepare a tool result for single-line indented display.
    /// Returns null if there is nothing to show.
    /// </summary>
    public static string? FormatResult(string? result, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(result))
            return null;

        var normalized = result.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        return Truncate(normalized, maxLength);
    }
}
