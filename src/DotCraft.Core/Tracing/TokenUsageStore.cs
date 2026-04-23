using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotCraft.State;

namespace DotCraft.Tracing;

public static class TokenUsageSourceModes
{
    public const string ServerManaged = "server-managed";
    public const string ClientManaged = "client-managed";
    public const string Mixed = "mixed";
}

public static class TokenUsageSubjectKinds
{
    public const string User = "user";
    public const string Thread = "thread";
    public const string Session = "session";
    public const string Mixed = "mixed";
}

public static class TokenUsageContextKinds
{
    public const string Group = "group";
    public const string Mixed = "mixed";
}

public sealed class TokenUsageRecord
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string SourceId { get; init; } = string.Empty;

    public string SourceMode { get; init; } = string.Empty;

    public string SubjectKind { get; init; } = string.Empty;

    public string SubjectId { get; init; } = string.Empty;

    public string SubjectLabel { get; init; } = string.Empty;

    public string? ContextKind { get; init; }

    public string? ContextId { get; init; }

    public string? ContextLabel { get; init; }

    public string? ThreadId { get; init; }

    public string? SessionKey { get; init; }

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long TotalTokens => InputTokens + OutputTokens;
}

public sealed class UsageSourceSummary
{
    public string SourceId { get; init; } = string.Empty;

    public string SourceMode { get; init; } = string.Empty;

    public string SubjectKind { get; init; } = string.Empty;

    public string? ContextKind { get; init; }

    public int SubjectCount { get; init; }

    public int ContextCount { get; init; }

    public int RequestCount { get; init; }

    public long TotalInputTokens { get; init; }

    public long TotalOutputTokens { get; init; }

    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    public DateTimeOffset LastActiveAt { get; init; }
}

public sealed class UsageBreakdownEntry
{
    public string Kind { get; init; } = string.Empty;

    public string Id { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public int RequestCount { get; init; }

    public int? RelatedSubjectCount { get; init; }

    public long TotalInputTokens { get; init; }

    public long TotalOutputTokens { get; init; }

    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    public DateTimeOffset LastActiveAt { get; init; }
}

public sealed class TokenUsageStore
{
    private readonly string? _storagePath;
    private readonly StateRuntime? _stateRuntime;
    private readonly ConcurrentQueue<TokenUsageRecord> _records = new();
    private readonly object _fileLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public TokenUsageStore(string? storagePath = null)
        : this(storagePath, null)
    {
    }

    internal TokenUsageStore(string? storagePath, StateRuntime? stateRuntime)
    {
        _storagePath = storagePath;
        _stateRuntime = stateRuntime;
    }

    public void Record(TokenUsageRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.SourceId)
            || string.IsNullOrWhiteSpace(record.SourceMode)
            || string.IsNullOrWhiteSpace(record.SubjectKind)
            || string.IsNullOrWhiteSpace(record.SubjectId))
        {
            return;
        }

        ApplyRecord(record);

        if (_stateRuntime != null)
            PersistRecordToDb(record);
        else if (_storagePath != null)
            PersistRecordToFile(record);
    }

    public IReadOnlyList<UsageSourceSummary> GetSourceSummaries()
    {
        return SnapshotRecords()
            .GroupBy(r => r.SourceId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new UsageSourceSummary
            {
                SourceId = group.Key,
                SourceMode = CollapseKinds(group.Select(r => r.SourceMode), TokenUsageSourceModes.Mixed),
                SubjectKind = CollapseKinds(group.Select(r => r.SubjectKind), TokenUsageSubjectKinds.Mixed),
                ContextKind = CollapseOptionalKinds(group.Select(r => r.ContextKind), TokenUsageContextKinds.Mixed),
                SubjectCount = group
                    .Select(r => r.SubjectId)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                ContextCount = group
                    .Where(r => !string.IsNullOrWhiteSpace(r.ContextId))
                    .Select(r => r.ContextId!)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                RequestCount = group.Count(),
                TotalInputTokens = group.Sum(r => r.InputTokens),
                TotalOutputTokens = group.Sum(r => r.OutputTokens),
                LastActiveAt = group.Max(r => r.Timestamp)
            })
            .OrderByDescending(s => s.TotalTokens)
            .ThenBy(s => s.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<UsageBreakdownEntry> GetSubjectBreakdown(string sourceId)
    {
        return SnapshotRecords()
            .Where(r => string.Equals(r.SourceId, sourceId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => new { r.SubjectKind, r.SubjectId })
            .Select(group => new UsageBreakdownEntry
            {
                Kind = CollapseKinds(group.Select(r => r.SubjectKind), TokenUsageSubjectKinds.Mixed),
                Id = group.Key.SubjectId,
                Label = group
                    .Select(r => string.IsNullOrWhiteSpace(r.SubjectLabel) ? r.SubjectId : r.SubjectLabel)
                    .FirstOrDefault(label => !string.IsNullOrWhiteSpace(label))
                    ?? group.Key.SubjectId,
                RequestCount = group.Count(),
                TotalInputTokens = group.Sum(r => r.InputTokens),
                TotalOutputTokens = group.Sum(r => r.OutputTokens),
                LastActiveAt = group.Max(r => r.Timestamp)
            })
            .OrderByDescending(e => e.TotalTokens)
            .ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<UsageBreakdownEntry> GetContextBreakdown(string sourceId)
    {
        return SnapshotRecords()
            .Where(r => string.Equals(r.SourceId, sourceId, StringComparison.OrdinalIgnoreCase))
            .Where(r => !string.IsNullOrWhiteSpace(r.ContextId))
            .GroupBy(r => new { Kind = r.ContextKind ?? string.Empty, Id = r.ContextId! })
            .Select(group => new UsageBreakdownEntry
            {
                Kind = CollapseKinds(group.Select(r => r.ContextKind ?? string.Empty), TokenUsageContextKinds.Mixed),
                Id = group.Key.Id,
                Label = group
                    .Select(r => string.IsNullOrWhiteSpace(r.ContextLabel) ? group.Key.Id : r.ContextLabel!)
                    .FirstOrDefault(label => !string.IsNullOrWhiteSpace(label))
                    ?? group.Key.Id,
                RequestCount = group.Count(),
                RelatedSubjectCount = group
                    .Select(r => r.SubjectId)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                TotalInputTokens = group.Sum(r => r.InputTokens),
                TotalOutputTokens = group.Sum(r => r.OutputTokens),
                LastActiveAt = group.Max(r => r.Timestamp)
            })
            .OrderByDescending(e => e.TotalTokens)
            .ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void LoadFromDisk()
    {
        ClearRecords();

        if (_stateRuntime != null)
        {
            using var connection = _stateRuntime.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    timestamp,
                    source_id,
                    source_mode,
                    subject_kind,
                    subject_id,
                    subject_label,
                    context_kind,
                    context_id,
                    context_label,
                    thread_id,
                    session_key,
                    input_tokens,
                    output_tokens
                FROM dashboard_usage_records
                ORDER BY timestamp, id
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ApplyRecord(new TokenUsageRecord
                {
                    Timestamp = DateTimeOffset.Parse(reader.GetString(0)),
                    SourceId = reader.GetString(1),
                    SourceMode = reader.GetString(2),
                    SubjectKind = reader.GetString(3),
                    SubjectId = reader.GetString(4),
                    SubjectLabel = reader.GetString(5),
                    ContextKind = reader.IsDBNull(6) ? null : reader.GetString(6),
                    ContextId = reader.IsDBNull(7) ? null : reader.GetString(7),
                    ContextLabel = reader.IsDBNull(8) ? null : reader.GetString(8),
                    ThreadId = reader.IsDBNull(9) ? null : reader.GetString(9),
                    SessionKey = reader.IsDBNull(10) ? null : reader.GetString(10),
                    InputTokens = reader.GetInt64(11),
                    OutputTokens = reader.GetInt64(12)
                });
            }
            return;
        }

        if (_storagePath == null)
            return;

        var filePath = Path.Combine(_storagePath, "usage_records.jsonl");
        if (!File.Exists(filePath))
            return;

        try
        {
            var lines = File.ReadAllLines(filePath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    var record = JsonSerializer.Deserialize<TokenUsageRecord>(line, JsonOptions);
                    if (record != null)
                        ApplyRecord(record);
                }
                catch
                {
                    // Skip corrupted lines
                }
            }
        }
        catch
        {
            // Skip corrupted file
        }
    }

    private void ApplyRecord(TokenUsageRecord record)
    {
        _records.Enqueue(record);
    }

    private void ClearRecords()
    {
        while (_records.TryDequeue(out _))
        {
        }
    }

    private TokenUsageRecord[] SnapshotRecords()
    {
        return _records.ToArray();
    }

    private void PersistRecordToDb(TokenUsageRecord record)
    {
        try
        {
            using var connection = _stateRuntime!.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO dashboard_usage_records (
                    timestamp,
                    source_id,
                    source_mode,
                    subject_kind,
                    subject_id,
                    subject_label,
                    context_kind,
                    context_id,
                    context_label,
                    thread_id,
                    session_key,
                    input_tokens,
                    output_tokens
                ) VALUES (
                    $timestamp,
                    $source_id,
                    $source_mode,
                    $subject_kind,
                    $subject_id,
                    $subject_label,
                    $context_kind,
                    $context_id,
                    $context_label,
                    $thread_id,
                    $session_key,
                    $input_tokens,
                    $output_tokens
                )
                """;
            command.Parameters.AddWithValue("$timestamp", record.Timestamp.UtcDateTime.ToString("O"));
            command.Parameters.AddWithValue("$source_id", record.SourceId);
            command.Parameters.AddWithValue("$source_mode", record.SourceMode);
            command.Parameters.AddWithValue("$subject_kind", record.SubjectKind);
            command.Parameters.AddWithValue("$subject_id", record.SubjectId);
            command.Parameters.AddWithValue("$subject_label", record.SubjectLabel);
            command.Parameters.AddWithValue("$context_kind", (object?)record.ContextKind ?? DBNull.Value);
            command.Parameters.AddWithValue("$context_id", (object?)record.ContextId ?? DBNull.Value);
            command.Parameters.AddWithValue("$context_label", (object?)record.ContextLabel ?? DBNull.Value);
            command.Parameters.AddWithValue("$thread_id", (object?)record.ThreadId ?? DBNull.Value);
            command.Parameters.AddWithValue("$session_key", (object?)record.SessionKey ?? DBNull.Value);
            command.Parameters.AddWithValue("$input_tokens", record.InputTokens);
            command.Parameters.AddWithValue("$output_tokens", record.OutputTokens);
            command.ExecuteNonQuery();
        }
        catch
        {
            // Preserve current best-effort persistence semantics.
        }
    }

    private void PersistRecordToFile(TokenUsageRecord record)
    {
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(_storagePath!);
                var filePath = Path.Combine(_storagePath!, "usage_records.jsonl");
                var json = JsonSerializer.Serialize(record, JsonOptions);
                lock (_fileLock)
                {
                    File.AppendAllText(filePath, json + "\n");
                }
            }
            catch
            {
                // Silently ignore persistence errors
            }
        });
    }

    private static string CollapseKinds(IEnumerable<string> values, string mixedValue)
    {
        var distinct = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return distinct.Length switch
        {
            0 => string.Empty,
            1 => distinct[0],
            _ => mixedValue
        };
    }

    private static string? CollapseOptionalKinds(IEnumerable<string?> values, string mixedValue)
    {
        var distinct = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return distinct.Length switch
        {
            0 => null,
            1 => distinct[0],
            _ => mixedValue
        };
    }
}
