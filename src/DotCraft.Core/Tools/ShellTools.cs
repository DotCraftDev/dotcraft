using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotCraft.Security;

namespace DotCraft.Tools;

/// <summary>
/// Shell command execution with safety guards.
/// </summary>
public sealed class ShellTools
{
    private readonly string _workingDirectory;
    
    private readonly int _timeoutSeconds;
    
    private readonly List<Regex> _denyPatterns;
    
    private readonly bool _requireApprovalOutsideWorkspace;
    
    private readonly int _maxOutputLength;
    
    private readonly IApprovalService? _approvalService;

    private readonly PathBlacklist? _blacklist;

    private readonly ShellCommandInspector _inspector;

    public ShellTools(
        string workingDirectory,
        int timeoutSeconds = 60,
        bool requireApprovalOutsideWorkspace = true,
        int maxOutputLength = 10000,
        IEnumerable<string>? denyPatterns = null,
        IApprovalService? approvalService = null,
        PathBlacklist? blacklist = null)
    {
        _workingDirectory = Path.GetFullPath(workingDirectory);
        _timeoutSeconds = timeoutSeconds;
        _requireApprovalOutsideWorkspace = requireApprovalOutsideWorkspace;
        _maxOutputLength = maxOutputLength;
        _approvalService = approvalService;
        _blacklist = blacklist;
        _inspector = new ShellCommandInspector(_workingDirectory);

        var defaultDenyPatterns = new[]
        {
            @"\brm\s+-[rf]{1,2}\b",             // rm -r, rm -rf (bash / PowerShell alias)
            @"\bRemove-Item\b.*-Recurse\b",      // PowerShell Remove-Item -Recurse
            @"\bdel\s+/[fqs]\b",                // del /f, del /q, del /s (cmd.exe)
            @"\brmdir\s+/s\b",                  // rmdir /s (cmd.exe)
            @"\b(format|mkfs|diskpart)\b",      // disk operations
            @"\b(Clear-Disk|Format-Volume|Initialize-Disk)\b", // PowerShell disk operations
            @"\bdd\s+if=",                      // dd
            @">\s*/dev/sd",                     // write to disk
            @"\b(shutdown|reboot|poweroff)\b",  // system power (bash)
            @"\b(Stop-Computer|Restart-Computer)\b", // PowerShell system power
            @":\(\)\s*\{.*\};\s*:",             // fork bomb (bash)
        };

        _denyPatterns = (denyPatterns ?? defaultDenyPatterns)
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
            .ToList();
    }

    [Description("Execute a shell command and return its output.")]
    [Tool(Icon = "⌨️", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.Exec), MaxResultChars = 30_000)]
    public async Task<string> Exec(
        [Description("The shell command to execute.")] string command,
        [Description("Optional working directory for the command.")] string? workingDir = null)
    {
        var cwd = !string.IsNullOrWhiteSpace(workingDir)
            ? Path.GetFullPath(workingDir)
            : _workingDirectory;

        var guardError = await GuardCommandAsync(command, cwd);
        if (guardError != null)
            return guardError;

        try
        {
            var isWindows = OperatingSystem.IsWindows();

            var psi = new ProcessStartInfo
            {
                WorkingDirectory = cwd,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            if (isWindows)
            {
                var script = "$ProgressPreference = 'SilentlyContinue'\n[Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" + command;
                var bytes = Encoding.Unicode.GetBytes(script);
                var encoded = Convert.ToBase64String(bytes);
                psi.FileName = "powershell.exe";
                psi.Arguments = $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encoded}";
            }
            else
            {
                psi.FileName = "/bin/bash";
            }

            using var process = Process.Start(psi);
            if (process == null)
                return "Error: Failed to start process.";

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) outputBuilder.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) errorBuilder.AppendLine(e.Data);
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!isWindows)
            {
                await process.StandardInput.WriteLineAsync(command);
                process.StandardInput.Close();
            }

            var completed = await process.WaitForExitAsync(
                TimeSpan.FromSeconds(_timeoutSeconds));

            if (!completed)
            {
                process.Kill(entireProcessTree: true);
                return $"Error: Command timed out after {_timeoutSeconds} seconds.";
            }

            var result = new StringBuilder();
            var stdout = outputBuilder.ToString().TrimEnd();
            var stderr = isWindows
                ? SanitizePowerShellStderr(errorBuilder.ToString().TrimEnd())
                : errorBuilder.ToString().TrimEnd();

            if (!string.IsNullOrWhiteSpace(stdout))
                result.Append(stdout);

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                if (result.Length > 0) result.AppendLine();
                result.AppendLine("STDERR:");
                result.Append(stderr);
            }

            if (process.ExitCode != 0)
            {
                if (result.Length > 0) result.AppendLine();
                result.AppendLine($"Exit code: {process.ExitCode}");
            }

            var output = result.Length > 0 ? result.ToString() : "(no output)";

            if (output.Length > _maxOutputLength)
            {
                output = output[.._maxOutputLength]
                         + $"\n... (truncated, {output.Length - _maxOutputLength} more chars)";
            }

            return output;
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}";
        }
    }

    /// <summary>
    /// Parses PowerShell CLIXML stderr output and extracts only the human-readable error text.
    /// Progress records are stripped entirely; error strings are extracted and decoded.
    /// Falls back to regex-based stripping if XML parsing fails.
    /// </summary>
    private static string SanitizePowerShellStderr(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
            return stderr;

        // Only process CLIXML-formatted output
        var trimmed = stderr.TrimStart('\r', '\n');
        if (!trimmed.StartsWith("#< CLIXML", StringComparison.Ordinal))
            return stderr;

        try
        {
            // Strip the CLIXML header line and parse the XML body
            var xmlStart = trimmed.IndexOf('<');
            if (xmlStart < 0)
                return string.Empty;

            var xml = trimmed[xmlStart..];
            var doc = XDocument.Parse(xml);

            XNamespace ns = "http://schemas.microsoft.com/powershell/2004/04";
            var errors = doc.Descendants(ns + "S")
                .Where(e => (string?)e.Attribute("S") == "Error")
                .Select(e => DecodeCLIXMLString(e.Value))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            return errors.Count > 0
                ? string.Join(Environment.NewLine, errors).TrimEnd()
                : string.Empty;
        }
        catch
        {
            // Fallback: strip the CLIXML header and any XML blocks, keep other lines
            var lines = stderr.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var kept = lines
                .Where(l => !l.TrimStart().StartsWith("#< CLIXML", StringComparison.Ordinal)
                         && !l.TrimStart().StartsWith('<'))
                .ToArray();
            return kept.Length > 0 ? string.Join(Environment.NewLine, kept) : string.Empty;
        }
    }

    /// <summary>
    /// Decodes CLIXML escape sequences such as _x000D__x000A_ (CR+LF) back to their characters.
    /// </summary>
    private static string DecodeCLIXMLString(string value)
    {
        // Replace encoded carriage-return/linefeed pairs with a single newline
        value = value.Replace("_x000D__x000A_", "\n");

        // Decode remaining _xHHHH_ Unicode escapes
        return Regex.Replace(value, @"_x([0-9A-Fa-f]{4})_", m =>
        {
            var codePoint = Convert.ToInt32(m.Groups[1].Value, 16);
            return ((char)codePoint).ToString();
        });
    }

    private async Task<string?> GuardCommandAsync(string command, string cwd)
    {
        var normalized = command.Trim();
        var lower = normalized.ToLowerInvariant();

        foreach (var pattern in _denyPatterns)
        {
            if (pattern.IsMatch(lower))
                return "Error: Command blocked by safety guard (dangerous pattern detected).";
        }

        if (_blacklist != null && _blacklist.CommandReferencesBlacklistedPath(command))
        {
            return "Error: Command references a blacklisted path and cannot be executed.";
        }

        var hasPathTraversal = normalized.Contains("..\\") || normalized.Contains("../");
        
        var cwdPath = new DirectoryInfo(cwd).FullName;
        var workspace = new DirectoryInfo(_workingDirectory).FullName;
        var isOutsideWorkspace = !cwdPath.StartsWith(workspace, StringComparison.OrdinalIgnoreCase);

        // Detect absolute, ~, and $HOME paths that resolve outside the workspace
        var outsidePaths = _inspector.DetectOutsideWorkspacePaths(command);
        var referencesOutsidePaths = outsidePaths.Count > 0;

        if (hasPathTraversal || isOutsideWorkspace || referencesOutsidePaths)
        {
            if (!_requireApprovalOutsideWorkspace)
            {
                if (referencesOutsidePaths)
                    return $"Error: Command references paths outside workspace: {string.Join(", ", outsidePaths)}";
                if (hasPathTraversal)
                    return "Error: Command blocked by safety guard (path traversal detected).";
                if (isOutsideWorkspace)
                    return "Error: Working directory is outside workspace boundary.";
            }
            
            if (_approvalService != null)
            {
                var context = ApprovalContextScope.Current;
                var approved = await _approvalService.RequestShellApprovalAsync(command, cwd, context);
                if (!approved)
                {
                    return "Error: Command execution was rejected by user.";
                }
            }
        }

        return null;
    }
}

file static class ProcessExtensions
{
    public static async Task<bool> WaitForExitAsync(this Process process, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
