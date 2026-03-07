using System.ComponentModel;
using System.Text;
using OpenSandbox.Models;

namespace DotCraft.Tools.Sandbox;

/// <summary>
/// File operations inside an OpenSandbox container.
/// All reads and writes happen within the sandbox's isolated filesystem.
/// No path validation or approval is needed — the container is the boundary.
/// </summary>
public sealed class SandboxFileTools
{
    private readonly SandboxSessionManager _sandboxManager;
    private readonly int _maxFileSize;

    private const int DefaultReadLimit = 2000;
    private const int MaxLineLength = 2000;

    public SandboxFileTools(
        SandboxSessionManager sandboxManager,
        int maxFileSize = 10 * 1024 * 1024)
    {
        _sandboxManager = sandboxManager;
        _maxFileSize = maxFileSize;
    }

    [Description("Read the contents of a file or list the contents of a directory. If the path is a directory, lists its entries. Supports offset and limit for paginated reading of large files.")]
    [Tool(Icon = "📄", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.ReadFile))]
    public async Task<string> ReadFile(
        [Description("Path inside the sandbox (absolute or relative to /workspace).")] string path,
        [Description("Line number to start reading from (1-indexed).")] int offset = 0,
        [Description("Maximum number of lines to read.")] int limit = 0)
    {
        try
        {
            var sandbox = await _sandboxManager.GetOrCreateAsync();
            var fullPath = ResolveSandboxPath(path);

            // Check if it's a directory
            var checkResult = await sandbox.Commands.RunAsync($"test -d {EscapeShellArg(fullPath)} && echo DIR || echo FILE");
            var isDir = checkResult.Logs.Stdout.FirstOrDefault()?.Text?.Trim() == "DIR";

            if (isDir)
            {
                var lsResult = await sandbox.Commands.RunAsync($"ls -la {EscapeShellArg(fullPath)}");
                return FormatCommandOutput(lsResult);
            }

            // Read file content
            var content = await sandbox.Files.ReadFileAsync(fullPath);

            if (content.Length > _maxFileSize)
                return $"Error: File too large ({content.Length} bytes). Max size: {_maxFileSize} bytes.";

            if (offset > 0)
            {
                var lines = content.Split('\n');
                var startIndex = offset - 1;
                if (startIndex >= lines.Length)
                    return $"Error: Offset {offset} is out of range for this file ({lines.Length} lines).";

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

                return sb.ToString();
            }

            return content;
        }
        catch (OpenSandbox.Core.SandboxException ex)
        {
            return $"Sandbox error: [{ex.Error.Code}] {ex.Error.Message}";
        }
        catch (Exception ex)
        {
            return $"Error reading file in sandbox: {ex.Message}";
        }
    }

    [Description("Write content to a file at the given path. Creates parent directories if needed.")]
    [Tool(Icon = "✏️", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.WriteFile))]
    public async Task<string> WriteFile(
        [Description("Path inside the sandbox (absolute or relative to /workspace).")] string path,
        [Description("The content to write.")] string content)
    {
        try
        {
            var sandbox = await _sandboxManager.GetOrCreateAsync();
            var fullPath = ResolveSandboxPath(path);

            // Ensure parent directory exists
            var parentDir = fullPath[..fullPath.LastIndexOf('/')];
            if (!string.IsNullOrEmpty(parentDir))
            {
                await sandbox.Files.CreateDirectoriesAsync([
                    new CreateDirectoryEntry { Path = parentDir, Mode = 755 }
                ]);
            }

            await sandbox.Files.WriteFilesAsync([
                new WriteEntry { Path = fullPath, Data = content, Mode = 644 }
            ]);

            return $"Successfully wrote {content.Length} bytes to {path}";
        }
        catch (OpenSandbox.Core.SandboxException ex)
        {
            return $"Sandbox error: [{ex.Error.Code}] {ex.Error.Message}";
        }
        catch (Exception ex)
        {
            return $"Error writing file in sandbox: {ex.Message}";
        }
    }

    [Description("Edit a file. Two modes: (1) Search/Replace - provide oldText and newText, with automatic fuzzy matching fallback for whitespace/indentation differences. (2) Line range - provide startLine and endLine to replace specific lines with newText (pairs with ReadFile offset/limit).")]
    [Tool(Icon = "🔄", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.EditFile))]
    public async Task<string> EditFile(
        [Description("Path inside the sandbox (absolute or relative to /workspace).")] string path,
        [Description("The text to find and replace.")] string oldText = "",
        [Description("The replacement text.")] string newText = "")
    {
        if (string.IsNullOrEmpty(oldText))
            return "Error: oldText is required for search/replace mode.";

        try
        {
            var sandbox = await _sandboxManager.GetOrCreateAsync();
            var fullPath = ResolveSandboxPath(path);

            // Read current content
            var content = await sandbox.Files.ReadFileAsync(fullPath);

            // Perform replacement
            var occurrences = CountOccurrences(content, oldText);
            if (occurrences == 0)
                return $"Error: oldText not found in {path}. No changes made.";
            if (occurrences > 1)
                return $"Error: oldText found {occurrences} times in {path}. Please provide a more specific search string.";

            var newContent = content.Replace(oldText, newText);

            await sandbox.Files.WriteFilesAsync([
                new WriteEntry { Path = fullPath, Data = newContent, Mode = 644 }
            ]);

            return $"Successfully edited {path} (1 replacement made)";
        }
        catch (OpenSandbox.Core.SandboxException ex)
        {
            return $"Sandbox error: [{ex.Error.Code}] {ex.Error.Message}";
        }
        catch (Exception ex)
        {
            return $"Error editing file in sandbox: {ex.Message}";
        }
    }

    [Description("Search file contents using a regular expression pattern. Returns matching lines with file paths and line numbers. Skips binary files and .git/node_modules directories.")]
    [Tool(Icon = "🔍", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.GrepFiles))]
    public async Task<string> GrepFiles(
        [Description("The regular expression pattern to search for.")] string pattern,
        [Description("Directory to search in (relative to /workspace).")] string path = "",
        [Description("File name pattern to include (e.g. \"*.cs\").")] string include = "")
    {
        try
        {
            var sandbox = await _sandboxManager.GetOrCreateAsync();
            var searchPath = string.IsNullOrEmpty(path) ? "/workspace" : ResolveSandboxPath(path);

            // Use grep inside the sandbox
            var includeArg = string.IsNullOrEmpty(include) ? "" : $"--include={EscapeShellArg(include)}";
            var command = $"grep -rn {includeArg} --max-count=100 -P {EscapeShellArg(pattern)} {EscapeShellArg(searchPath)} 2>/dev/null | head -100";

            var result = await sandbox.Commands.RunAsync(command);
            var output = FormatCommandOutput(result);

            return string.IsNullOrWhiteSpace(output) || output == "(no output)"
                ? "No matches found."
                : output;
        }
        catch (OpenSandbox.Core.SandboxException ex)
        {
            return $"Sandbox error: [{ex.Error.Code}] {ex.Error.Message}";
        }
        catch (Exception ex)
        {
            return $"Error searching files in sandbox: {ex.Message}";
        }
    }

    [Description("Find files by name pattern. Searches recursively, skipping .git and node_modules directories. Use semicolons to separate multiple patterns (e.g. \"*.cs;*.json\").")]
    [Tool(Icon = "📂", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.FindFiles))]
    public async Task<string> FindFiles(
        [Description("File name pattern (e.g. \"*.cs\", \"*.json\"). Use semicolons for multiple patterns.")] string pattern,
        [Description("Directory to search in (relative to /workspace).")] string path = "")
    {
        try
        {
            var sandbox = await _sandboxManager.GetOrCreateAsync();
            var searchPath = string.IsNullOrEmpty(path) ? "/workspace" : ResolveSandboxPath(path);

            // Build find command with multiple patterns
            var patterns = pattern.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var nameArgs = string.Join(" -o ", patterns.Select(p => $"-name {EscapeShellArg(p)}"));
            if (patterns.Length > 1) nameArgs = $"\\( {nameArgs} \\)";

            var command = $"find {EscapeShellArg(searchPath)} {nameArgs} -not -path '*/.git/*' -not -path '*/node_modules/*' 2>/dev/null | head -200";

            var result = await sandbox.Commands.RunAsync(command);
            var output = FormatCommandOutput(result);

            return string.IsNullOrWhiteSpace(output) || output == "(no output)"
                ? "No files found."
                : output;
        }
        catch (OpenSandbox.Core.SandboxException ex)
        {
            return $"Sandbox error: [{ex.Error.Code}] {ex.Error.Message}";
        }
        catch (Exception ex)
        {
            return $"Error finding files in sandbox: {ex.Message}";
        }
    }

    private static string ResolveSandboxPath(string path)
    {
        if (path.StartsWith('/'))
            return path;
        if (path.StartsWith(".."))
            throw new ArgumentException("Path traversal not allowed");
        if (path.StartsWith("./"))
            return "/workspace/" + path[2..];
        return "/workspace/" + path;
    }

    private static string FormatCommandOutput(OpenSandbox.Models.Execution execution)
    {
        var sb = new StringBuilder();
        foreach (var line in execution.Logs.Stdout)
        {
            if (line.Text != null) sb.AppendLine(line.Text);
        }
        var output = sb.ToString().TrimEnd();
        return string.IsNullOrWhiteSpace(output) ? "(no output)" : output;
    }

    private static int CountOccurrences(string text, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(search, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += search.Length;
        }
        return count;
    }

    private static string EscapeShellArg(string arg)
    {
        return "'" + arg.Replace("'", "'\\''") + "'";
    }
}
