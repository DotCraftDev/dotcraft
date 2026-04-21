using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Channels;
using DotCraft.Protocol;
using DotCraft.State;

namespace DotCraft.Tracing;

/// <param name="synchronousPersist">When true, writes traces on the caller thread (blocks). When false (default), uses background persistence.</param>
public sealed class TraceStore
{
    private readonly string? _storagePath;
    private readonly int _maxEventsPerSession;
    private readonly bool _synchronousPersist;
    private readonly StateRuntime? _stateRuntime;
    private readonly TraceSessionBindingStore? _bindingStore;
    private readonly object _diskMutationLock = new();
    private readonly ConcurrentDictionary<string, TraceSession> _sessions = new();
    private readonly Channel<TraceEvent> _sseChannel = Channel.CreateBounded<TraceEvent>(
        new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });
    private int _persistInFlight;

    private static readonly JsonSerializerOptions PersistJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public TraceStore(
        string? storagePath = null,
        int maxEventsPerSession = 5000,
        bool synchronousPersist = false)
        : this(storagePath, maxEventsPerSession, synchronousPersist, null)
    {
    }

    internal TraceStore(
        string? storagePath,
        int maxEventsPerSession,
        bool synchronousPersist,
        StateRuntime? stateRuntime)
    {
        _storagePath = storagePath;
        _maxEventsPerSession = maxEventsPerSession;
        _synchronousPersist = synchronousPersist;
        _stateRuntime = stateRuntime;
        _bindingStore = stateRuntime != null ? new TraceSessionBindingStore(stateRuntime) : null;
    }

    public void Record(TraceEvent evt)
    {
        _bindingStore?.GetOrCreateBinding(evt.SessionKey, evt.Timestamp);
        ApplyEvent(evt, writeToSse: true);

        if (_stateRuntime != null || _storagePath != null)
            PersistEvent(evt);
    }

    /// <summary>
    /// Blocks until asynchronous persistence work scheduled by this instance has completed.
    /// </summary>
    public void WaitForPendingPersistence()
    {
        if ((_storagePath == null && _stateRuntime == null) || _synchronousPersist)
            return;

        var spin = new SpinWait();
        while (Volatile.Read(ref _persistInFlight) != 0)
            spin.SpinOnce();
    }

    /// <summary>
    /// Replaces in-memory sessions with a full reload from the persistent store.
    /// </summary>
    public void RefreshFromDisk()
    {
        if (_storagePath == null && _stateRuntime == null)
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
            var persistedRemoved = false;

            if (_stateRuntime != null)
            {
                using var connection = _stateRuntime.OpenConnection();
                using var deleteEvents = connection.CreateCommand();
                deleteEvents.CommandText = "DELETE FROM trace_events WHERE session_key = $session_key";
                deleteEvents.Parameters.AddWithValue("$session_key", sessionKey);
                persistedRemoved |= deleteEvents.ExecuteNonQuery() > 0;

                using var deleteSession = connection.CreateCommand();
                deleteSession.CommandText = "DELETE FROM trace_sessions WHERE session_key = $session_key";
                deleteSession.Parameters.AddWithValue("$session_key", sessionKey);
                persistedRemoved |= deleteSession.ExecuteNonQuery() > 0;
            }
            else if (_storagePath != null)
            {
                persistedRemoved = File.Exists(Path.Combine(_storagePath, $"{SanitizeFileName(sessionKey)}.jsonl"));
                DeleteSessionFile(sessionKey);
            }

            if (!removed && !persistedRemoved)
                return false;

            _bindingStore?.DeleteBinding(sessionKey);
            return true;
        }
    }

    public void ClearAll()
    {
        lock (_diskMutationLock)
        {
            _sessions.Clear();

            if (_stateRuntime != null)
            {
                using var connection = _stateRuntime.OpenConnection();
                using var deleteEvents = connection.CreateCommand();
                deleteEvents.CommandText = "DELETE FROM trace_events";
                deleteEvents.ExecuteNonQuery();

                using var deleteSessions = connection.CreateCommand();
                deleteSessions.CommandText = "DELETE FROM trace_sessions";
                deleteSessions.ExecuteNonQuery();
            }
            else if (_storagePath != null)
            {
                DeleteAllSessionFiles();
            }

            _bindingStore?.DeleteAllBindings();
        }
    }

    public ChannelReader<TraceEvent> SseReader => _sseChannel.Reader;

    public void BindThreadMainSession(string threadId, DateTimeOffset? createdAt = null)
        => _bindingStore?.BindThreadMain(threadId, createdAt);

    public void BindChildSession(
        string sessionKey,
        string rootThreadId,
        string parentSessionKey,
        DateTimeOffset? createdAt = null)
        => _bindingStore?.BindThreadChild(sessionKey, rootThreadId, parentSessionKey, createdAt);

    public TraceSessionDeletionDescriptor DescribeSessionDeletion(string sessionKey)
    {
        var binding = _bindingStore?.GetOrCreateBinding(sessionKey);
        var scope = binding == null
            || binding.BindingKind == TraceSessionBindingKind.Unbound
            || string.IsNullOrWhiteSpace(binding.RootThreadId)
            ? SessionPersistenceDeletionScopes.TraceOnly
            : SessionPersistenceDeletionScopes.ThreadCascade;

        return new TraceSessionDeletionDescriptor(
            sessionKey,
            binding?.RootThreadId,
            (binding?.BindingKind ?? TraceSessionBindingKind.Unbound).ToStorageValue(),
            scope);
    }

    public Dictionary<string, TraceSessionDeletionDescriptor> DescribeSessionDeletions(IEnumerable<string> sessionKeys)
    {
        var keys = sessionKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var bindings = _bindingStore?.GetBindings(keys)
            ?? new Dictionary<string, TraceSessionBinding>(StringComparer.Ordinal);
        var result = new Dictionary<string, TraceSessionDeletionDescriptor>(StringComparer.Ordinal);

        foreach (var sessionKey in keys)
        {
            if (!bindings.TryGetValue(sessionKey, out var binding))
                binding = _bindingStore?.GetOrCreateBinding(sessionKey)
                    ?? new TraceSessionBinding(
                        sessionKey,
                        null,
                        null,
                        TraceSessionBindingKind.Unbound,
                        DateTimeOffset.UtcNow);

            var scope = binding.BindingKind == TraceSessionBindingKind.Unbound || string.IsNullOrWhiteSpace(binding.RootThreadId)
                ? SessionPersistenceDeletionScopes.TraceOnly
                : SessionPersistenceDeletionScopes.ThreadCascade;
            result[sessionKey] = new TraceSessionDeletionDescriptor(
                sessionKey,
                binding.RootThreadId,
                binding.BindingKind.ToStorageValue(),
                scope);
        }

        return result;
    }

    public IReadOnlyList<string> GetBoundSessionKeys(string rootThreadId)
    {
        if (_bindingStore == null)
            return [];

        return _bindingStore.GetBindingsForRootThread(rootThreadId)
            .Select(b => b.SessionKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public bool DeleteStandaloneSession(string sessionKey)
    {
        var hadBinding = _bindingStore?.GetBinding(sessionKey) != null;
        var deleted = ClearSession(sessionKey);
        _bindingStore?.DeleteBinding(sessionKey);
        return deleted || hadBinding;
    }

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
        if (_stateRuntime != null)
        {
            LoadFromDb();
            return;
        }

        if (_storagePath == null || !Directory.Exists(_storagePath))
            return;

        foreach (var file in Directory.GetFiles(_storagePath, "*.jsonl"))
        {
            if (string.Equals(Path.GetFileName(file), "token_usage.jsonl", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var lines = File.ReadAllLines(file);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var evt = JsonSerializer.Deserialize<TraceEvent>(line, PersistJsonOptions);
                    if (evt != null && !string.IsNullOrEmpty(evt.SessionKey))
                        ApplyEvent(evt, writeToSse: false);
                }
            }
            catch
            {
                // Skip corrupted files
            }
        }
    }

    private void ApplyEvent(TraceEvent evt, bool writeToSse)
    {
        var session = _sessions.GetOrAdd(evt.SessionKey, key => new TraceSession
        {
            SessionKey = key,
            StartedAt = evt.Timestamp
        });

        if (session.StartedAt > evt.Timestamp)
        {
            // TraceSession.StartedAt is init-only; keep earliest in a new replacement session.
            _sessions[evt.SessionKey] = session = CloneSessionWithStartedAt(session, evt.Timestamp);
        }

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

        if (session.Events.Count < _maxEventsPerSession)
            session.Events.Add(evt);

        if (writeToSse)
            _sseChannel.Writer.TryWrite(evt);
    }

    private void PersistEvent(TraceEvent evt)
    {
        if (_synchronousPersist)
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
        if (_stateRuntime != null)
        {
            PersistEventToDb(evt);
            return;
        }

        try
        {
            Directory.CreateDirectory(_storagePath!);
            var safeKey = SanitizeFileName(evt.SessionKey);
            var filePath = Path.Combine(_storagePath!, $"{safeKey}.jsonl");
            var json = JsonSerializer.Serialize(evt, PersistJsonOptions);
            lock (_diskMutationLock)
            {
                File.AppendAllText(filePath, json + "\n");
            }
        }
        catch
        {
            // Best-effort persistence.
        }
    }

    private void PersistEventToDb(TraceEvent evt)
    {
        try
        {
            using var connection = _stateRuntime!.OpenConnection();
            using var insert = connection.CreateCommand();
            insert.CommandText = """
                INSERT INTO trace_events (
                    event_id,
                    session_key,
                    timestamp,
                    type,
                    tool_name,
                    call_id,
                    response_id,
                    message_id,
                    model_id,
                    finish_reason,
                    duration_ms,
                    event_json
                ) VALUES (
                    $event_id,
                    $session_key,
                    $timestamp,
                    $type,
                    $tool_name,
                    $call_id,
                    $response_id,
                    $message_id,
                    $model_id,
                    $finish_reason,
                    $duration_ms,
                    $event_json
                )
                """;
            insert.Parameters.AddWithValue("$event_id", evt.Id);
            insert.Parameters.AddWithValue("$session_key", evt.SessionKey);
            insert.Parameters.AddWithValue("$timestamp", evt.Timestamp.UtcDateTime.ToString("O"));
            insert.Parameters.AddWithValue("$type", evt.Type.ToString());
            insert.Parameters.AddWithValue("$tool_name", (object?)evt.ToolName ?? DBNull.Value);
            insert.Parameters.AddWithValue("$call_id", (object?)evt.CallId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$response_id", (object?)evt.ResponseId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$message_id", (object?)evt.MessageId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$model_id", (object?)evt.ModelId ?? DBNull.Value);
            insert.Parameters.AddWithValue("$finish_reason", (object?)evt.FinishReason ?? DBNull.Value);
            insert.Parameters.AddWithValue("$duration_ms", evt.DurationMs ?? (object)DBNull.Value);
            insert.Parameters.AddWithValue("$event_json", JsonSerializer.Serialize(evt, PersistJsonOptions));
            insert.ExecuteNonQuery();

            if (_sessions.TryGetValue(evt.SessionKey, out var session))
                PersistSessionSummary(connection, session);
        }
        catch
        {
            // Best-effort persistence.
        }
    }

    private static void PersistSessionSummary(Microsoft.Data.Sqlite.SqliteConnection connection, TraceSession session)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trace_sessions (
                session_key,
                started_at,
                last_activity_at,
                request_count,
                response_count,
                tool_call_count,
                error_count,
                context_compaction_count,
                thinking_count,
                total_input_tokens,
                total_output_tokens,
                total_tool_duration_ms,
                max_tool_duration_ms,
                last_finish_reason,
                final_system_prompt,
                tool_names_json
            ) VALUES (
                $session_key,
                $started_at,
                $last_activity_at,
                $request_count,
                $response_count,
                $tool_call_count,
                $error_count,
                $context_compaction_count,
                $thinking_count,
                $total_input_tokens,
                $total_output_tokens,
                $total_tool_duration_ms,
                $max_tool_duration_ms,
                $last_finish_reason,
                $final_system_prompt,
                $tool_names_json
            )
            ON CONFLICT(session_key) DO UPDATE SET
                started_at = excluded.started_at,
                last_activity_at = excluded.last_activity_at,
                request_count = excluded.request_count,
                response_count = excluded.response_count,
                tool_call_count = excluded.tool_call_count,
                error_count = excluded.error_count,
                context_compaction_count = excluded.context_compaction_count,
                thinking_count = excluded.thinking_count,
                total_input_tokens = excluded.total_input_tokens,
                total_output_tokens = excluded.total_output_tokens,
                total_tool_duration_ms = excluded.total_tool_duration_ms,
                max_tool_duration_ms = excluded.max_tool_duration_ms,
                last_finish_reason = excluded.last_finish_reason,
                final_system_prompt = excluded.final_system_prompt,
                tool_names_json = excluded.tool_names_json
            """;
        command.Parameters.AddWithValue("$session_key", session.SessionKey);
        command.Parameters.AddWithValue("$started_at", session.StartedAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$last_activity_at", session.LastActivityAt.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$request_count", session.RequestCount);
        command.Parameters.AddWithValue("$response_count", session.ResponseCount);
        command.Parameters.AddWithValue("$tool_call_count", session.ToolCallCount);
        command.Parameters.AddWithValue("$error_count", session.ErrorCount);
        command.Parameters.AddWithValue("$context_compaction_count", session.ContextCompactionCount);
        command.Parameters.AddWithValue("$thinking_count", session.ThinkingCount);
        command.Parameters.AddWithValue("$total_input_tokens", session.TotalInputTokens);
        command.Parameters.AddWithValue("$total_output_tokens", session.TotalOutputTokens);
        command.Parameters.AddWithValue("$total_tool_duration_ms", session.TotalToolDurationMs);
        command.Parameters.AddWithValue("$max_tool_duration_ms", session.MaxToolDurationMs);
        command.Parameters.AddWithValue("$last_finish_reason", (object?)session.LastFinishReason ?? DBNull.Value);
        command.Parameters.AddWithValue("$final_system_prompt", (object?)session.FinalSystemPrompt ?? DBNull.Value);
        command.Parameters.AddWithValue("$tool_names_json", JsonSerializer.Serialize(session.ToolNames, PersistJsonOptions));
        command.ExecuteNonQuery();
    }

    private void LoadFromDb()
    {
        using var connection = _stateRuntime!.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT event_json
            FROM trace_events
            ORDER BY timestamp, id
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            try
            {
                var evt = JsonSerializer.Deserialize<TraceEvent>(reader.GetString(0), PersistJsonOptions);
                if (evt != null)
                    ApplyEvent(evt, writeToSse: false);
            }
            catch
            {
                // Skip corrupted rows.
            }
        }
    }

    private void DeleteSessionFile(string sessionKey)
    {
        var filePath = Path.Combine(_storagePath!, $"{SanitizeFileName(sessionKey)}.jsonl");
        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    private void DeleteAllSessionFiles()
    {
        if (_storagePath == null || !Directory.Exists(_storagePath))
            return;

        foreach (var file in Directory.GetFiles(_storagePath, "*.jsonl"))
        {
            if (string.Equals(Path.GetFileName(file), "token_usage.jsonl", StringComparison.OrdinalIgnoreCase))
                continue;
            File.Delete(file);
        }
    }

    private static TraceSession CloneSessionWithStartedAt(TraceSession session, DateTimeOffset startedAt)
    {
        var clone = new TraceSession
        {
            SessionKey = session.SessionKey,
            StartedAt = startedAt,
            LastActivityAt = session.LastActivityAt,
            RequestCount = session.RequestCount,
            ResponseCount = session.ResponseCount,
            ToolCallCount = session.ToolCallCount,
            ErrorCount = session.ErrorCount,
            ContextCompactionCount = session.ContextCompactionCount,
            ThinkingCount = session.ThinkingCount,
            FinalSystemPrompt = session.FinalSystemPrompt,
            LastFinishReason = session.LastFinishReason,
            SessionMetadataCapturedAt = session.SessionMetadataCapturedAt
        };
        clone.SetToolNames(session.ToolNames);
        clone.LoadAggregateSnapshot(
            session.TotalInputTokens,
            session.TotalOutputTokens,
            session.TotalToolDurationMs,
            session.MaxToolDurationMs);
        foreach (var evt in session.Events)
            clone.Events.Add(evt);
        return clone;
    }

    private static string SanitizeFileName(string value)
        => string.Concat(value.Split(Path.GetInvalidFileNameChars()));
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
