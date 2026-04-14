using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using DotCraft.Lsp;
using DotCraft.Security;
using Microsoft.Extensions.AI;

namespace DotCraft.Tools;

/// <summary>
/// File system tools: read, write, edit, search files with safety guards.
/// </summary>
public sealed class FileTools(
    string workspaceRoot,
    bool requireApprovalOutsideWorkspace = true,
    int maxFileSize = 10 * 1024 * 1024,
    IApprovalService? approvalService = null,
    PathBlacklist? blacklist = null,
    IReadOnlyList<string>? trustedReadPaths = null,
    LspServerManager? lspServerManager = null)
{
    private const int DefaultReadLimit = 2000;
    
    private const int MaxLineLength = 2000;
    
    private const int MaxGrepMatches = 100;
    
    private const int MaxFindResults = 200;
    
    private const int MaxGrepFileSize = 5 * 1024 * 1024;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".tar", ".gz", ".7z", ".rar", ".bz2", ".xz",
        ".exe", ".dll", ".so", ".dylib", ".pdb",
        ".class", ".jar", ".war",
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".webp", ".tiff",
        ".mp3", ".mp4", ".avi", ".mkv", ".wav", ".flac", ".ogg", ".webm",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".odt", ".ods", ".odp",
        ".bin", ".dat", ".obj", ".o", ".a", ".lib",
        ".wasm", ".pyc", ".pyo",
        ".ttf", ".otf", ".woff", ".woff2", ".eot",
        ".db", ".sqlite", ".mdb", ".ldb"
    };

    private static readonly HashSet<string> SkipDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules"
    };

    /// <summary>
    /// Image extensions returned as <see cref="DataContent"/> for vision models (subset of <see cref="BinaryExtensions"/>).
    /// </summary>
    private static readonly Dictionary<string, string> ImageExtensionToMediaType = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".bmp"] = "image/bmp",
    };

    private readonly string _workspaceRoot = Path.GetFullPath(workspaceRoot);
    private readonly FileAccessGuard _fileAccessGuard = new(
        workspaceRoot,
        requireApprovalOutsideWorkspace,
        approvalService,
        blacklist,
        trustedReadPaths);

    [Description("Read the contents of a file or list the contents of a directory. If the path is a directory, lists its entries. Supports offset and limit for paginated reading of large text files. Image files (.png, .jpg, .jpeg, .gif, .webp, .bmp) are returned as vision input for the model (full file only; offset/limit do not apply).")]
    [Tool(Icon = "📄", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.ReadFile), MaxResultChars = 0)]
    public async Task<IList<AIContent>> ReadFile(
        [Description("The workspace-relative or absolute path to read.")] string path,
        [Description("The line number to start reading from (1-indexed). Enables line-numbered output when set.")] int offset = 0,
        [Description("The maximum number of lines to read (defaults to 2000 when offset is used).")] int limit = 0)
    {
        try
        {
            var fullPath = ResolvePath(path);
            var validateResult = await ValidatePathAsync(fullPath, "read", path);
            if (validateResult != null)
                return ReadFileTextResult(validateResult);

            if (Directory.Exists(fullPath))
                return ReadFileTextResult(FormatDirectoryListing(fullPath, path));

            if (!File.Exists(fullPath))
                return ReadFileTextResult($"Error: File not found: {path}");

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length > maxFileSize)
                return ReadFileTextResult($"Error: File too large ({fileInfo.Length} bytes). Max size: {maxFileSize} bytes.");

            if (TryGetImageMediaType(fullPath, out var mediaType))
            {
                if (offset > 0)
                {
                    return ReadFileTextResult(
                        "Error: Line offset/limit pagination is not supported for image files; call ReadFile without offset and limit to load the image as vision input.");
                }

                var bytes = await File.ReadAllBytesAsync(fullPath);
                var summary = $"Image: {path} ({bytes.Length:N0} bytes, {mediaType})";
                return [new TextContent(summary), new DataContent(bytes, mediaType)];
            }

            var encoding = DetectFileEncoding(fullPath);

            if (offset > 0)
            {
                var lines = await File.ReadAllLinesAsync(fullPath, encoding);
                var startIndex = offset - 1;
                if (startIndex >= lines.Length)
                    return ReadFileTextResult($"Error: Offset {offset} is out of range for this file ({lines.Length} lines).");

                var readLimit = limit > 0 ? limit : DefaultReadLimit;
                var endIndex = Math.Min(lines.Length, startIndex + readLimit);
                var sb = new StringBuilder();
                for (var i = startIndex; i < endIndex; i++)
                {
                    var line = lines[i].Length > MaxLineLength
                        ? lines[i][..MaxLineLength] + "..."
                        : lines[i];
                    sb.AppendLine($"{i + 1}: {line}");
                }

                if (endIndex < lines.Length)
                    sb.AppendLine($"\n(Showing lines {offset}-{endIndex} of {lines.Length}. Use offset={endIndex + 1} to read more.)");
                else
                    sb.AppendLine($"\n(End of file - total {lines.Length} lines)");

                return ReadFileTextResult(sb.ToString());
            }

            return ReadFileTextResult(await File.ReadAllTextAsync(fullPath, encoding));
        }
        catch (UnauthorizedAccessException)
        {
            return ReadFileTextResult($"Error: Permission denied: {path}");
        }
        catch (Exception ex)
        {
            return ReadFileTextResult($"Error reading file: {ex.Message}");
        }
    }

    private static IList<AIContent> ReadFileTextResult(string text) => [new TextContent(text)];

    private static bool TryGetImageMediaType(string fullPath, out string mediaType)
    {
        var ext = Path.GetExtension(fullPath);
        return ImageExtensionToMediaType.TryGetValue(ext, out mediaType!);
    }

    [Description("Write content to a file at the given path. Creates parent directories if needed.")]
    [Tool(Icon = "✏️", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.WriteFile))]
    public async Task<string> WriteFile(
        [Description("The workspace-relative or absolute file path to write to.")] string path,
        [Description("The content to write.")] string content)
    {
        try
        {
            var fullPath = ResolvePath(path);
            var validateResult = await ValidatePathAsync(fullPath, "write", path);
            if (validateResult != null)
                return validateResult;

            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var encoding = File.Exists(fullPath) ? DetectFileEncoding(fullPath) : Utf8NoBom;
            if (File.Exists(fullPath))
            {
                var existing = await File.ReadAllTextAsync(fullPath, encoding);
                content = RestoreLineEndings(NormalizeToLf(content), UsesCrLf(existing));
            }
            else
            {
                content = NormalizeToLf(content);
            }
            await File.WriteAllTextAsync(fullPath, content, encoding);
            await NotifyLspFileChangedAsync(fullPath, content);
            var lineCount = content.Split('\n').Length;
            return $"Successfully wrote {content.Length} bytes ({lineCount} lines) to {path}";
        }
        catch (UnauthorizedAccessException)
        {
            return $"Error: Permission denied: {path}";
        }
        catch (Exception ex)
        {
            return $"Error writing file: {ex.Message}";
        }
    }

    [Description("Replace text in a file: provide oldText (snippet to find) and newText. Prefer a minimal unique snippet (typically 3-8 lines including surrounding context) to save tokens; do not paste entire files unless rewriting. For new files or full rewrites use WriteFile. When replaceAll is false (default), matching tries exact text first, then fuzzy fallbacks (line trim, indentation, collapsed whitespace, Unicode punctuation); oldText must match exactly one location unless you set replaceAll to true. Use replaceAll to replace every exact occurrence at once (e.g. renaming a symbol across the file).")]
    [Tool(Icon = "🔄", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.EditFile))]
    public async Task<string> EditFile(
        [Description("The workspace-relative or absolute file path to edit.")] string path,
        [Description("The exact snippet from the file to replace. Include enough surrounding lines to be unique when replaceAll is false.")] string oldText = "",
        [Description("The replacement text.")] string newText = "",
        [Description("If true, replace all exact occurrences of oldText (no fuzzy matching). Defaults to false.")] bool replaceAll = false)
    {
        try
        {
            var fullPath = ResolvePath(path);
            var validateResult = await ValidatePathAsync(fullPath, "edit", path);
            if (validateResult != null)
                return validateResult;

            if (!File.Exists(fullPath))
                return $"Error: File not found: {path}";

            var encoding = DetectFileEncoding(fullPath);
            var content = await File.ReadAllTextAsync(fullPath, encoding);
            newText = UnescapeUnicodeSequences(newText);

            if (string.IsNullOrEmpty(oldText))
                return "Error: oldText is required. Provide the exact snippet to find and replace.";

            oldText = UnescapeUnicodeSequences(oldText);
            var result = await ApplySearchReplaceEdit(fullPath, path, content, oldText, newText, encoding, replaceAll);
            if (result.StartsWith("Successfully", StringComparison.Ordinal))
            {
                var latest = await File.ReadAllTextAsync(fullPath, encoding);
                await NotifyLspFileChangedAsync(fullPath, latest);
            }

            return result;
        }
        catch (UnauthorizedAccessException)
        {
            return $"Error: Permission denied: {path}";
        }
        catch (Exception ex)
        {
            return $"Error editing file: {ex.Message}";
        }
    }

    [Description("Search file contents using a regular expression pattern. Returns matching lines with file paths and line numbers. Skips binary files and .git/node_modules directories. For open-ended searches requiring multiple rounds or broad codebase exploration, use SpawnSubagent instead.")]
    [Tool(Icon = "🔍", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.GrepFiles), MaxResultChars = 20_000)]
    public async Task<string> GrepFiles(
        [Description("The regular expression pattern to search for.")] string pattern,
        [Description("The directory to search in. Defaults to workspace root.")] string path = "",
        [Description("File name pattern to include (e.g. \"*.cs\", \"*.json\"). Searches all text files if not specified.")] string include = "")
    {
        try
        {
            var searchPath = string.IsNullOrEmpty(path) ? _workspaceRoot : ResolvePath(path);
            var validateResult = await ValidatePathAsync(searchPath, "read", string.IsNullOrEmpty(path) ? "." : path);
            if (validateResult != null)
                return validateResult;

            if (!Directory.Exists(searchPath))
                return $"Error: Directory not found: {path}";

            Regex regex;
            try
            {
                regex = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromSeconds(5));
            }
            catch (ArgumentException ex)
            {
                return $"Error: Invalid regex pattern: {ex.Message}";
            }

            var includePattern = string.IsNullOrEmpty(include) ? null : include;
            var files = EnumerateSearchableFiles(searchPath, includePattern);
            var matches = new List<(string FilePath, int LineNum, string LineText)>();
            var totalMatches = 0;

            foreach (var filePath in files)
            {
                if (totalMatches >= MaxGrepMatches)
                    break;

                try
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length > MaxGrepFileSize || fileInfo.Length == 0)
                        continue;

                    if (IsBinaryFile(filePath))
                        continue;

                    var lines = await File.ReadAllLinesAsync(filePath, DetectFileEncoding(filePath));
                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (regex.IsMatch(lines[i]))
                        {
                            totalMatches++;
                            matches.Add((filePath, i + 1, lines[i]));
                            if (totalMatches >= MaxGrepMatches)
                                break;
                        }
                    }
                }
                catch
                {
                    // ignored
                }
            }

            if (matches.Count == 0)
                return "No matches found.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {matches.Count} matches{(totalMatches >= MaxGrepMatches ? $" (showing first {MaxGrepMatches}, there may be more)" : "")}:");

            var currentFile = "";
            foreach (var match in matches)
            {
                var relativePath = Path.GetRelativePath(searchPath, match.FilePath);
                if (currentFile != relativePath)
                {
                    if (currentFile != "")
                        sb.AppendLine();
                    currentFile = relativePath;
                    sb.AppendLine($"{relativePath}:");
                }
                var lineText = match.LineText.Length > MaxLineLength
                    ? match.LineText[..MaxLineLength] + "..."
                    : match.LineText;
                sb.AppendLine($"  Line {match.LineNum}: {lineText}");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error searching files: {ex.Message}";
        }
    }

    [Description("Find files by name pattern. Searches recursively, skipping .git and node_modules directories. Use semicolons to separate multiple patterns (e.g. \"*.cs;*.json\"). When you need to explore an unfamiliar codebase structure with multiple rounds of discovery, consider using SpawnSubagent instead.")]
    [Tool(Icon = "📂", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.FindFiles))]
    public async Task<string> FindFiles(
        [Description("The file name pattern to match (e.g. \"*.cs\", \"*.json\"). Use semicolons for multiple patterns.")] string pattern,
        [Description("The directory to search in. Defaults to workspace root.")] string path = "")
    {
        try
        {
            var searchPath = string.IsNullOrEmpty(path) ? _workspaceRoot : ResolvePath(path);
            var validateResult = await ValidatePathAsync(searchPath, "read", string.IsNullOrEmpty(path) ? "." : path);
            if (validateResult != null)
                return validateResult;

            if (!Directory.Exists(searchPath))
                return $"Error: Directory not found: {path}";

            var patterns = pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in patterns)
            {
                foreach (var f in EnumerateFilesRecursive(searchPath, p))
                    files.Add(f);
            }

            var sorted = files
                .Select(f =>
                {
                    try { return (Path: f, ModTime: File.GetLastWriteTimeUtc(f)); }
                    catch { return (Path: f, ModTime: DateTime.MinValue); }
                })
                .OrderByDescending(f => f.ModTime)
                .Take(MaxFindResults)
                .ToList();

            if (sorted.Count == 0)
                return "No files found.";

            var truncated = files.Count > MaxFindResults;
            var sb = new StringBuilder();
            sb.AppendLine($"Found {files.Count} files{(truncated ? $" (showing first {MaxFindResults})" : "")}:");
            foreach (var f in sorted)
            {
                sb.AppendLine(Path.GetRelativePath(searchPath, f.Path));
            }

            if (truncated)
                sb.AppendLine($"\n(Results truncated. Consider using a more specific path or pattern.)");

            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error finding files: {ex.Message}";
        }
    }

    #region Private Helpers

    /// <summary>
    /// Detect file encoding by inspecting the BOM (Byte Order Mark).
    /// Falls back to UTF-8 without BOM when no BOM is found.
    /// </summary>
    private static Encoding DetectFileEncoding(string filePath)
    {
        Span<byte> bom = stackalloc byte[4];
        int bytesRead;
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            bytesRead = fs.Read(bom);
        }

        // UTF-32 BE: 00 00 FE FF (check before UTF-16 BE)
        if (bytesRead >= 4 && bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xFE && bom[3] == 0xFF)
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);

        // UTF-32 LE: FF FE 00 00 (check before UTF-16 LE)
        if (bytesRead >= 4 && bom[0] == 0xFF && bom[1] == 0xFE && bom[2] == 0x00 && bom[3] == 0x00)
            return new UTF32Encoding(bigEndian: false, byteOrderMark: true);

        // UTF-8 BOM: EF BB BF
        if (bytesRead >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        // UTF-16 LE: FF FE
        if (bytesRead >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
            return Encoding.Unicode;

        // UTF-16 BE: FE FF
        if (bytesRead >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        return Utf8NoBom;
    }

    private static string FormatDirectoryListing(string fullPath, string originalPath)
    {
        var items = Directory.GetFileSystemEntries(fullPath)
            .OrderBy(x => x)
            .Select(x =>
            {
                var name = Path.GetFileName(x);
                var prefix = Directory.Exists(x) ? "[DIR] " : "[FILE] ";
                return $"{prefix}{name}";
            });

        var result = string.Join("\n", items);
        return string.IsNullOrWhiteSpace(result) ? $"Directory {originalPath} is empty" : result;
    }

    private static IEnumerable<string> EnumerateSearchableFiles(string rootPath, string? includePattern)
    {
        if (!string.IsNullOrEmpty(includePattern))
        {
            var patterns = includePattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var p in patterns)
            {
                foreach (var f in EnumerateFilesRecursive(rootPath, p))
                    yield return f;
            }
        }
        else
        {
            foreach (var f in EnumerateFilesRecursive(rootPath))
                yield return f;
        }
    }

    private static IEnumerable<string> EnumerateFilesRecursive(string rootPath, string searchPattern = "*")
    {
        var dirs = new Stack<string>();
        dirs.Push(rootPath);

        while (dirs.Count > 0)
        {
            var dir = dirs.Pop();

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(dir, searchPattern);
            }
            catch { continue; }

            foreach (var file in files)
                yield return file;

            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(dir))
                {
                    var dirName = Path.GetFileName(subDir);
                    if (SkipDirectories.Contains(dirName))
                        continue;
                    dirs.Push(subDir);
                }
            }
            catch
            {
                // ignored
            }
        }
    }

    private static bool IsBinaryFile(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return BinaryExtensions.Contains(ext);
    }

    private string ResolvePath(string path)
        => _fileAccessGuard.ResolvePath(path);

    private static readonly Regex UnicodeEscapeRegex = new(@"\\u([0-9a-fA-F]{4})", RegexOptions.Compiled);

    private static string UnescapeUnicodeSequences(string input)
    {
        if (!input.Contains("\\u"))
            return input;

        return UnicodeEscapeRegex.Replace(input, match =>
            ((char)Convert.ToInt32(match.Groups[1].Value, 16)).ToString());
    }

    private async Task<string?> ValidatePathAsync(string fullPath, string operation, string originalPath)
        => await _fileAccessGuard.ValidatePathAsync(fullPath, operation, originalPath);

    private async Task NotifyLspFileChangedAsync(string fullPath, string content)
    {
        if (lspServerManager == null)
            return;

        try
        {
            await lspServerManager.ChangeFileAsync(fullPath, content);
            await lspServerManager.SaveFileAsync(fullPath);
        }
        catch
        {
            // LSP sync is best-effort and should not fail write/edit operations.
        }
    }

    private static async Task<string> ApplySearchReplaceEdit(
        string fullPath, string displayPath, string content, string oldText, string newText,
        Encoding encoding, bool replaceAll)
    {
        // Normalize all inputs to LF for consistent matching, restore on write
        var useCrLf = UsesCrLf(content);
        content = NormalizeToLf(content);
        oldText = NormalizeToLf(oldText);
        newText = NormalizeToLf(newText);

        var (ok, newLfContent, error, matchKind, lineNum, oldLineCount, replaceCount) =
            FileEditSearchReplace.Apply(content, oldText, newText, replaceAll);
        if (!ok)
            return error!;

        var newContent = RestoreLineEndings(newLfContent, useCrLf);
        await File.WriteAllTextAsync(fullPath, newContent, encoding);

        if (replaceCount > 1)
            return $"Successfully replaced {replaceCount} occurrences in {displayPath}";

        var newLineCount = string.IsNullOrEmpty(newText) ? 0 : newText.Count(c => c == '\n') + 1;
        var suffix = matchKind != null ? $" ({matchKind})" : "";
        return $"Successfully edited {displayPath} at line {lineNum} ({oldLineCount} -> {newLineCount} lines){suffix}";
    }

    private static bool UsesCrLf(string content)
        => content.Contains("\r\n");

    private static string NormalizeToLf(string content)
        => content.Replace("\r\n", "\n");

    private static string RestoreLineEndings(string content, bool useCrLf)
        => useCrLf ? content.Replace("\n", "\r\n") : content;

    #endregion
}
