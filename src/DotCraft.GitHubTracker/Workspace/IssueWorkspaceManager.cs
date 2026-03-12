using System.Diagnostics;
using System.Text.RegularExpressions;
using DotCraft.Configuration;
using DotCraft.GitHubTracker.Tracker;
using Microsoft.Extensions.Logging;

namespace DotCraft.GitHubTracker.Workspace;

/// <summary>
/// Manages per-issue workspace lifecycle: creation, reuse, hooks, and cleanup.
/// Enforces safety invariants per SPEC.md Section 9.
/// </summary>
public sealed partial class IssueWorkspaceManager(GitHubTrackerConfig config, ILogger<IssueWorkspaceManager> logger)
{
    private static readonly Regex SafeChars = GenerateSafeCharsRegex();

    private readonly string _workspaceRoot = ResolveWorkspaceRoot(config.Workspace.Root);
    private readonly GitHubTrackerTrackerConfig _trackerConfig = config.Tracker;
    private readonly GitHubTrackerHooksConfig _hooksConfig = config.Hooks;

    public string WorkspaceRoot => _workspaceRoot;

    /// <summary>
    /// Ensure a workspace directory exists for the given work item.
    /// For pull requests, checks out the PR's head branch after clone.
    /// Runs after_create hook if the directory was newly created.
    /// </summary>
    public async Task<IssueWorkspace> EnsureWorkspaceAsync(TrackedIssue workItem, CancellationToken ct = default)
    {
        var issueIdentifier = workItem.Identifier;
        var key = SanitizeIdentifier(issueIdentifier);
        var path = Path.GetFullPath(Path.Combine(_workspaceRoot, key));

        ValidatePathSafety(path);

        var isNew = !Directory.Exists(path);
        Directory.CreateDirectory(path);

        var gitDir = Path.Combine(path, ".git");
        var needsClone = !Directory.Exists(gitDir) && !string.IsNullOrWhiteSpace(_trackerConfig.Repository);
        if (needsClone)
        {
            try
            {
                await CloneRepositoryAsync(path, _trackerConfig, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Repository clone failed for {Identifier}, continuing without clone", issueIdentifier);
            }
        }

        // For pull requests, fetch and checkout the PR's head branch.
        if (workItem.Kind == WorkItemKind.PullRequest && !string.IsNullOrWhiteSpace(workItem.HeadBranch))
        {
            try
            {
                await CheckoutPullRequestBranchAsync(path, workItem.HeadBranch, _trackerConfig, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "PR branch checkout failed for {Identifier}, staying on default branch", issueIdentifier);
            }
        }

        Directory.CreateDirectory(Path.Combine(path, ".craft"));

        if (isNew && !string.IsNullOrWhiteSpace(_hooksConfig.AfterCreate))
        {
            try
            {
                await RunHookAsync(_hooksConfig.AfterCreate, path, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "after_create hook failed for {Identifier}, removing workspace", issueIdentifier);
                try { Directory.Delete(path, true); } catch { /* best effort */ }
                throw;
            }
        }

        return new IssueWorkspace(path);
    }

    /// <summary>
    /// Overload for simple identifier-based calls (backward compat for cleanup, etc.).
    /// </summary>
    public Task<IssueWorkspace> EnsureWorkspaceAsync(string issueIdentifier, CancellationToken ct = default)
    {
        var placeholder = new TrackedIssue
        {
            Id = issueIdentifier,
            Identifier = issueIdentifier,
            Title = "",
            State = "",
            Kind = WorkItemKind.Issue,
        };
        return EnsureWorkspaceAsync(placeholder, ct);
    }

    /// <summary>
    /// Remove a workspace directory for the given issue.
    /// Runs before_remove hook first.
    /// </summary>
    public async Task CleanWorkspaceAsync(string issueIdentifier, CancellationToken ct = default)
    {
        var key = SanitizeIdentifier(issueIdentifier);
        var path = Path.GetFullPath(Path.Combine(_workspaceRoot, key));

        if (!Directory.Exists(path)) return;

        if (!string.IsNullOrWhiteSpace(_hooksConfig.BeforeRemove))
        {
            try
            {
                await RunHookAsync(_hooksConfig.BeforeRemove, path, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "before_remove hook failed for {Identifier}, proceeding with cleanup", issueIdentifier);
            }
        }

        try
        {
            Directory.Delete(path, true);
            logger.LogInformation("Cleaned workspace for {Identifier}", issueIdentifier);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete workspace directory for {Identifier}", issueIdentifier);
        }
    }

    /// <summary>
    /// Run before_run hook for the given workspace path.
    /// </summary>
    public async Task RunBeforeRunHookAsync(string workspacePath, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(_hooksConfig.BeforeRun))
            await RunHookAsync(_hooksConfig.BeforeRun, workspacePath, ct);
    }

    /// <summary>
    /// Run after_run hook for the given workspace path. Failures are logged and ignored.
    /// </summary>
    public async Task RunAfterRunHookAsync(string workspacePath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_hooksConfig.AfterRun)) return;

        try
        {
            await RunHookAsync(_hooksConfig.AfterRun, workspacePath, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "after_run hook failed");
        }
    }

    internal static string SanitizeIdentifier(string identifier)
    {
        return SafeChars.Replace(identifier, "_");
    }

    private void ValidatePathSafety(string resolvedPath)
    {
        var normalizedRoot = Path.GetFullPath(_workspaceRoot);
        if (!resolvedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Workspace path escape detected: {resolvedPath} is not under {normalizedRoot}");
    }

    private async Task RunHookAsync(string script, string workingDirectory, CancellationToken ct)
    {
        var isWindows = OperatingSystem.IsWindows();
        var psi = new ProcessStartInfo
        {
            FileName = isWindows ? "cmd" : "bash",
            Arguments = isWindows ? $"/c {script}" : $"-lc \"{script}\"",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start hook process");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_hooksConfig.TimeoutMs);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Hook timed out after {_hooksConfig.TimeoutMs}ms");
        }

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException(
                $"Hook exited with code {process.ExitCode}: {stderr[..Math.Min(stderr.Length, 500)]}");
        }
    }

    private static async Task CheckoutPullRequestBranchAsync(
        string workspacePath, string headBranch, GitHubTrackerTrackerConfig trackerConfig, CancellationToken ct)
    {
        var token = ResolveToken(trackerConfig.ApiKey);
        await RunGitAsync(workspacePath, ["fetch", "origin", headBranch], token, ct);
        await RunGitAsync(workspacePath, ["checkout", "-B", headBranch, $"origin/{headBranch}"], token, ct);
    }

    private static async Task CloneRepositoryAsync(string workspacePath, GitHubTrackerTrackerConfig trackerConfig, CancellationToken ct)
    {
        var repository = trackerConfig.Repository!;
        var token = ResolveToken(trackerConfig.ApiKey);

        // Build authenticated clone URL using the standard GitHub PAT format:
        // https://x-access-token:{token}@github.com/{owner}/{repo}
        // Using "x-access-token" as the username and the PAT as the password prevents
        // git from treating the token as a bare username and prompting for a password.
        string cloneUrl;
        if (!string.IsNullOrEmpty(token))
            cloneUrl = $"https://x-access-token:{Uri.EscapeDataString(token)}@github.com/{repository}";
        else
            cloneUrl = $"https://github.com/{repository}";

        // Use git init + fetch + reset instead of git clone so the sequence is safe
        // to run in a non-empty directory (e.g. one that already has a .craft subdir).
        // git reset --hard only touches files tracked by the commit; untracked files
        // such as .craft/ are left completely untouched.

        // Step 1: initialise (idempotent — safe if .git already exists)
        await RunGitAsync(workspacePath, ["init"], token, ct);

        // Step 2: configure remote — add if new, update URL if already present
        try
        {
            await RunGitAsync(workspacePath, ["remote", "add", "origin", cloneUrl], token, ct);
        }
        catch
        {
            await RunGitAsync(workspacePath, ["remote", "set-url", "origin", cloneUrl], token, ct);
        }

        // Step 3: fetch the tip of the default branch (shallow for speed)
        await RunGitAsync(workspacePath, ["fetch", "--depth=1", "origin"], token, ct);

        // Step 4: reset working tree to the fetched HEAD
        await RunGitAsync(workspacePath, ["reset", "--hard", "FETCH_HEAD"], token, ct);
    }

    private static async Task RunGitAsync(string workingDir, string[] args, string? token, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        // Prevent any interactive credential prompt (terminal or GUI).
        // GIT_TERMINAL_PROMPT disables stdin prompts; GCM_INTERACTIVE=never
        // prevents Git Credential Manager from opening its GUI dialog on Windows.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";
        psi.Environment["GCM_INTERACTIVE"] = "never";

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start git {args[0]}");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"git {args[0]} timed out after 5 minutes");
        }

        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync(CancellationToken.None);
            // Redact token from error message before surfacing
            var safeError = string.IsNullOrEmpty(token) ? stderr
                : stderr.Replace(token, "***", StringComparison.Ordinal);
            throw new InvalidOperationException(
                $"git {args[0]} exited with code {process.ExitCode}: {safeError[..Math.Min(safeError.Length, 500)]}");
        }
    }

    private static string? ResolveToken(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured)) return null;
        if (configured.StartsWith('$'))
        {
            var envName = configured[1..];
            return Environment.GetEnvironmentVariable(envName);
        }
        return configured;
    }

    private static string ResolveWorkspaceRoot(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return Path.Combine(Path.GetTempPath(), "github_tracker_workspaces");

        var resolved = configured;

        if (resolved.StartsWith('~'))
            resolved = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                resolved[1..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (resolved.Contains('$'))
        {
            resolved = Environment.ExpandEnvironmentVariables(
                resolved.Replace("$", "%").Replace("/", $"{Path.DirectorySeparatorChar}"));
        }

        return Path.GetFullPath(resolved);
    }

    [GeneratedRegex(@"[^A-Za-z0-9._\-]")]
    private static partial Regex GenerateSafeCharsRegex();
}
