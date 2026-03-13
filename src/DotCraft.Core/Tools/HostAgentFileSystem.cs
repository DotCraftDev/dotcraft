using DotCraft.Abstractions;

namespace DotCraft.Tools;

/// <summary>
/// Host-mode implementation of <see cref="IAgentFileSystem"/>.
/// Resolves paths directly on the local filesystem, relative to the workspace root.
/// </summary>
public sealed class HostAgentFileSystem(string workspacePath) : IAgentFileSystem
{
    public Task<HostFileHandle> ResolveHostFileAsync(string path)
    {
        var resolved = ResolvePath(path);
        return Task.FromResult(new HostFileHandle(resolved));
    }

    public async Task<string> ReadAsBase64Async(string path)
    {
        var resolved = ResolvePath(path);
        var bytes = await File.ReadAllBytesAsync(resolved);
        return Convert.ToBase64String(bytes);
    }

    private string ResolvePath(string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(workspacePath, path));
    }
}
