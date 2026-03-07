using System.Text.RegularExpressions;
using Spectre.Console;

namespace DotCraft.CLI.Rendering;

/// <summary>
/// Renders a markdown string with basic Spectre.Console styles.
/// </summary>
internal static partial class MarkdownConsoleRenderer
{
    [GeneratedRegex(@"^(\s*)([-*+])\s+(.+)$")]
    private static partial Regex UnorderedListRegex();

    [GeneratedRegex(@"^(\s*)(\d+)\.\s+(.+)$")]
    private static partial Regex OrderedListRegex();

    [GeneratedRegex(@"^(\s*)[-*+]\s*\[(x|X| )\]\s*(.+)$")]
    private static partial Regex TaskListRegex();

    [GeneratedRegex(@"^\s*\|?[\s:\-|]+\|?\s*$")]
    private static partial Regex TableSeparatorRegex();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex(@"\*\*\*([^*]+)\*\*\*")]
    private static partial Regex BoldItalicRegex();

    [GeneratedRegex(@"\*\*([^*]+)\*\*")]
    private static partial Regex BoldRegex();

    [GeneratedRegex(@"(?<!\*)\*([^*\n]+)\*(?!\*)")]
    private static partial Regex ItalicRegex();

    [GeneratedRegex(@"~~([^~]+)~~")]
    private static partial Regex StrikeRegex();

    [GeneratedRegex(@"==([^=]+)==")]
    private static partial Regex HighlightRegex();

    [GeneratedRegex(@"!\[([^\]]*)\]\(([^)]+)\)")]
    private static partial Regex ImageRegex();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)]+)\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"<kbd>(.*?)</kbd>", RegexOptions.IgnoreCase)]
    private static partial Regex KbdRegex();

    [GeneratedRegex(@"\{\{P(\d+)\}\}")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"(?=├──|└──)")]
    private static partial Regex TreeSplitRegex();

    [GeneratedRegex(@"(?<!^)(?=(#{1,6}(?!#)))")]
    private static partial Regex PackedHeadingSplitRegex();

    [GeneratedRegex(@"(?<!^)(?=(?:[-*+]\s*\[[xX ]\]|[-*+](?=[\p{L}\p{N}\p{IsCJKUnifiedIdeographs}])))")]
    private static partial Regex PackedBulletSplitRegex();

    [GeneratedRegex(@"(?<!^)(?=(\d+\.\S))")]
    private static partial Regex PackedOrderedSplitRegex();

    [GeneratedRegex(@"(?<!^)(?=>)")]
    private static partial Regex PackedQuoteSplitRegex();

    internal sealed class StreamSession
    {
        private string _pending = string.Empty;
        private bool _inCodeBlock;
        private string _codeLanguage = string.Empty;
        private readonly List<string> _tableBuffer = [];

        public void Append(string chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return;
            }

            _pending += Normalize(chunk);
            ProcessPendingLines(flushLastLine: false);
        }

        public void Complete()
        {
            ProcessPendingLines(flushLastLine: true);
            FlushTableBuffer();

            if (_inCodeBlock)
            {
                EndCodeBlock();
            }

            _pending = string.Empty;
            _inCodeBlock = false;
            _codeLanguage = string.Empty;
        }

        private void ProcessPendingLines(bool flushLastLine)
        {
            while (true)
            {
                var newlineIndex = _pending.IndexOf('\n');
                if (newlineIndex < 0)
                {
                    break;
                }

                var line = _pending[..newlineIndex];
                _pending = _pending[(newlineIndex + 1)..];
                ProcessLine(line);
            }

            if (flushLastLine && _pending.Length > 0)
            {
                ProcessLine(_pending);
                _pending = string.Empty;
            }
        }

        private void ProcessLine(string line)
        {
            var remaining = line;
            while (true)
            {
                var fenceIndex = remaining.IndexOf("```", StringComparison.Ordinal);
                if (fenceIndex < 0)
                {
                    if (_inCodeBlock)
                    {
                        RenderCodeLine(remaining);
                    }
                    else if (IsTableLine(remaining))
                    {
                        _tableBuffer.Add(remaining);
                    }
                    else
                    {
                        FlushTableBuffer();
                        RenderMarkdownLine(remaining);
                    }
                    return;
                }

                var beforeFence = remaining[..fenceIndex];
                var afterFence = remaining[(fenceIndex + 3)..];

                if (_inCodeBlock)
                {
                    if (beforeFence.Length > 0)
                    {
                        RenderCodeLine(beforeFence);
                    }

                    EndCodeBlock();
                    _inCodeBlock = false;
                    _codeLanguage = string.Empty;

                    if (afterFence.Length == 0)
                    {
                        return;
                    }

                    remaining = afterFence;
                    continue;
                }

                if (beforeFence.Length > 0)
                {
                    RenderOutsideCodeLine(beforeFence);
                }

                _inCodeBlock = true;
                FlushTableBuffer();
                var (language, sameLineCode) = ParseFenceHeader(afterFence);
                _codeLanguage = language;
                BeginCodeBlock(_codeLanguage);

                if (string.IsNullOrEmpty(sameLineCode))
                {
                    return;
                }

                // Continue scanning the same physical line so compact forms like
                // ```bashgit push -f origin master``` can close correctly.
                remaining = sameLineCode;
            }
        }

        private static (string Language, string SameLineCode) ParseFenceHeader(string textAfterFence)
        {
            if (string.IsNullOrWhiteSpace(textAfterFence))
            {
                return (string.Empty, string.Empty);
            }

            var raw = textAfterFence.TrimStart();
            if (raw.Length == 0)
            {
                return (string.Empty, string.Empty);
            }

            var tokenEnd = 0;
            while (tokenEnd < raw.Length && (char.IsLetterOrDigit(raw[tokenEnd]) || raw[tokenEnd] is '_' or '-' or '+' or '#'))
            {
                tokenEnd++;
            }

            if (tokenEnd == 0)
            {
                return (string.Empty, raw);
            }

            var token = raw[..tokenEnd];
            var rest = raw[tokenEnd..];
            if (rest.Length == 0)
            {
                return (token, string.Empty);
            }

            // Accept compact forms like ```python# comment
            if (rest[0] == '#')
            {
                return (token, rest);
            }

            if (char.IsWhiteSpace(rest[0]))
            {
                var trimmedRest = rest.TrimStart();
                if (trimmedRest.Length == 0)
                {
                    return (token, string.Empty);
                }

                return (token, trimmedRest);
            }

            // No clear language delimiter: treat whole tail as code.
            return (string.Empty, raw);
        }

        private void RenderOutsideCodeLine(string line)
        {
            foreach (var segment in ExpandPackedMarkdownLine(line))
            {
                if (IsTableLine(segment))
                {
                    _tableBuffer.Add(segment);
                    continue;
                }

                FlushTableBuffer();
                RenderMarkdownLine(segment);
            }
        }

        private void FlushTableBuffer()
        {
            if (_tableBuffer.Count == 0)
            {
                return;
            }

            WriteMarkdownTable(_tableBuffer);
            _tableBuffer.Clear();
        }

        private static bool IsTableLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return false;
            }

            var trimmed = line.Trim();
            return trimmed.Count(ch => ch == '|') >= 2;
        }
    }

    public static StreamSession CreateStreamSession()
    {
        return new StreamSession();
    }

    public static void Render(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return;
        }

        var session = CreateStreamSession();
        session.Append(markdown);
        session.Complete();
    }

    private static bool TryRenderHeading(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '#')
        {
            return false;
        }

        var level = 0;
        while (level < trimmed.Length && level < 6 && trimmed[level] == '#')
        {
            level++;
        }

        if (level == 0 || level >= trimmed.Length)
        {
            return false;
        }

        var content = trimmed[level..].TrimStart();
        if (content.Length == 0)
        {
            return false;
        }

        var color = level switch
        {
            1 => "deepskyblue1",
            2 => "dodgerblue1",
            _ => "lightskyblue1"
        };

        var style = level <= 2 ? "bold" : string.Empty;
        var text = ApplyInlineStyles(content);
        var prefix = style.Length > 0 ? $"{style} {color}" : color;
        AnsiConsole.MarkupLine($"[{prefix}]{text}[/]");
        return true;
    }

    private static bool TryRenderQuote(string line)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith('>'))
        {
            return false;
        }

        var content = trimmed.Length > 1 ? trimmed[1..].TrimStart() : string.Empty;
        AnsiConsole.MarkupLine($"[grey]│[/] [italic grey]{ApplyInlineStyles(content)}[/]");
        return true;
    }

    private static bool TryRenderListItem(string line)
    {
        var taskMatch = TaskListRegex().Match(line);
        if (taskMatch.Success)
        {
            var indent = taskMatch.Groups[1].Value;
            var isChecked = taskMatch.Groups[2].Value.Equals("x", StringComparison.OrdinalIgnoreCase);
            var content = taskMatch.Groups[3].Value;
            var indentSize = Math.Max(indent.Length / 2, 0);
            var bulletIndent = new string(' ', indentSize * 2);
            var box = isChecked ? "☑" : "☐";
            var color = isChecked ? "green" : "yellow";
            AnsiConsole.MarkupLine($"{bulletIndent}[{color}]{box}[/] {ApplyInlineStyles(content)}");
            return true;
        }

        var unorderedMatch = UnorderedListRegex().Match(line);
        if (unorderedMatch.Success)
        {
            var indent = unorderedMatch.Groups[1].Value;
            var content = unorderedMatch.Groups[3].Value;
            var indentSize = Math.Max(indent.Length / 2, 0);
            var bulletIndent = new string(' ', indentSize * 2);
            AnsiConsole.MarkupLine($"{bulletIndent}[teal]•[/] {ApplyInlineStyles(content)}");
            return true;
        }

        var orderedMatch = OrderedListRegex().Match(line);
        if (!orderedMatch.Success)
        {
            return false;
        }

        var orderedIndent = orderedMatch.Groups[1].Value;
        var number = orderedMatch.Groups[2].Value;
        var orderedContent = orderedMatch.Groups[3].Value;
        var orderedIndentSize = Math.Max(orderedIndent.Length / 2, 0);
        var orderedBulletIndent = new string(' ', orderedIndentSize * 2);
        AnsiConsole.MarkupLine($"{orderedBulletIndent}[teal]{Markup.Escape(number)}.[/] {ApplyInlineStyles(orderedContent)}");
        return true;
    }

    private static bool IsHorizontalRule(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 3)
        {
            return false;
        }

        return trimmed.All(c => c == '-') || trimmed.All(c => c == '*') || trimmed.All(c => c == '_');
    }

    private static string ApplyInlineStyles(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var placeholders = new List<string>();
        var text = input;

        text = ReplaceWithPlaceholder(text, ImageRegex(), match =>
        {
            var alt = Markup.Escape(match.Groups[1].Value);
            var url = Markup.Escape(match.Groups[2].Value);
            return $"[grey]🖼 {alt} ({url})[/]";
        }, placeholders);

        text = ReplaceWithPlaceholder(text, LinkRegex(), match =>
        {
            var label = Markup.Escape(match.Groups[1].Value);
            var url = Markup.Escape(match.Groups[2].Value);
            return $"[underline blue]{label}[/] [grey]({url})[/]";
        }, placeholders);

        text = ReplaceWithPlaceholder(text, InlineCodeRegex(), match =>
        {
            var code = Markup.Escape(match.Groups[1].Value);
            return $"[black on grey84]{code}[/]";
        }, placeholders);

        text = ReplaceWithPlaceholder(text, KbdRegex(), match => $"[black on grey70] {Markup.Escape(match.Groups[1].Value)} [/]", placeholders);
        text = ReplaceWithPlaceholder(text, BoldItalicRegex(), match => $"[bold italic]{Markup.Escape(match.Groups[1].Value)}[/]", placeholders);
        text = ReplaceWithPlaceholder(text, BoldRegex(), match => $"[bold]{Markup.Escape(match.Groups[1].Value)}[/]", placeholders);
        text = ReplaceWithPlaceholder(text, ItalicRegex(), match => $"[italic]{Markup.Escape(match.Groups[1].Value)}[/]", placeholders);
        text = ReplaceWithPlaceholder(text, StrikeRegex(), match => $"[strikethrough]{Markup.Escape(match.Groups[1].Value)}[/]", placeholders);
        text = ReplaceWithPlaceholder(text, HighlightRegex(), match => $"[black on yellow]{Markup.Escape(match.Groups[1].Value)}[/]", placeholders);

        var escaped = Markup.Escape(text);
        return RestorePlaceholders(escaped, placeholders);
    }

    private static string ReplaceWithPlaceholder(string input, Regex regex, Func<Match, string> projector, List<string> placeholders)
    {
        return regex.Replace(input, match =>
        {
            var token = $"{{{{P{placeholders.Count}}}}}";
            placeholders.Add(projector(match));
            return token;
        });
    }

    private static string RestorePlaceholders(string input, List<string> placeholders)
    {
        return PlaceholderRegex().Replace(input, match =>
        {
            if (!int.TryParse(match.Groups[1].Value, out var index))
            {
                return match.Value;
            }
            if (index < 0 || index >= placeholders.Count)
            {
                return match.Value;
            }
            return placeholders[index];
        });
    }

    private static void BeginCodeBlock(string? language)
    {
        var title = string.IsNullOrWhiteSpace(language) ? "code" : language.Trim();
        AnsiConsole.MarkupLine($"[grey]┌─ {Markup.Escape(title)}[/]");
    }

    private static void RenderCodeLine(string line)
    {
        foreach (var logicalLine in ExpandTreeLikeCodeLine(line))
        {
            AnsiConsole.MarkupLine($"[grey]│[/] [default]{Markup.Escape(logicalLine)}[/]");
        }
    }

    private static void EndCodeBlock()
    {
        AnsiConsole.MarkupLine("[grey]└[/]");
    }

    private static void RenderMarkdownLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            AnsiConsole.WriteLine();
            return;
        }

        if (TryRenderHeading(line))
        {
            return;
        }

        if (TryRenderQuote(line))
        {
            return;
        }

        if (TryRenderListItem(line))
        {
            return;
        }

        if (IsHorizontalRule(line))
        {
            AnsiConsole.Write(new Rule().RuleStyle("grey"));
            return;
        }

        AnsiConsole.MarkupLine(ApplyInlineStyles(line));
    }

    private static string Normalize(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static IEnumerable<string> ExpandTreeLikeCodeLine(string line)
    {
        if (string.IsNullOrEmpty(line) || (!line.Contains("├──", StringComparison.Ordinal) && !line.Contains("└──", StringComparison.Ordinal)))
        {
            yield return line;
            yield break;
        }

        var pieces = TreeSplitRegex().Split(line);
        foreach (var piece in pieces)
        {
            if (piece.Length == 0)
            {
                continue;
            }

            yield return piece;
        }
    }

    private static IEnumerable<string> ExpandPackedMarkdownLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            yield return line;
            yield break;
        }

        var expanded = line;
        expanded = PackedHeadingSplitRegex().Replace(expanded, "\n");
        expanded = PackedBulletSplitRegex().Replace(expanded, "\n");
        expanded = PackedOrderedSplitRegex().Replace(expanded, "\n");
        expanded = PackedQuoteSplitRegex().Replace(expanded, "\n");

        foreach (var segment in expanded.Split('\n'))
        {
            if (segment.Length == 0)
            {
                continue;
            }

            yield return segment;
        }
    }

    private static void WriteMarkdownTable(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
        {
            return;
        }

        var rows = new List<string[]>();
        foreach (var line in lines)
        {
            var cells = ParseTableCells(line);
            if (cells.Length >= 2)
            {
                rows.Add(cells);
            }
        }

        if (rows.Count == 0)
        {
            foreach (var line in lines)
            {
                AnsiConsole.MarkupLine(ApplyInlineStyles(line));
            }
            return;
        }

        var separatorIndex = rows.FindIndex(IsSeparatorRow);
        var headerRow = separatorIndex > 0 ? rows[0] : null;
        var bodyRows = separatorIndex >= 0
            ? rows.Where((_, i) => i != 0 && i != separatorIndex).ToList()
            : rows;

        var columnCount = Math.Max(headerRow?.Length ?? 0, bodyRows.Count > 0 ? bodyRows.Max(r => r.Length) : 0);
        if (columnCount < 2)
        {
            foreach (var line in lines)
            {
                AnsiConsole.MarkupLine(ApplyInlineStyles(line));
            }
            return;
        }

        var table = new Table();
        table.Border(TableBorder.Rounded);

        for (var i = 0; i < columnCount; i++)
        {
            var header = headerRow != null && i < headerRow.Length ? headerRow[i] : $"Col {i + 1}";
            table.AddColumn(new TableColumn($"[bold]{ApplyInlineStyles(header)}[/]"));
        }

        foreach (var row in bodyRows)
        {
            var padded = new string[columnCount];
            for (var i = 0; i < columnCount; i++)
            {
                padded[i] = i < row.Length ? ApplyInlineStyles(row[i]) : string.Empty;
            }
            table.AddRow(padded);
        }

        AnsiConsole.Write(table);
    }

    private static string[] ParseTableCells(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('|'))
        {
            trimmed = trimmed[1..];
        }
        if (trimmed.EndsWith('|'))
        {
            trimmed = trimmed[..^1];
        }

        return trimmed.Split('|').Select(cell => cell.Trim()).ToArray();
    }

    private static bool IsSeparatorRow(IEnumerable<string> row)
    {
        return row.All(cell => TableSeparatorRegex().IsMatch(cell.Replace(" ", string.Empty)));
    }
}
