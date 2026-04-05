using System.Text;
using DotCraft.Agents;

namespace DotCraft.Tools;

/// <summary>
/// Normalizes empty tool results, applies per-tool size limits, and spills oversized text to disk
/// under <c>{workspace}/.craft/tool-results/</c> with a head/tail preview.
/// </summary>
public static class ToolResultProcessor
{
    /// <summary>
    /// Marker substring included in spill previews so UIs can detect them.
    /// </summary>
    public const string SpillPreviewMarker = "lines omitted, full output at:";

    /// <summary>
    /// Returns the standardized empty-result message for a tool.
    /// </summary>
    public static string EmptyResultMessage(string toolName)
        => $"({toolName} completed with no output)";

    /// <summary>
    /// Converts a tool result to a string for length measurement and spill content.
    /// </summary>
    public static string ToStringForLimit(object? rawResult)
    {
        if (rawResult is string s)
            return s;
        return ImageContentSanitizingChatClient.DescribeResult(rawResult);
    }

    /// <summary>
    /// Whether the described tool output should be treated as empty for normalization.
    /// </summary>
    public static bool IsEffectivelyEmpty(string text)
        => string.IsNullOrWhiteSpace(text) || text == "(no output)";

    /// <summary>
    /// Applies empty normalization, optional size limiting with spill-to-disk, and returns the value
    /// to pass to the model (string or original object when unchanged).
    /// </summary>
    /// <param name="toolName">Tool function name.</param>
    /// <param name="rawResult">Raw return value from the tool.</param>
    /// <param name="maxResultChars">
    /// Maximum length of the string form before spill; <c>0</c> means unlimited (only empty normalization).
    /// </param>
    /// <param name="workspacePath">Workspace root; spill files are written under <c>.craft/tool-results/</c>.</param>
    /// <param name="sessionId">Session/thread id for the spill subdirectory, or null for <c>_unsession</c>.</param>
    /// <param name="previewLines">Head and tail line count for the preview.</param>
    public static object? Process(
        string toolName,
        object? rawResult,
        int maxResultChars,
        string workspacePath,
        string? sessionId,
        int previewLines)
    {
        var text = ToStringForLimit(rawResult);
        if (IsEffectivelyEmpty(text))
            return EmptyResultMessage(toolName);

        if (maxResultChars <= 0)
            return rawResult;

        if (text.Length <= maxResultChars)
            return rawResult;

        var relativePath = SpillToDisk(text, workspacePath, sessionId, toolName);
        return BuildPreview(text, previewLines, relativePath, maxResultChars);
    }

    /// <summary>
    /// Writes full text to disk and returns the workspace-relative path (forward slashes).
    /// </summary>
    public static string SpillToDisk(
        string text,
        string workspacePath,
        string? sessionId,
        string toolName)
    {
        var spillDir = GetSpillDirectory(workspacePath, sessionId);
        Directory.CreateDirectory(spillDir);
        var fileName = $"{toolName}_{Guid.NewGuid():N}.txt";
        var absolutePath = Path.Combine(spillDir, fileName);
        File.WriteAllText(absolutePath, text, Encoding.UTF8);
        return GetSpillRelativePath(sessionId, fileName);
    }

    /// <summary>
    /// Builds a head + tail preview with a reference to the spill file path.
    /// </summary>
    /// <param name="maxPreviewChars">
    /// Maximum characters of normalized text in the short-line preview; excess is truncated. Use <see cref="int.MaxValue"/> for no cap.
    /// </param>
    public static string BuildPreview(
        string text,
        int previewLines,
        string spillRelativePath,
        int maxPreviewChars = int.MaxValue)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var totalLines = lines.Length;

        if (previewLines < 1)
            previewLines = 1;

        if (totalLines <= previewLines * 2)
        {
            var body = normalized.Length > maxPreviewChars ? normalized[..maxPreviewChars] : normalized;
            return body + "\n\n" + $"... (full output at: {spillRelativePath})";
        }

        var headText = string.Join("\n", lines[..previewLines]);
        var tailText = string.Join("\n", lines[^previewLines..]);
        var omitted = totalLines - previewLines * 2;

        return headText
               + "\n\n... ("
               + omitted
               + " "
               + SpillPreviewMarker
               + " "
               + spillRelativePath
               + ") ...\n\n"
               + tailText;
    }

    /// <summary>
    /// Resolves the default maximum result length: per-tool attribute, then global config.
    /// A value of <c>0</c> means unlimited.
    /// </summary>
    public static int ResolveMaxResultChars(string toolName, int globalMaxToolResultChars)
    {
        var perTool = ToolRegistry.GetMaxResultChars(toolName);
        var limit = perTool ?? globalMaxToolResultChars;
        return limit <= 0 ? 0 : limit;
    }

    private static string GetSpillDirectory(string workspacePath, string? sessionId)
    {
        var safeSession = SanitizeSessionSegment(sessionId);
        return Path.Combine(workspacePath, ".craft", "tool-results", safeSession);
    }

    private static string GetSpillRelativePath(string? sessionId, string fileName)
    {
        var safeSession = SanitizeSessionSegment(sessionId);
        return Path.Combine(".craft", "tool-results", safeSession, fileName).Replace('\\', '/');
    }

    private static string SanitizeSessionSegment(string? sessionId)
    {
        var s = string.IsNullOrWhiteSpace(sessionId) ? "_unsession" : sessionId!;
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }
}
