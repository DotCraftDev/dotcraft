using DotCraft.Automations.Abstractions;
using Microsoft.Extensions.Logging;

namespace DotCraft.Automations.Workspace;

/// <summary>
/// Provisions and removes isolated workspace directories for automation tasks.
/// </summary>
public sealed class AutomationWorkspaceManager(
    AutomationsConfig config,
    ILogger<AutomationWorkspaceManager> logger)
{
    /// <summary>
    /// Creates a new workspace directory for the task and returns its path.
    /// Path: {WorkspaceRoot}/{sourceName}/{taskId}/
    /// </summary>
    public Task<string> ProvisionAsync(AutomationTask task, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var safeSource = SanitizePathSegment(task.SourceName);
        var safeTask = SanitizePathSegment(task.Id);
        var path = Path.Combine(config.WorkspaceRoot, safeSource, safeTask);
        Directory.CreateDirectory(path);
        logger.LogDebug("Provisioned automation workspace at {Path}", path);
        return Task.FromResult(path);
    }

    /// <summary>
    /// Returns the workspace path for a task if it exists, or null.
    /// </summary>
    public string? GetExisting(AutomationTask task)
    {
        var safeSource = SanitizePathSegment(task.SourceName);
        var safeTask = SanitizePathSegment(task.Id);
        var path = Path.Combine(config.WorkspaceRoot, safeSource, safeTask);
        return Directory.Exists(path) ? path : null;
    }

    /// <summary>
    /// Deletes the workspace directory for the task.
    /// </summary>
    public Task CleanupAsync(AutomationTask task, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = GetExisting(task);
        if (path == null)
            return Task.CompletedTask;

        try
        {
            Directory.Delete(path, recursive: true);
            logger.LogDebug("Removed automation workspace at {Path}", path);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete automation workspace at {Path}", path);
        }

        return Task.CompletedTask;
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "_";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (invalid.Contains(chars[i]) || chars[i] == Path.DirectorySeparatorChar || chars[i] == Path.AltDirectorySeparatorChar)
                chars[i] = '_';
        }

        return new string(chars);
    }
}
