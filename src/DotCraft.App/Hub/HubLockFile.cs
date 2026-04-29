using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DotCraft.Hub;

/// <summary>
/// Holds the Hub single-instance lock and publishes discovery metadata.
/// </summary>
public sealed class HubLockFile : IDisposable
{
    private readonly string _lockFilePath;
    private readonly FileStream _stream;

    private HubLockFile(string lockFilePath, FileStream stream)
    {
        _lockFilePath = lockFilePath;
        _stream = stream;
    }

    /// <summary>
    /// Attempts to acquire the Hub lock file.
    /// </summary>
    public static bool TryAcquire(HubPaths paths, out HubLockFile? lockFile, out HubLockInfo? existingInfo)
    {
        Directory.CreateDirectory(paths.HubStatePath);
        existingInfo = TryRead(paths.LockFilePath);

        try
        {
            var stream = new FileStream(
                paths.LockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read);
            lockFile = new HubLockFile(paths.LockFilePath, stream);
            return true;
        }
        catch (IOException)
        {
            existingInfo ??= TryRead(paths.LockFilePath);
            lockFile = null;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            existingInfo ??= TryRead(paths.LockFilePath);
            lockFile = null;
            return false;
        }
    }

    /// <summary>
    /// Writes the current Hub discovery metadata to the lock file.
    /// </summary>
    public void Publish(HubLockInfo info)
    {
        var json = JsonSerializer.Serialize(info, HubJson.Options);
        var bytes = Encoding.UTF8.GetBytes(json + Environment.NewLine);
        _stream.SetLength(0);
        _stream.Position = 0;
        _stream.Write(bytes);
        _stream.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Attempts to delete the lock file after the lock stream is closed.
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

    /// <summary>
    /// Reads Hub discovery metadata from disk.
    /// </summary>
    public static HubLockInfo? TryRead(string lockFilePath)
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

            return JsonSerializer.Deserialize<HubLockInfo>(json, HubJson.Options);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Published Hub discovery metadata.
/// </summary>
public sealed record HubLockInfo(
    int Pid,
    string ApiBaseUrl,
    string Token,
    DateTimeOffset StartedAt,
    string Version)
{
    /// <summary>
    /// Returns whether the recorded process appears to still exist.
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
