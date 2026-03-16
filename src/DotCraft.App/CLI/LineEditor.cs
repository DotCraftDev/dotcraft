using Spectre.Console;

namespace DotCraft.CLI;

/// <summary>
/// Result of a line-editing session.
/// </summary>
public enum LineEditResult
{
    /// <summary>User pressed Enter to submit the input.</summary>
    Submitted,

    /// <summary>User pressed Shift+Tab to switch agent mode.</summary>
    ModeSwitch
}

/// <summary>
/// A readline-style line editor with multi-line support, terminal-width-aware wrapping,
/// command hints, cursor movement, command history, and CJK wide-character support.
/// </summary>
public sealed class LineEditor
{
    private readonly List<string> _history;
    private int _historyIndex;
    private string? _savedCurrentInput;

    // Each entry is one logical character (may be a surrogate pair or "\n").
    private readonly List<string> _buffer = new();
    private int _cursorPos; // logical character index inside _buffer

    // Selection support
    private int? _selectionAnchor;

    // Column offset of the prompt rendered before this editor takes over.
    private readonly int _promptWidth;

    // Optional callback: receives current buffer text, returns ordered list of (command, description) hints.
    private readonly Func<string, IReadOnlyList<(string Command, string Description)>>? _hintProvider;
    private int _lastHintLineCount;

    // Width and text of the continuation prompt shown at the start of each wrapped line.
    private const int ContinuationPromptWidth = 4;
    private const string ContinuationPrompt = "... ";

    private static class Ansi
    {
        public const string EraseToEndOfScreen = "\x1b[J";
        public const string EraseToEndOfLine   = "\n\x1b[K";
        public const string Dim                = "\x1b[2m";
        public const string Reset              = "\x1b[0m";
        public const string Cyan               = "\x1b[36m";
        public const string BrightCyan         = "\x1b[96m";
        public const string Grey               = "\x1b[90m";
    }

    public LineEditor(
        List<string> history,
        int promptWidth = 0,
        Func<string, IReadOnlyList<(string Command, string Description)>>? hintProvider = null)
    {
        _history = history;
        _historyIndex = _history.Count;
        _promptWidth = promptWidth;
        _hintProvider = hintProvider;
    }

    /// <summary>
    /// Pre-populate the buffer (e.g. after a Shift+Tab mode-switch) and display it.
    /// </summary>
    public void SetInitialBuffer(IEnumerable<string> chars)
    {
        _buffer.AddRange(chars);
        _cursorPos = _buffer.Count;
        WriteBufferFrom(0);
        UpdateHints();
    }

    /// <summary>
    /// Read one line of input, returning the result kind and the text.
    /// When <see cref="LineEditResult.ModeSwitch"/> is returned, <paramref name="text"/>
    /// contains the current buffer so the caller can restore it later.
    /// Tab accepts the first command hint; Shift+Tab switches agent mode;
    /// Ctrl+Enter inserts a newline into the buffer; plain Enter submits.
    /// </summary>
    public async Task<(LineEditResult Result, string Text)> ReadLineAsync(CancellationToken ct)
    {
        while (true)
        {
            var keyInfo = await AnsiConsole.Console.Input.ReadKeyAsync(intercept: true, ct);
            if (keyInfo is not { } key)
                continue;

            switch (key.Key)
            {
                case ConsoleKey.Tab:
                    if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
                    {
                        ClearHints();
                        AnsiConsole.WriteLine();
                        return (LineEditResult.ModeSwitch, string.Concat(_buffer));
                    }
                    AcceptFirstHint();
                    break;

                case ConsoleKey.Enter:
                    if ((key.Modifiers & ConsoleModifiers.Control) != 0)
                    {
                        ClearSelection();
                        InsertNewline();
                        break;
                    }
                    ClearHints();
                    AnsiConsole.WriteLine();
                    return (LineEditResult.Submitted, string.Concat(_buffer));

                case ConsoleKey.Backspace:
                    if (HasSelection)
                        DeleteSelection();
                    else
                        HandleBackspace();
                    break;

                case ConsoleKey.Delete:
                    if (HasSelection)
                        DeleteSelection();
                    else
                        HandleDelete();
                    break;

                case ConsoleKey.LeftArrow:
                    if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
                    {
                        _selectionAnchor ??= _cursorPos;
                        MoveCursorLeft();
                    }
                    else
                    {
                        ClearSelection();
                        MoveCursorLeft();
                    }
                    break;

                case ConsoleKey.RightArrow:
                    if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
                    {
                        _selectionAnchor ??= _cursorPos;
                        MoveCursorRight();
                    }
                    else
                    {
                        ClearSelection();
                        MoveCursorRight();
                    }
                    break;

                case ConsoleKey.Home:
                    if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
                    {
                        _selectionAnchor ??= _cursorPos;
                        MoveCursorToStart();
                    }
                    else
                    {
                        ClearSelection();
                        MoveCursorToStart();
                    }
                    break;

                case ConsoleKey.End:
                    if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
                    {
                        _selectionAnchor ??= _cursorPos;
                        MoveCursorToEnd();
                    }
                    else
                    {
                        ClearSelection();
                        MoveCursorToEnd();
                    }
                    break;

                case ConsoleKey.UpArrow:
                    ClearSelection();
                    NavigateHistory(-1);
                    break;

                case ConsoleKey.DownArrow:
                    ClearSelection();
                    NavigateHistory(+1);
                    break;

                case ConsoleKey.C:
                    if ((key.Modifiers & ConsoleModifiers.Control) != 0)
                    {
                        CopyToClipboard();
                        ClearSelection();
                        break;
                    }
                    goto default;

                case ConsoleKey.V:
                    if ((key.Modifiers & ConsoleModifiers.Control) != 0)
                    {
                        await PasteFromClipboardAsync();
                        break;
                    }
                    goto default;

                case ConsoleKey.X:
                    if ((key.Modifiers & ConsoleModifiers.Control) != 0)
                    {
                        CutToClipboard();
                        break;
                    }
                    goto default;

                case ConsoleKey.A:
                    if ((key.Modifiers & ConsoleModifiers.Control) != 0)
                    {
                        SelectAll();
                        break;
                    }
                    goto default;

                default:
                    ClearSelection();
                    await HandleCharacterInput(key, ct);
                    break;
            }
        }
    }

    /// <summary>
    /// Returns the current buffer content (for external snapshot).
    /// </summary>
    public IReadOnlyList<string> Buffer => _buffer;

    // ───────── Visual position tracking ──────────────────────────────────────

    /// <summary>
    /// Returns the visual (row, col) position for the given buffer index.
    /// Row 0 starts at column <see cref="_promptWidth"/>; continuation rows
    /// (after a hard newline) start at <see cref="ContinuationPromptWidth"/>.
    /// Automatic wrap at terminal width is also handled.
    /// </summary>
    private (int Row, int Col) GetVisualPosition(int bufferIndex)
    {
        int termWidth = Math.Max(Console.WindowWidth, 1);
        int row = 0;
        int col = _promptWidth;

        for (int i = 0; i < bufferIndex && i < _buffer.Count; i++)
        {
            if (_buffer[i] == "\n")
            {
                row++;
                col = ContinuationPromptWidth;
            }
            else
            {
                col += GetDisplayWidth(_buffer[i]);
                if (col >= termWidth)
                {
                    row++;
                    col -= termWidth;
                }
            }
        }

        return (row, col);
    }

    // ───────── Terminal cursor movement ──────────────────────────────────────

    /// <summary>
    /// Move the terminal cursor from (fromRow, fromCol) to (toRow, toCol) using ANSI escapes.
    /// Column movement is relative (delta) so any uniform error in the computed column base
    /// (e.g. an emoji rendered wider than GetDisplayWidth predicts) cancels out.
    /// </summary>
    private static void MoveTo(int fromRow, int fromCol, int toRow, int toCol)
    {
        int rowDelta = toRow - fromRow;
        if (rowDelta < 0)
            AnsiConsole.Write($"\x1b[{-rowDelta}A");
        else if (rowDelta > 0)
            AnsiConsole.Write($"\x1b[{rowDelta}B");

        int colDelta = toCol - fromCol;
        if (colDelta < 0)
            AnsiConsole.Write($"\x1b[{-colDelta}D");
        else if (colDelta > 0)
            AnsiConsole.Write($"\x1b[{colDelta}C");
    }

    /// <summary>
    /// Move the terminal cursor from the visual position of <paramref name="fromIndex"/>
    /// to that of <paramref name="toIndex"/>.
    /// </summary>
    private void MoveTerminalCursor(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex) return;
        var (fromRow, fromCol) = GetVisualPosition(fromIndex);
        var (toRow, toCol) = GetVisualPosition(toIndex);
        MoveTo(fromRow, fromCol, toRow, toCol);
    }

    /// <summary>Moves the terminal cursor to the given column on the current row via CR + optional right.</summary>
    private static void MoveToColumn(int col)
    {
        AnsiConsole.Write("\r");
        if (col > 0) AnsiConsole.Write($"\x1b[{col}C");
    }

    /// <summary>
    /// Moves the terminal cursor from its current visual position back to the first
    /// character of the input area (immediately after the prompt).
    /// </summary>
    private void MoveToInputStart()
    {
        var (curRow, curCol) = GetVisualPosition(_cursorPos);
        if (curRow > 0) AnsiConsole.Write($"\x1b[{curRow}A");
        int colDelta = _promptWidth - curCol;
        if (colDelta < 0) AnsiConsole.Write($"\x1b[{-colDelta}D");
        else if (colDelta > 0) AnsiConsole.Write($"\x1b[{colDelta}C");
    }

    // ───────── Buffer display ─────────────────────────────────────────────────

    /// <summary>
    /// Write buffer content from <paramref name="startIndex"/> to end, emitting the
    /// continuation prompt for each embedded newline. After this call the terminal
    /// cursor is at <c>GetVisualPosition(_buffer.Count)</c>.
    /// </summary>
    private void WriteBufferFrom(int startIndex)
    {
        bool cmdMode = _buffer.Count > 0 && _buffer[0] == "/";
        if (cmdMode) AnsiConsole.Write(Ansi.Cyan);

        for (int i = startIndex; i < _buffer.Count; i++)
        {
            if (_buffer[i] == "\n")
            {
                // Dim continuation prompt, then restore command color if needed.
                AnsiConsole.Write("\n" + Ansi.Dim + ContinuationPrompt + Ansi.Reset);
                if (cmdMode) AnsiConsole.Write(Ansi.Cyan);
            }
            else
            {
                AnsiConsole.Write(_buffer[i]);
            }
        }

        if (cmdMode) AnsiConsole.Write(Ansi.Reset);
    }

    /// <summary>
    /// Erase from the current cursor position to end of screen, rewrite the buffer
    /// tail, then restore the terminal cursor to <see cref="_cursorPos"/>.
    /// </summary>
    private void RewriteFromCursor()
    {
        AnsiConsole.Write(Ansi.EraseToEndOfScreen);
        WriteBufferFrom(_cursorPos);
        MoveTerminalCursor(_buffer.Count, _cursorPos);
        UpdateHints();
    }

    /// <summary>
    /// Move terminal cursor to the start of the input area, erase everything,
    /// write new buffer content, and redraw hints.
    /// </summary>
    private void ReplaceBuffer(string newText)
    {
        ClearHints();
        MoveToInputStart();
        AnsiConsole.Write(Ansi.EraseToEndOfScreen);

        _buffer.Clear();
        foreach (var ch in StringToChars(newText))
            _buffer.Add(ch);
        _cursorPos = _buffer.Count;

        WriteBufferFrom(0);
        UpdateHints();
    }

    // ───────── Hints ─────────────────────────────────────────────────────────

    /// <summary>
    /// Moves the terminal cursor to the hint area (below the last input row), invokes
    /// <paramref name="drawLine"/> for each of <paramref name="lineCount"/> rows, then
    /// restores the cursor to its original position. Scroll-safe: uses only relative
    /// row movement, so terminal scrolling during hint rendering cannot corrupt position.
    /// </summary>
    private void DrawInHintArea(int lineCount, Action<int> drawLine)
    {
        var (curRow, curCol) = GetVisualPosition(_cursorPos);
        var (endRow, _) = GetVisualPosition(_buffer.Count);
        int rowsToEnd = endRow - curRow;
        if (rowsToEnd > 0)
            AnsiConsole.Write($"\x1b[{rowsToEnd}B");

        for (int i = 0; i < lineCount; i++)
        {
            AnsiConsole.Write(Ansi.EraseToEndOfLine);
            drawLine(i);
        }

        int totalRowsDown = rowsToEnd + lineCount;
        if (totalRowsDown > 0)
            AnsiConsole.Write($"\x1b[{totalRowsDown}A");
        MoveToColumn(curCol);
    }

    /// <summary>Draw (or refresh) command hint lines below the current input area.</summary>
    private void UpdateHints()
    {
        if (_hintProvider == null) return;

        var bufferText = string.Concat(_buffer);
        var hints = _hintProvider(bufferText);
        int newHintCount = Math.Min(hints.Count, 5);
        int totalLines = Math.Max(newHintCount, _lastHintLineCount);

        if (totalLines == 0) return;

        DrawInHintArea(totalLines, i =>
        {
            if (i >= newHintCount) return;
            var (cmd, desc) = hints[i];

            // First hint gets a highlight indicator; others are indented to align.
            AnsiConsole.Write(i == 0 ? "❯ " : "  ");

            // Matched prefix in bright cyan, unmatched suffix in regular cyan.
            int matchLen = Math.Min(bufferText.Length, cmd.Length);
            AnsiConsole.Write(Ansi.BrightCyan);
            AnsiConsole.Write(SanitizeHintText(cmd[..matchLen]));
            if (matchLen < cmd.Length)
            {
                AnsiConsole.Write(Ansi.Cyan);
                AnsiConsole.Write(SanitizeHintText(cmd[matchLen..]));
            }
            AnsiConsole.Write(Ansi.Reset);

            if (!string.IsNullOrEmpty(desc))
            {
                AnsiConsole.Write("  ");
                AnsiConsole.Write(Ansi.Grey);
                AnsiConsole.Write(Ansi.Dim);
                AnsiConsole.Write(SanitizeHintText(desc));
                AnsiConsole.Write(Ansi.Reset);
            }
        });

        _lastHintLineCount = newHintCount;
    }

    /// <summary>Erase all hint lines previously drawn below the input area.</summary>
    private void ClearHints()
    {
        if (_lastHintLineCount == 0) return;
        int count = _lastHintLineCount;
        _lastHintLineCount = 0;
        DrawInHintArea(count, _ => { });
    }

    /// <summary>Accept the first hint into the buffer, replacing current content.</summary>
    private void AcceptFirstHint()
    {
        if (_hintProvider == null) return;

        var bufferText = string.Concat(_buffer);
        var hints = _hintProvider(bufferText);
        if (hints.Count == 0) return;

        var completed = hints[0].Command;
        if (completed == bufferText) return;

        ReplaceBuffer(completed);
    }

    private static string SanitizeHintText(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
            if (!char.IsControl(c)) sb.Append(c);
        var result = sb.ToString();
        return result.Length > 60 ? result[..60] + "..." : result;
    }

    // ───────── Editing operations ─────────────────────────────────────────────

    /// <summary>
    /// Insert a hard newline at the current cursor position and render the
    /// continuation prompt on the new row.
    /// </summary>
    private void InsertNewline()
    {
        _buffer.Insert(_cursorPos, "\n");

        // Erase old tail starting at the insertion point, then rewrite including the new \n.
        AnsiConsole.Write(Ansi.EraseToEndOfScreen);
        WriteBufferFrom(_cursorPos);

        // Advance logical cursor past the \n.
        _cursorPos++;

        // Terminal cursor is now at buffer end; move it back to new _cursorPos.
        MoveTerminalCursor(_buffer.Count, _cursorPos);
        UpdateHints();
    }

    private void HandleBackspace()
    {
        if (_cursorPos <= 0)
            return;

        var (oldRow, oldCol) = GetVisualPosition(_cursorPos);
        _buffer.RemoveAt(_cursorPos - 1);
        _cursorPos--;
        var (newRow, newCol) = GetVisualPosition(_cursorPos);

        MoveTo(oldRow, oldCol, newRow, newCol);
        RewriteFromCursor();
    }

    private void HandleDelete()
    {
        if (_cursorPos >= _buffer.Count)
            return;

        _buffer.RemoveAt(_cursorPos);
        RewriteFromCursor();
    }

    private void MoveCursorLeft()
    {
        if (_cursorPos <= 0) return;
        int oldPos = _cursorPos;
        _cursorPos--;
        MoveTerminalCursor(oldPos, _cursorPos);
    }

    private void MoveCursorRight()
    {
        if (_cursorPos >= _buffer.Count) return;
        int oldPos = _cursorPos;
        _cursorPos++;
        MoveTerminalCursor(oldPos, _cursorPos);
    }

    private void MoveCursorToStart()
    {
        if (_cursorPos == 0) return;
        int oldPos = _cursorPos;
        _cursorPos = 0;
        MoveTerminalCursor(oldPos, 0);
    }

    private void MoveCursorToEnd()
    {
        if (_cursorPos == _buffer.Count) return;
        int oldPos = _cursorPos;
        _cursorPos = _buffer.Count;
        MoveTerminalCursor(oldPos, _cursorPos);
    }

    // ───────── History ─────────────────────────────────────────────────────────

    private void NavigateHistory(int direction)
    {
        var newIndex = _historyIndex + direction;

        if (direction < 0 && newIndex < 0) return;
        if (direction > 0 && newIndex > _history.Count) return;

        // Save current live input when leaving it for the first time.
        if (_historyIndex == _history.Count)
            _savedCurrentInput = string.Concat(_buffer);

        _historyIndex = newIndex;

        string newText;
        if (_historyIndex == _history.Count)
        {
            newText = _savedCurrentInput ?? string.Empty;
            _savedCurrentInput = null;
        }
        else
        {
            newText = _history[_historyIndex];
        }

        ReplaceBuffer(newText);
    }

    // ───────── Selection ──────────────────────────────────────────────────────

    private void ClearSelection() => _selectionAnchor = null;

    private (int Start, int End)? GetSelectionRange()
    {
        if (_selectionAnchor is not int anchor) return null;
        return (Math.Min(anchor, _cursorPos), Math.Max(anchor, _cursorPos));
    }

    private string GetSelectedText()
    {
        var range = GetSelectionRange();
        if (range is null) return string.Empty;
        var (start, end) = range.Value;
        return string.Concat(_buffer.Skip(start).Take(end - start));
    }

    private bool HasSelection => _selectionAnchor.HasValue;

    // ───────── Clipboard operations ───────────────────────────────────────────

    private void CopyToClipboard()
    {
        var text = HasSelection ? GetSelectedText() : string.Concat(_buffer);
        try
        {
            TextCopy.ClipboardService.SetText(text);
        }
        catch
        {
            // ignored
        }
    }

    private void CutToClipboard()
    {
        CopyToClipboard();
        if (HasSelection)
        {
            DeleteSelection();
        }
        else
        {
            ClearHints();
            MoveToInputStart();
            _buffer.Clear();
            _cursorPos = 0;
            ClearSelection();
            AnsiConsole.Write(Ansi.EraseToEndOfScreen);
            UpdateHints();
        }
    }

    private async Task PasteFromClipboardAsync()
    {
        string? text;
        try
        {
            text = await TextCopy.ClipboardService.GetTextAsync();
            if (string.IsNullOrEmpty(text)) return;
        }
        catch { return; }

        if (HasSelection)
            DeleteSelection();

        // Normalise line endings so embedded newlines become logical lines.
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        var chars = StringToChars(text).ToList();
        _buffer.InsertRange(_cursorPos, chars);

        // Rewrite from insertion point (cursor currently at start of paste).
        RewriteFromCursor();

        // Advance logical and terminal cursor to end of pasted content.
        int oldPos = _cursorPos;
        _cursorPos += chars.Count;
        MoveTerminalCursor(oldPos, _cursorPos);
    }

    private void SelectAll()
    {
        _selectionAnchor = 0;
        MoveCursorToEnd();
    }

    private void DeleteSelection()
    {
        var range = GetSelectionRange();
        if (range is null) return;

        var (start, end) = range.Value;
        var (oldRow, oldCol) = GetVisualPosition(_cursorPos);
        var (startRow, startCol) = GetVisualPosition(start);

        MoveTo(oldRow, oldCol, startRow, startCol);

        _buffer.RemoveRange(start, end - start);
        _cursorPos = start;
        ClearSelection();

        RewriteFromCursor();
    }

    // ───────── Character input ────────────────────────────────────────────────

    private async Task HandleCharacterInput(ConsoleKeyInfo key, CancellationToken ct)
    {
        if (key.KeyChar == '\0') return;

        string ch;
        if (char.IsHighSurrogate(key.KeyChar))
        {
            var lowInfo = await AnsiConsole.Console.Input.ReadKeyAsync(intercept: true, ct);
            if (lowInfo is { } lowKey && char.IsLowSurrogate(lowKey.KeyChar))
                ch = new string([key.KeyChar, lowKey.KeyChar]);
            else
                return;
        }
        else
        {
            ch = key.KeyChar.ToString();
        }

        _buffer.Insert(_cursorPos, ch);
        _cursorPos++;

        bool cmdMode = _buffer.Count > 0 && _buffer[0] == "/";
        AnsiConsole.Write(cmdMode ? Ansi.Cyan + ch + Ansi.Reset : ch);

        if (_cursorPos == _buffer.Count)
        {
            // Fast path: appending at end; just update hints.
            UpdateHints();
        }
        else
        {
            // Inserting in the middle: rewrite the tail.
            RewriteFromCursor();
        }
    }

    // ───────── Wide-character utilities ──────────────────────────────────────

    /// <summary>
    /// Returns the number of terminal columns occupied by the string.
    /// Wide characters (CJK, full-width forms, etc.) count as 2 columns.
    /// </summary>
    internal static int GetDisplayWidth(string s)
    {
        var width = 0;
        var i = 0;
        while (i < s.Length)
        {
            int cp;
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                cp = char.ConvertToUtf32(s[i], s[i + 1]);
                i += 2;
            }
            else
            {
                cp = s[i];
                i++;
            }
            width += IsWideCodePoint(cp) ? 2 : 1;
        }
        return width;
    }

    private static bool IsWideCodePoint(int cp) =>
        // Hangul Jamo
        (cp >= 0x1100 && cp <= 0x115F) ||
        cp is 0x2329 or 0x232A ||
        // Miscellaneous Symbols (⚡ U+26A1, ☀ U+2600, etc.) — most terminals
        // render these with emoji presentation (2 columns).
        (cp >= 0x2600 && cp <= 0x27BF) ||
        // CJK Radicals Supplement .. CJK Symbols and Punctuation
        (cp >= 0x2E80 && cp <= 0x303E) ||
        // Hiragana, Katakana, Bopomofo, CJK Compatibility
        (cp >= 0x3040 && cp <= 0x33FF) ||
        // CJK Unified Ideographs Extension A
        (cp >= 0x3400 && cp <= 0x4DBF) ||
        // CJK Unified Ideographs .. Yi
        (cp >= 0x4E00 && cp <= 0xA4CF) ||
        // Hangul Jamo Extended-A
        (cp >= 0xA960 && cp <= 0xA97F) ||
        // Hangul Syllables .. Hangul Jamo Extended-B
        (cp >= 0xAC00 && cp <= 0xD7FF) ||
        // CJK Compatibility Ideographs
        (cp >= 0xF900 && cp <= 0xFAFF) ||
        // Vertical Forms
        (cp >= 0xFE10 && cp <= 0xFE1F) ||
        // CJK Compatibility Forms
        (cp >= 0xFE30 && cp <= 0xFE4F) ||
        // Fullwidth Forms
        (cp >= 0xFF00 && cp <= 0xFF60) ||
        (cp >= 0xFFE0 && cp <= 0xFFE6) ||
        // Misc Symbols and Pictographs, Emoticons
        (cp >= 0x1F300 && cp <= 0x1F64F) ||
        // Transport and Map Symbols, Supplemental Symbols
        (cp >= 0x1F680 && cp <= 0x1F6FF) ||
        // Supplemental Symbols and Pictographs .. Symbols and Pictographs Extended-A
        (cp >= 0x1F900 && cp <= 0x1FFFF) ||
        // CJK Unified Ideographs Extension B+
        (cp >= 0x20000 && cp <= 0x2FFFD) ||
        (cp >= 0x30000 && cp <= 0x3FFFD);

    /// <summary>
    /// Split a string into logical characters (handling surrogate pairs).
    /// </summary>
    private static IEnumerable<string> StringToChars(string s)
    {
        var i = 0;
        while (i < s.Length)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                yield return new string([s[i], s[i + 1]]);
                i += 2;
            }
            else
            {
                yield return s[i].ToString();
                i++;
            }
        }
    }
}
