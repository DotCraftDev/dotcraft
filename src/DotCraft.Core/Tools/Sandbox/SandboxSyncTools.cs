using System.ComponentModel;
using System.Text;
using DotCraft.Security;

namespace DotCraft.Tools.Sandbox;

/// <summary>
/// Tools for synchronizing files between the sandbox and the host workspace.
/// Pulling files from the sandbox to the host requires approval via <see cref="IApprovalService"/>.
/// </summary>
public sealed class SandboxSyncTools
{
    private readonly SandboxSessionManager _sandboxManager;
    private readonly string _workspacePath;
    private readonly IApprovalService _approvalService;

    public SandboxSyncTools(
        SandboxSessionManager sandboxManager,
        string workspacePath,
        IApprovalService approvalService)
    {
        _sandboxManager = sandboxManager;
        _workspacePath = Path.GetFullPath(workspacePath);
        _approvalService = approvalService;
    }

    [Description("List files modified inside the sandbox since it was created. Use this to see what changes were made before syncing back to the host.")]
    [Tool(Icon = "🔄", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.ListModifiedFiles))]
    public async Task<string> ListModifiedFiles()
    {
        try
        {
            var files = await _sandboxManager.GetModifiedFilesAsync();
            if (files.Count == 0)
                return "No modified files found in the sandbox.";

            var sb = new StringBuilder();
            sb.AppendLine($"Found {files.Count} modified file(s) in sandbox:");
            foreach (var file in files)
            {
                sb.AppendLine($"  {file}");
            }
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return $"Error listing modified files: {ex.Message}";
        }
    }

    [Description("Sync a specific file from the sandbox back to the host workspace. Requires user approval for security.")]
    [Tool(Icon = "🔄", DisplayType = typeof(CoreToolDisplays), DisplayMethod = nameof(CoreToolDisplays.SyncToHost))]
    public async Task<string> SyncToHost(
        [Description("Path of the file inside the sandbox to sync back.")] string sandboxPath,
        [Description("Target path in the host workspace (relative to workspace root).")] string hostPath)
    {
        try
        {
            // Resolve host path and validate it's within workspace
            var fullHostPath = Path.IsPathRooted(hostPath)
                ? Path.GetFullPath(hostPath)
                : Path.GetFullPath(Path.Combine(_workspacePath, hostPath));

            if (!fullHostPath.StartsWith(_workspacePath, StringComparison.OrdinalIgnoreCase))
                return "Error: Target path must be within the workspace boundary.";

            // Request approval for writing to host
            var context = ApprovalContextScope.Current;
            var approved = await _approvalService.RequestFileApprovalAsync("sandbox_sync", fullHostPath, context);
            if (!approved)
                return "Error: File sync was rejected by user.";

            // Read from sandbox
            var content = await _sandboxManager.ReadSandboxFileAsync(sandboxPath);
            if (content == null)
                return $"Error: Could not read {sandboxPath} from sandbox.";

            // Write to host
            var directory = Path.GetDirectoryName(fullHostPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(fullHostPath, content);
            return $"Successfully synced {sandboxPath} -> {hostPath} ({content.Length} bytes)";
        }
        catch (Exception ex)
        {
            return $"Error syncing file to host: {ex.Message}";
        }
    }
}
