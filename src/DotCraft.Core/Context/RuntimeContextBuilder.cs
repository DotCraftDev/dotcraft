namespace DotCraft.Context;

/// <summary>
/// Builds the [Runtime Context] block that is appended to each user message.
/// Keeping dynamic values (time, per-message sender) out of the system prompt
/// ensures the system prompt prefix stays stable across requests, enabling
/// LLM prompt cache reuse.
/// </summary>
public static class RuntimeContextBuilder
{
    /// <summary>
    /// Appends a [Runtime Context] block to the given prompt containing:
    /// - Current time (always)
    /// - Any dynamic lines contributed by registered IChatContextProvider instances
    /// </summary>
    public static string AppendTo(string prompt)
    {
        var lines = new List<string>();

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm (dddd)");
        lines.Add($"Current Time: {now} ({TimeZoneInfo.Local.DisplayName})");

        foreach (var provider in ChatContextRegistry.All)
            lines.AddRange(provider.GetRuntimeContextLines());

        return $"{prompt}\n\n[Runtime Context]\n{string.Join("\n", lines)}";
    }
}
