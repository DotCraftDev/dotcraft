using System.Text.Encodings.Web;
using System.Text.Json;

namespace DotCraft.Cron;

public sealed class CronService : IDisposable
{
    private readonly string _storePath;

    private readonly CronStore _store;

    private CancellationTokenSource? _cts;

    // Guards _store.Jobs list mutations and reads (fast, sync-only).
    private readonly Lock _storeLock = new();

    // Prevents concurrent job execution — at most one job runs at a time.
    private readonly SemaphoreSlim _execLock = new(1, 1);

    // FileSystemWatcher for detecting external store mutations (e.g. manual edits).
    private FileSystemWatcher? _watcher;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public Func<CronJob, Task>? OnJob { get; set; }

    public CronService(string storePath)
    {
        _storePath = storePath;
        _store = LoadStore();
    }

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        StartWatching();
        _ = RunTimerAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>
    /// Reloads the cron store from disk, replacing the current in-memory job list.
    /// Safe to call from any thread. In AppServer mode this is triggered automatically
    /// by the FileSystemWatcher when an external process writes to the store file.
    /// In CLI mode call this before reads to see server-side changes.
    /// </summary>
    public void ReloadStore()
    {
        var fresh = LoadStore();
        lock (_storeLock)
        {
            _store.Jobs.Clear();
            _store.Jobs.AddRange(fresh.Jobs);
        }
    }

    public CronJob AddJob(string name, CronSchedule schedule, CronPayload payload, bool deleteAfterRun = false)
    {
        var job = new CronJob
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = name,
            Schedule = schedule,
            Payload = payload,
            CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            DeleteAfterRun = deleteAfterRun,
            State = new CronJobState()
        };
        ComputeNextRun(job);
        lock (_storeLock)
        {
            _store.Jobs.Add(job);
        }
        SaveStore();
        return job;
    }

    public bool RemoveJob(string jobId)
    {
        bool removed;
        lock (_storeLock)
        {
            removed = _store.Jobs.RemoveAll(j => j.Id == jobId) > 0;
        }
        if (removed) SaveStore();
        return removed;
    }

    public CronJob? EnableJob(string jobId, bool enabled)
    {
        CronJob? job;
        lock (_storeLock)
        {
            job = _store.Jobs.Find(j => j.Id == jobId);
            if (job == null) return null;
            job.Enabled = enabled;
            if (enabled) ComputeNextRun(job);
        }
        SaveStore();
        return job;
    }

    public List<CronJob> ListJobs(bool includeDisabled = false)
    {
        lock (_storeLock)
        {
            return includeDisabled
                ? _store.Jobs.ToList()
                : _store.Jobs.Where(j => j.Enabled).ToList();
        }
    }

    private async Task RunTimerAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                await CheckAndRunDueJobsAsync();
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Cron] Timer error: {ex.Message}");
            }
        }
    }

    private async Task CheckAndRunDueJobsAsync()
    {
        // Snapshot due jobs under _storeLock so a concurrent ReloadStore() or
        // wire-based mutation doesn't corrupt the iteration.
        List<CronJob> dueJobs;
        lock (_storeLock)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            dueJobs = _store.Jobs
                .Where(j => j.Enabled && j.State.NextRunAtMs.HasValue && j.State.NextRunAtMs.Value <= now)
                .ToList();
        }

        foreach (var job in dueJobs)
        {
            // _execLock ensures jobs run sequentially, not concurrently.
            await _execLock.WaitAsync();
            try
            {
                // Re-check that the job still exists and is enabled after acquiring the lock,
                // since a concurrent RemoveJob or ReloadStore may have removed it.
                bool stillValid;
                lock (_storeLock)
                {
                    stillValid = _store.Jobs.Any(j => j.Id == job.Id && j.Enabled);
                }
                if (stillValid)
                    await ExecuteJobAsync(job);
            }
            finally
            {
                _execLock.Release();
            }
        }
    }

    private async Task ExecuteJobAsync(CronJob job)
    {
        try
        {
            if (OnJob != null)
                await OnJob(job);

            job.State.LastRunAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            job.State.LastStatus = "ok";
            job.State.LastError = null;

            lock (_storeLock)
            {
                if (job.DeleteAfterRun || job.Schedule.Kind == "at")
                    _store.Jobs.Remove(job);
                else
                    ComputeNextRun(job);
            }
        }
        catch (Exception ex)
        {
            job.State.LastRunAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            job.State.LastStatus = "error";
            job.State.LastError = ex.Message;
            lock (_storeLock)
            {
                ComputeNextRun(job);
            }
        }
        SaveStore();
    }

    private static void ComputeNextRun(CronJob job)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        switch (job.Schedule.Kind)
        {
            case "at":
                job.State.NextRunAtMs = job.Schedule.AtMs;
                break;
            case "every" when job.Schedule.EveryMs.HasValue:
                job.State.NextRunAtMs = now + job.Schedule.EveryMs.Value;
                break;
            default:
                job.State.NextRunAtMs = null;
                break;
        }
    }

    /// <summary>
    /// Starts watching the store file for external changes (e.g. CLI writing via its own
    /// CronService instance). When a change is detected, ReloadStore() is called after a
    /// short debounce to let the write complete.
    /// </summary>
    private void StartWatching()
    {
        var dir = Path.GetDirectoryName(_storePath);
        var file = Path.GetFileName(_storePath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file)) return;

        // The directory may not exist yet if no jobs have been created.
        if (!Directory.Exists(dir)) return;

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };

        // Debounce: wait 200ms after the last write event before reloading, to avoid
        // reading a partially-written file.
        _watcher.Changed += (_, _) =>
        {
            Task.Delay(200).ContinueWith(_ => ReloadStore());
        };
    }

    private CronStore LoadStore()
    {
        if (!File.Exists(_storePath))
            return new CronStore();
        try
        {
            var json = File.ReadAllText(_storePath);
            return JsonSerializer.Deserialize<CronStore>(json, JsonOptions) ?? new CronStore();
        }
        catch
        {
            return new CronStore();
        }
    }

    private void SaveStore()
    {
        // Snapshot under _storeLock so the serialized JSON is consistent with in-memory state.
        // File I/O is done outside the lock to avoid blocking other operations during a slow write.
        CronStore snapshot;
        lock (_storeLock)
        {
            snapshot = new CronStore
            {
                Version = _store.Version,
                Jobs = _store.Jobs.ToList()
            };
        }

        var dir = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_storePath, JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    public void Dispose()
    {
        Stop();
        _execLock.Dispose();
    }
}
