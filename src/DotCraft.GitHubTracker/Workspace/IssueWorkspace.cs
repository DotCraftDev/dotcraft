namespace DotCraft.GitHubTracker.Workspace;

/// <summary>
/// Represents a per-issue workspace directory.
/// </summary>
public sealed class IssueWorkspace(string path)
{
    /// <summary>
    /// Absolute path to the workspace directory.
    /// </summary>
    public string Path { get; } = path;

    /// <summary>
    /// Path to the .craft directory inside this workspace.
    /// </summary>
    public string CraftPath => System.IO.Path.Combine(Path, ".craft");
}
