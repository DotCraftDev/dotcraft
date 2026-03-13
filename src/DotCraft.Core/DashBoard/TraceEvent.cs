using System.Collections.Concurrent;
using System.Text.Json.Serialization;

namespace DotCraft.DashBoard;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TraceEventType
{
    SessionMetadata,
    Request,
    Response,
    ToolCallStarted,
    ToolCallCompleted,
    TokenUsage,
    Error,
    ContextCompaction,
    Thinking
}

public sealed class TraceEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];

    public TraceEventType Type { get; init; }

    public string SessionKey { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public string? Content { get; init; }

    public string? ToolName { get; init; }

    public string? ToolIcon { get; init; }

    public string? ToolArguments { get; init; }

    public string? ToolResult { get; init; }

    public double? DurationMs { get; init; }

    public string? CallId { get; init; }

    public string? ResponseId { get; init; }

    public string? MessageId { get; init; }

    public string? ModelId { get; init; }

    public string? FinishReason { get; init; }

    public string? MetadataJson { get; init; }

    public string? FinalSystemPrompt { get; init; }

    public string[]? ToolNames { get; init; }

    public long? InputTokens { get; init; }

    public long? OutputTokens { get; init; }

    public long? TotalTokens { get; init; }
}

public sealed class TraceSession
{
    public string SessionKey { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;

    private long _totalInputTokens;
    private long _totalOutputTokens;
    private long _totalToolDurationMs;
    private long _maxToolDurationMs;

    public long TotalInputTokens => Interlocked.Read(ref _totalInputTokens);

    public long TotalOutputTokens => Interlocked.Read(ref _totalOutputTokens);

    public long TotalToolDurationMs => Interlocked.Read(ref _totalToolDurationMs);

    public long MaxToolDurationMs => Interlocked.Read(ref _maxToolDurationMs);

    public double AvgToolDurationMs => ToolCallCount > 0
        ? TotalToolDurationMs / (double)ToolCallCount
        : 0;

    public void AddInputTokens(long value) => Interlocked.Add(ref _totalInputTokens, value);

    public void AddOutputTokens(long value) => Interlocked.Add(ref _totalOutputTokens, value);

    public void AddToolDuration(long value)
    {
        Interlocked.Add(ref _totalToolDurationMs, value);

        long current;
        do
        {
            current = Interlocked.Read(ref _maxToolDurationMs);
            if (value <= current)
                break;
        } while (Interlocked.CompareExchange(ref _maxToolDurationMs, value, current) != current);
    }

    public int RequestCount { get; set; }

    public int ToolCallCount { get; set; }

    public int ResponseCount { get; set; }

    public int ErrorCount { get; set; }

    public int ContextCompactionCount { get; set; }

    public int ThinkingCount { get; set; }

    public string? FinalSystemPrompt { get; set; }

    public string? LastFinishReason { get; set; }

    public DateTimeOffset? SessionMetadataCapturedAt { get; set; }

    private string[] _toolNames = [];

    public IReadOnlyList<string> ToolNames => _toolNames;

    public void SetToolNames(IEnumerable<string>? toolNames)
    {
        if (toolNames == null)
            return;

        var normalized = toolNames
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
            return;

        _toolNames = normalized;
    }

    public ConcurrentBag<TraceEvent> Events { get; } = [];
}
