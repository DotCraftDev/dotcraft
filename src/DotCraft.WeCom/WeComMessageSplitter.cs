using System.Text;

namespace DotCraft.WeCom;

/// <summary>
/// Splits text into chunks that fit within WeCom webhook API byte limits.
/// Text messages: 2048 bytes; Markdown messages: 4096 bytes.
/// </summary>
internal static class WeComMessageSplitter
{
    public const int TextMaxBytes = 2048;
    public const int MarkdownMaxBytes = 4096;

    /// <summary>
    /// Delay between consecutive chunk sends to avoid hitting the 20 msg/min rate limit.
    /// </summary>
    public const int InterChunkDelayMs = 200;

    private static readonly string[] ParagraphSeparators = ["\r\n\r\n", "\n\n"];
    private static readonly string[] LineSeparators = ["\r\n", "\n"];
    private static readonly string[] SentenceSeparators = ["。", ".\u0020", "！", "! ", "？", "? "];

    /// <summary>
    /// Split content into chunks where each chunk's UTF-8 byte length &lt;= maxBytes.
    /// Prefers splitting at natural boundaries: paragraph > line > sentence > word > hard cut.
    /// </summary>
    public static List<string> Split(string content, int maxBytes)
    {
        if (string.IsNullOrEmpty(content))
            return [content ?? string.Empty];

        if (Encoding.UTF8.GetByteCount(content) <= maxBytes)
            return [content];

        var chunks = new List<string>();
        var remaining = content.AsSpan();

        while (remaining.Length > 0)
        {
            if (Encoding.UTF8.GetByteCount(remaining) <= maxBytes)
            {
                chunks.Add(remaining.ToString());
                break;
            }

            var splitIndex = FindSplitIndex(remaining, maxBytes);
            chunks.Add(remaining[..splitIndex].ToString().TrimEnd());
            remaining = remaining[splitIndex..].TrimStart();
        }

        return chunks;
    }

    /// <summary>
    /// Find the best character index to split at, ensuring the left part fits within maxBytes.
    /// </summary>
    private static int FindSplitIndex(ReadOnlySpan<char> text, int maxBytes)
    {
        // Binary-search for the max char count whose UTF-8 encoding fits in maxBytes.
        int lo = 0, hi = text.Length;
        while (lo < hi)
        {
            var mid = lo + (hi - lo + 1) / 2;
            if (Encoding.UTF8.GetByteCount(text[..mid]) <= maxBytes)
                lo = mid;
            else
                hi = mid - 1;
        }

        // lo = max chars that fit. Now look for a natural boundary walking backwards.
        var limit = lo;

        if (TryFindSeparator(text, limit, ParagraphSeparators, out var idx))
            return idx;
        if (TryFindSeparator(text, limit, LineSeparators, out idx))
            return idx;
        if (TryFindSeparator(text, limit, SentenceSeparators, out idx))
            return idx;

        // Word boundary: find last space
        var spaceIdx = text[..limit].LastIndexOf(' ');
        if (spaceIdx > limit / 4)
            return spaceIdx + 1;

        // Hard cut at the byte boundary
        return limit;
    }

    /// <summary>
    /// Search backwards from limit for any of the given separators.
    /// Returns true if found, with idx set to the character position right after the separator.
    /// Only accepts a position in the back 75% of the available range to avoid tiny first chunks.
    /// </summary>
    private static bool TryFindSeparator(ReadOnlySpan<char> text, int limit, string[] separators, out int idx)
    {
        var minAcceptable = limit / 4;
        idx = -1;

        foreach (var sep in separators)
        {
            var searchArea = text[..limit];
            var pos = searchArea.LastIndexOf(sep.AsSpan());
            if (pos >= minAcceptable)
            {
                var candidate = pos + sep.Length;
                if (candidate > idx)
                    idx = candidate;
            }
        }

        return idx > 0;
    }
}
