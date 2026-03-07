namespace DotCraft.Security;

public enum ApprovalSource
{
    Console,
    QQ,
    WeCom,
    Api
}

public sealed class ApprovalContext
{
    public string UserId { get; init; } = "";

    public string UserRole { get; init; } = string.Empty;

    public long GroupId { get; init; }

    public ApprovalSource Source { get; init; }

    public bool IsGroupContext => GroupId > 0;
}

/// <summary>
/// Service for requesting user approval for sensitive operations.
/// </summary>
public interface IApprovalService
{
    /// <summary>
    /// Request user approval for a file operation outside workspace.
    /// </summary>
    /// <param name="operation">The operation name (read, write, edit, list)</param>
    /// <param name="path">The file path</param>
    /// <param name="context">Optional approval context with user info</param>
    /// <returns>True if approved, false if rejected</returns>
    Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null);

    /// <summary>
    /// Request user approval for a shell command that accesses paths outside workspace.
    /// </summary>
    /// <param name="command">The shell command</param>
    /// <param name="workingDir">The working directory</param>
    /// <param name="context">Optional approval context with user info</param>
    /// <returns>True if approved, false if rejected</returns>
    Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null);
}
