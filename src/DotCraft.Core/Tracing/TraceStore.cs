using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;

namespace DotCraft.Tracing;

/// <param name="synchronousPersist">When true, writes traces on the caller thread (blocks). When false (default), uses fire-and-forget Task.Run.</param>
public sealed class TraceStore(
    string? storagePath = null,
    int maxEventsPerSession = 5000,
    bool synchronousPersist = false)
{
    private int _persistInFlight;

    private readonly ConcurrentDictionary<string, TraceSession> _sessions = new();

    private readonly Channel<TraceEvent> _sseChannel = Channel.CreateBounded<TraceEvent>(
        new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });

    private static readonly JsonSerializerOptions PersistJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void Record(TraceEvent evt)
    {
        var session = _sessions.GetOrAdd(evt.SessionKey, key => new TraceSession
        {
            SessionKey = key
        });

        session.LastActivityAt = evt.Timestamp;

        switch (evt.Type)
        {
            case TraceEventType.SessionMetadata:
                UpsertSessionMetadata(evt.SessionKey, evt.FinalSystemPrompt, evt.ToolNames, evt.Timestamp);
                break;
            case TraceEventType.Request:
                session.RequestCount++;
                break;
            case TraceEventType.Response:
                session.ResponseCount++;
                if (!string.IsNullOrEmpty(evt.FinishReason))
                    session.LastFinishReason = evt.FinishReason;
                break;
            case TraceEventType.ToolCallCompleted:
                session.ToolCallCount++;
                if (evt.DurationMs.HasValue)
                    session.AddToolDuration((long)Math.Round(evt.DurationMs.Value));
                break;
            case TraceEventType.TokenUsage:
                if (evt.InputTokens.HasValue)
                    session.AddInputTokens(evt.InputTokens.Value);
                if (evt.OutputTokens.HasValue)
                    session.AddOutputTokens(evt.OutputTokens.Value);
                break;
            case TraceEventType.Error:
                session.ErrorCount++;
                break;
            case TraceEventType.ContextCompaction:
                session.ContextCompactionCount++;
                break;
            case TraceEventType.Thinking:
                session.ThinkingCount++;
                break;
        }

        if (session.Events.Count < maxEventsPerSession)
            session.Events.Add(evt);

        _sseChannel.Writer.TryWrite(evt);

        if (storagePath != null)
            PersistEvent(evt);
    }

    /// <summary>
    /// Blocks until asynchronous persistence work scheduled by this instance has completed.
    /// No-op when <see cref="synchronousPersist"/> is true or there is no storage path.
    /// </summary>
    public void WaitForPendingPersistence()
    {
        if (storagePath == null || synchronousPersist)
            return;

        var spin = new SpinWait();
        while (Volatile.Read(ref _persistInFlight) != 0)
            spin.SpinOnce();
    }

    /// <summary>
    /// Replaces in-memory sessions with a full reload from <see cref="storagePath"/>.
    /// Used when the dashboard runs in a different process than trace producers (e.g. CLI + AppServer subprocess)
    /// so the UI reflects the shared on-disk trace files.
    /// </summary>
    public void RefreshFromDisk()
    {
        if (storagePath == null)
            return;

        WaitForPendingPersistence();

        lock (_diskMutationLock)
        {
            _sessions.Clear();
            LoadFromDisk();
        }
    }

    public void UpsertSessionMetadata(
        string sessionKey,
        string? finalSystemPrompt,
        IEnumerable<string>? toolNames,
        DateTimeOffset? capturedAt = null)
    {
        var session = _sessions.GetOrAdd(sessionKey, key => new TraceSession
        {
            SessionKey = key
        });

        if (!string.IsNullOrWhiteSpace(finalSystemPrompt) && string.IsNullOrWhiteSpace(session.FinalSystemPrompt))
            session.FinalSystemPrompt = finalSystemPrompt;

        session.SetToolNames(toolNames);

        var at = capturedAt ?? DateTimeOffset.UtcNow;
        if (!session.SessionMetadataCapturedAt.HasValue || at > session.SessionMetadataCapturedAt.Value)
            session.SessionMetadataCapturedAt = at;
    }

    public IReadOnlyList<TraceSession> GetSessions()
    {
        return _sessions.Values
            .OrderByDescending(s => s.LastActivityAt)
            .ToList();
    }

    public TraceSession? GetSession(string sessionKey)
    {
        return _sessions.GetValueOrDefault(sessionKey);
    }

    public IReadOnlyList<TraceEvent> GetEvents(string sessionKey)
    {
        if (!_sessions.TryGetValue(sessionKey, out var session))
            return [];
        return session.Events.OrderBy(e => e.Timestamp).ToList();
    }

    public bool ClearSession(string sessionKey)
    {
        lock (_diskMutationLock)
        {
            var removed = _sessions.TryRemove(sessionKey, out _);
            if (removed && storagePath != null)
                DeleteSessionFile(sessionKey);
            return removed;
        }
    }

    public void ClearAll()
    {
        lock (_diskMutationLock)
        {
            _sessions.Clear();
            if (storagePath != null)
                DeleteAllSessionFiles();
        }
    }

    public ChannelReader<TraceEvent> SseReader => _sseChannel.Reader;

    public TraceSummary GetSummary()
    {
        long totalInput = 0, totalOutput = 0;
        int totalRequests = 0, totalResponses = 0, totalToolCalls = 0, totalErrors = 0, totalContextCompactions = 0;
        long totalToolDuration = 0, maxToolDuration = 0;

        foreach (var session in _sessions.Values)
        {
            totalInput += session.TotalInputTokens;
            totalOutput += session.TotalOutputTokens;
            totalRequests += session.RequestCount;
            totalResponses += session.ResponseCount;
            totalToolCalls += session.ToolCallCount;
            totalErrors += session.ErrorCount;
            totalContextCompactions += session.ContextCompactionCount;
            totalToolDuration += session.TotalToolDurationMs;
            maxToolDuration = Math.Max(maxToolDuration, session.MaxToolDurationMs);
        }

        return new TraceSummary
        {
            SessionCount = _sessions.Count,
            TotalRequests = totalRequests,
            TotalResponses = totalResponses,
            TotalToolCalls = totalToolCalls,
            TotalErrors = totalErrors,
            TotalContextCompactions = totalContextCompactions,
            TotalToolDurationMs = totalToolDuration,
            AvgToolDurationMs = totalToolCalls > 0 ? totalToolDuration / (double)totalToolCalls : 0,
            MaxToolDurationMs = maxToolDuration,
            TotalInputTokens = totalInput,
            TotalOutputTokens = totalOutput,
            TotalTokens = totalInput + totalOutput
        };
    }

    public void LoadFromDisk()
    {
        if (storagePath == null || !Directory.Exists(storagePath))
            return;

        foreach (var file in Directory.GetFiles(storagePath, "*.jsonl"))
        {
            try
            {
                var lines = File.ReadAllLines(file);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var evt = JsonSerializer.Deserialize<TraceEvent>(line, PersistJsonOptions);
                    if (evt == null || string.IsNullOrEmpty(evt.SessionKey)) continue;

                    var session = _sessions.GetOrAdd(evt.SessionKey, key => new TraceSession
                    {
                        SessionKey = key,
                        StartedAt = evt.Timestamp
                    });

                    session.LastActivityAt = evt.Timestamp;

                    switch (evt.Type)
                    {
                        case TraceEventType.SessionMetadata:
                            UpsertSessionMetadata(evt.SessionKey, evt.FinalSystemPrompt, evt.ToolNames, evt.Timestamp);
                            break;
                        case TraceEventType.Request:
                            session.RequestCount++;
                            break;
                        case TraceEventType.Response:
                            session.ResponseCount++;
                            if (!string.IsNullOrEmpty(evt.FinishReason))
                                session.LastFinishReason = evt.FinishReason;
                            break;
                        case TraceEventType.ToolCallCompleted:
                            session.ToolCallCount++;
                            if (evt.DurationMs.HasValue)
                                session.AddToolDuration((long)Math.Round(evt.DurationMs.Value));
                            break;
                        case TraceEventType.TokenUsage:
                            if (evt.InputTokens.HasValue)
                                session.AddInputTokens(evt.InputTokens.Value);
                            if (evt.OutputTokens.HasValue)
                                session.AddOutputTokens(evt.OutputTokens.Value);
                            break;
                        case TraceEventType.Error:
                            session.ErrorCount++;
                            break;
                        case TraceEventType.ContextCompaction:
                            session.ContextCompactionCount++;
                            break;
                        case TraceEventType.Thinking:
                            session.ThinkingCount++;
                            break;
                    }

                    if (session.Events.Count < maxEventsPerSession)
                        session.Events.Add(evt);
                }
            }
            catch
            {
                // Skip corrupted files
            }
        }
    }

    private void PersistEvent(TraceEvent evt)
    {
        if (synchronousPersist)
        {
            PersistEventCore(evt);
            return;
        }

        Interlocked.Increment(ref _persistInFlight);
        _ = Task.Run(() =>
        {
            try
            {
                PersistEventCore(evt);
            }
            finally
            {
                Interlocked.Decrement(ref _persistInFlight);
            }
        });
    }

    private void PersistEventCore(TraceEvent evt)
    {
        try
        {
            Directory.CreateDirectory(storagePath!);
            var safeKey = SanitizeFileName(evt.SessionKey);
            var filePath = Path.Combine(storagePath!, $"{safeKey}.jsonl");
            var json = JsonSerializer.Serialize(evt, PersistJsonOptions);
            lock (GetFileLock(filePath))
            {
                File.AppendAllText(filePath, json + "\n");
            }
        }
        catch
        {
            // Silently ignore persistence errors
        }
    }

    private void DeleteSessionFile(string sessionKey)
    {
        try
        {
            var safeKey = SanitizeFileName(sessionKey);
            var filePath = Path.Combine(storagePath!, $"{safeKey}.jsonl");
            lock (GetFileLock(filePath))
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
        }
        catch
        {
            // Ignore deletion errors
        }
    }

    private void DeleteAllSessionFiles()
    {
        try
        {
            if (!Directory.Exists(storagePath!))
                return;

            foreach (var file in Directory.GetFiles(storagePath!, "*.jsonl"))
            {
                try
                {
                    lock (GetFileLock(file))
                    {
                        if (File.Exists(file))
                            File.Delete(file);
                    }
                }
                catch
                {
                    // ignore per file
                }
            }
        }
        catch
        {
            // Ignore deletion errors
        }
    }

    private static string SanitizeFileName(string name)
    {
        return string.Concat(name.Split(Path.GetInvalidFileNameChars()));
    }

    private static readonly ConcurrentDictionary<string, object> FileLocks = new();

    /// <summary>Serializes RefreshFromDisk, ClearSession, and ClearAll against each other.</summary>
    private readonly object _diskMutationLock = new();

    private static object GetFileLock(string filePath)
    {
        return FileLocks.GetOrAdd(filePath, _ => new object());
    }
}

public sealed class TraceSummary
{
    public int SessionCount { get; init; }
    
    public int TotalRequests { get; init; }

    public int TotalResponses { get; init; }
    
    public int TotalToolCalls { get; init; }

    public int TotalErrors { get; init; }

    public int TotalContextCompactions { get; init; }

    public long TotalToolDurationMs { get; init; }

    public double AvgToolDurationMs { get; init; }

    public long MaxToolDurationMs { get; init; }
    
    public long TotalInputTokens { get; init; }
    
    public long TotalOutputTokens { get; init; }
    
    public long TotalTokens { get; init; }
}
