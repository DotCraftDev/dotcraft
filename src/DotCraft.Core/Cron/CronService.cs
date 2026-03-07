using System.Text.Encodings.Web;
using System.Text.Json;

namespace DotCraft.Cron;

public sealed class CronService : IDisposable
{
    private readonly string _storePath;
    
    private readonly CronStore _store;
    
    private CancellationTokenSource? _cts;
    
    private readonly SemaphoreSlim _lock = new(1, 1);
    
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
        _ = RunTimerAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
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
        _store.Jobs.Add(job);
        SaveStore();
        return job;
    }

    public bool RemoveJob(string jobId)
    {
        var removed = _store.Jobs.RemoveAll(j => j.Id == jobId) > 0;
        if (removed) SaveStore();
        return removed;
    }

    public CronJob? EnableJob(string jobId, bool enabled)
    {
        var job = _store.Jobs.Find(j => j.Id == jobId);
        if (job == null) return null;
        job.Enabled = enabled;
        if (enabled) ComputeNextRun(job);
        SaveStore();
        return job;
    }

    public List<CronJob> ListJobs(bool includeDisabled = false)
    {
        return includeDisabled
            ? _store.Jobs.ToList()
            : _store.Jobs.Where(j => j.Enabled).ToList();
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
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var dueJobs = _store.Jobs
            .Where(j => j.Enabled && j.State.NextRunAtMs.HasValue && j.State.NextRunAtMs.Value <= now)
            .ToList();

        foreach (var job in dueJobs)
        {
            await _lock.WaitAsync();
            try
            {
                await ExecuteJobAsync(job);
            }
            finally
            {
                _lock.Release();
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

            if (job.DeleteAfterRun || job.Schedule.Kind == "at")
            {
                _store.Jobs.Remove(job);
            }
            else
            {
                ComputeNextRun(job);
            }
        }
        catch (Exception ex)
        {
            job.State.LastRunAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            job.State.LastStatus = "error";
            job.State.LastError = ex.Message;
            ComputeNextRun(job);
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
        var dir = Path.GetDirectoryName(_storePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(_storePath, JsonSerializer.Serialize(_store, JsonOptions));
    }

    public void Dispose()
    {
        Stop();
        _lock.Dispose();
    }
}
