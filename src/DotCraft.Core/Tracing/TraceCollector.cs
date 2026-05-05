using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Tracing;

public sealed class TraceCollector(TraceStore store)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public void RecordRequest(string sessionKey, string prompt)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.Request,
            SessionKey = sessionKey,
            Content = prompt
        });
    }

    public void RecordSessionMetadata(string sessionKey, string? finalSystemPrompt, IEnumerable<string>? toolNames)
    {
        var normalizedToolNames = NormalizeToolNames(toolNames);
        var existing = store.GetSession(sessionKey);
        var hasPrompt = !string.IsNullOrWhiteSpace(existing?.FinalSystemPrompt);
        var hasTools = existing is { ToolNames.Count: > 0 };

        store.UpsertSessionMetadata(sessionKey, finalSystemPrompt, normalizedToolNames);

        if (hasPrompt && hasTools)
            return;

        store.Record(new TraceEvent
        {
            Type = TraceEventType.SessionMetadata,
            SessionKey = sessionKey,
            FinalSystemPrompt = finalSystemPrompt,
            ToolNames = normalizedToolNames
        });
    }

    public void RecordResponse(string sessionKey, string? response, DateTimeOffset? timestamp = null)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.Response,
            SessionKey = sessionKey,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Content = response ?? "(empty)"
        });
    }

    public void RecordResponse(
        string sessionKey,
        string? response,
        string? responseId,
        string? messageId,
        string? modelId,
        string? finishReason,
        object? metadata = null,
        DateTimeOffset? timestamp = null)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.Response,
            SessionKey = sessionKey,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Content = response ?? "(empty)",
            ResponseId = responseId,
            MessageId = messageId,
            ModelId = modelId,
            FinishReason = finishReason,
            MetadataJson = SerializeMetadata(metadata)
        });
    }

    public void RecordToolCallStarted(string sessionKey, FunctionCallContent fc)
    {
        string? argsJson = null;
        if (fc.Arguments != null)
        {
            try
            {
                argsJson = JsonSerializer.Serialize(fc.Arguments, JsonOptions);
            }
            catch
            {
                argsJson = fc.Arguments.ToString();
            }
        }

        store.Record(new TraceEvent
        {
            Type = TraceEventType.ToolCallStarted,
            SessionKey = sessionKey,
            ToolName = fc.Name,
            ToolIcon = ToolRegistry.GetToolIcon(fc.Name),
            ToolArguments = argsJson,
            Content = fc.CallId,
            CallId = fc.CallId
        });
    }

    public void RecordToolCallCompleted(string sessionKey, FunctionResultContent fr, string? toolName, double durationMs)
    {
        var result = Agents.ImageContentSanitizingChatClient.DescribeResult(fr.Result);
        store.Record(new TraceEvent
        {
            Type = TraceEventType.ToolCallCompleted,
            SessionKey = sessionKey,
            ToolName = toolName ?? "unknown",
            ToolIcon = ToolRegistry.GetToolIcon(toolName ?? ""),
            ToolResult = result,
            DurationMs = durationMs,
            Content = fr.CallId,
            CallId = fr.CallId
        });
    }

    public void RecordToolInjection(string sessionKey, IReadOnlyList<string> toolNames)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.ToolInjection,
            SessionKey = sessionKey,
            ToolName = $"{toolNames.Count} tool{(toolNames.Count != 1 ? "s" : "")} injected",
            ToolIcon = "🔌",
            Content = string.Join(", ", toolNames)
        });
    }

    public void RecordTokenUsage(string sessionKey, long inputTokens, long outputTokens)
    {
        var total = inputTokens + outputTokens;
        store.Record(new TraceEvent
        {
            Type = TraceEventType.TokenUsage,
            SessionKey = sessionKey,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = total
        });
    }

    public void RecordError(string sessionKey, string error)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.Error,
            SessionKey = sessionKey,
            Content = error
        });
    }

    public void RecordContextCompaction(string sessionKey)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.ContextCompaction,
            SessionKey = sessionKey,
            Content = "Context compacted due to token limit"
        });
    }

    public void RecordThinking(string sessionKey, string content, DateTimeOffset? timestamp = null)
    {
        store.Record(new TraceEvent
        {
            Type = TraceEventType.Thinking,
            SessionKey = sessionKey,
            Timestamp = timestamp ?? DateTimeOffset.UtcNow,
            Content = content
        });
    }

    public void BindThreadMainSession(string threadId, DateTimeOffset? createdAt = null)
        => store.BindThreadMainSession(threadId, createdAt);

    public void BindChildSession(
        string sessionKey,
        string rootThreadId,
        string parentSessionKey,
        DateTimeOffset? createdAt = null)
        => store.BindChildSession(sessionKey, rootThreadId, parentSessionKey, createdAt);

    public string? ResolveRootThreadId(string sessionKey)
        => store.DescribeSessionDeletion(sessionKey).RootThreadId;

    public ToolCallTimer StartToolTimer()
    {
        return new ToolCallTimer();
    }

    private static string? SerializeMetadata(object? metadata)
    {
        if (metadata == null)
            return null;

        try
        {
            return JsonSerializer.Serialize(metadata, JsonOptions);
        }
        catch
        {
            return metadata.ToString();
        }
    }

    private static string[] NormalizeToolNames(IEnumerable<string>? toolNames)
    {
        if (toolNames == null)
            return [];

        return toolNames
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed class ToolCallTimer
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public double ElapsedMs => _stopwatch.Elapsed.TotalMilliseconds;

    public void Stop() => _stopwatch.Stop();
}
