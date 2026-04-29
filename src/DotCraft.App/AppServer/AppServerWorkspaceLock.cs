using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DotCraft.Hosting;
using DotCraft.Hub;

namespace DotCraft.AppServer;

/// <summary>
/// Process-level lock that prevents multiple AppServer instances from owning one workspace.
/// </summary>
public sealed class AppServerWorkspaceLock : IDisposable
{
    private readonly string _lockFilePath;
    private readonly FileStream _stream;

    private AppServerWorkspaceLock(string lockFilePath, FileStream stream)
    {
        _lockFilePath = lockFilePath;
        _stream = stream;
    }

    /// <summary>
    /// Attempts to acquire the workspace AppServer lock.
    /// </summary>
    public static bool TryAcquire(DotCraftPaths paths, out AppServerWorkspaceLock? lockFile, out AppServerLockInfo? existingInfo)
    {
        Directory.CreateDirectory(paths.CraftPath);
        var lockPath = GetLockFilePath(paths.CraftPath);
        existingInfo = TryRead(lockPath);

        try
        {
            var stream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read);
            lockFile = new AppServerWorkspaceLock(lockPath, stream);
            return true;
        }
        catch (IOException)
        {
            existingInfo ??= TryRead(lockPath);
            lockFile = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            existingInfo ??= TryRead(lockPath);
            lockFile = null;
            return false;
        }
    }

    /// <summary>
    /// Writes current AppServer owner metadata to the lock file.
    /// </summary>
    public void Publish(AppServerLockInfo info)
    {
        var json = JsonSerializer.Serialize(info, HubJson.Options);
        var bytes = Encoding.UTF8.GetBytes(json + Environment.NewLine);
        _stream.SetLength(0);
        _stream.Position = 0;
        _stream.Write(bytes);
        _stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Attempts to read AppServer owner metadata.
    /// </summary>
    public static AppServerLockInfo? TryRead(string lockFilePath)
    {
        try
        {
            if (!File.Exists(lockFilePath))
                return null;

            using var stream = new FileStream(
                lockFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var json = reader.ReadToEnd();
            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize<AppServerLockInfo>(json, HubJson.Options);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the AppServer lock path for a workspace `.craft` directory.
    /// </summary>
    public static string GetLockFilePath(string craftPath) => Path.Combine(craftPath, "appserver.lock");

    /// <summary>
    /// Attempts to delete the lock file after releasing the stream.
    /// </summary>
    public void DeleteAfterDispose()
    {
        Dispose();
        try
        {
            File.Delete(_lockFilePath);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stream.Dispose();
    }
}

/// <summary>
/// Published workspace AppServer owner metadata.
/// </summary>
public sealed record AppServerLockInfo(
    int Pid,
    string WorkspacePath,
    bool ManagedByHub,
    string? HubApiBaseUrl,
    DateTimeOffset StartedAt,
    string Version,
    IReadOnlyDictionary<string, string> Endpoints)
{
    /// <summary>
    /// Returns whether the recorded owner process appears alive.
    /// </summary>
    public bool IsProcessAlive()
    {
        try
        {
            return !Process.GetProcessById(Pid).HasExited;
        }
        catch
        {
            return false;
        }
    }
}
