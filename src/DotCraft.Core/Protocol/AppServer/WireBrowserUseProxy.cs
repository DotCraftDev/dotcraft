using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Tracing;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Routes agent-side browser-use calls to the Desktop client bound to the current thread.
/// </summary>
public sealed class WireBrowserUseProxy : IBrowserUseProxy
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly ConcurrentDictionary<string, BrowserUseThreadBinding> _byThread = new();

    /// <inheritdoc />
    public bool IsAvailable => GetCurrentBinding()?.Connection.HasBrowserUse == true;

    /// <summary>
    /// Binds a thread to the transport that created/resumed it.
    /// </summary>
    public void BindThread(string threadId, IAppServerTransport transport, AppServerConnection connection)
    {
        if (!connection.HasBrowserUse)
            return;
        _byThread[threadId] = new BrowserUseThreadBinding(threadId, transport, connection);
    }

    /// <summary>
    /// Removes all thread bindings for a disconnected transport.
    /// </summary>
    public void UnbindTransport(IAppServerTransport transport)
    {
        foreach (var kv in _byThread.ToArray())
        {
            if (ReferenceEquals(kv.Value.Transport, transport))
                _byThread.TryRemove(kv.Key, out _);
        }
    }

    /// <summary>
    /// Removes a single thread binding.
    /// </summary>
    public void UnbindThread(string threadId) => _byThread.TryRemove(threadId, out _);

    /// <inheritdoc />
    public async Task<BrowserUseEvaluateResult?> EvaluateAsync(
        string code,
        int? timeoutSeconds = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new BrowserUseEvaluateResult { Error = "BrowserJs requires non-empty code." };

        var threadId = ResolveCurrentThreadId();
        if (threadId == null || !_byThread.TryGetValue(threadId, out var binding))
            return null;

        var safeTimeout = Math.Clamp(timeoutSeconds ?? 30, 1, 120);
        var response = await binding.Transport.SendClientRequestAsync(
            AppServerMethods.ExtBrowserUseEvaluate,
            new
            {
                threadId,
                turnId = string.Empty,
                code,
                timeoutMs = safeTimeout * 1000
            },
            ct,
            TimeSpan.FromSeconds(safeTimeout + 5));

        if (!response.Result.HasValue)
            return new BrowserUseEvaluateResult
            {
                Error = response.Error.HasValue ? response.Error.Value.ToString() : "Browser-use client returned no result."
            };

        try
        {
            return response.Result.Value.Deserialize<BrowserUseEvaluateResult>(JsonOptions);
        }
        catch (Exception ex)
        {
            return new BrowserUseEvaluateResult { Error = $"Failed to parse browser-use response: {ex.Message}" };
        }
    }

    /// <inheritdoc />
    public async Task<bool> ResetAsync(CancellationToken ct = default)
    {
        var threadId = ResolveCurrentThreadId();
        if (threadId == null || !_byThread.TryGetValue(threadId, out var binding))
            return false;

        var response = await binding.Transport.SendClientRequestAsync(
            AppServerMethods.ExtBrowserUseReset,
            new { threadId },
            ct,
            TimeSpan.FromSeconds(10));
        if (!response.Result.HasValue)
            return false;
        try
        {
            var parsed = response.Result.Value.Deserialize<BrowserUseResetResult>(JsonOptions);
            return parsed?.Ok == true;
        }
        catch
        {
            return false;
        }
    }

    private BrowserUseThreadBinding? GetCurrentBinding()
    {
        var threadId = ResolveCurrentThreadId();
        if (threadId == null)
            return null;
        return _byThread.TryGetValue(threadId, out var b) ? b : null;
    }

    private static string? ResolveCurrentThreadId()
        => TracingChatClient.CurrentSessionKey ?? TracingChatClient.GetActiveSessionKey();

    internal static object MergeThreadIdIntoParams(string threadId, object? @params)
    {
        if (@params == null)
            return new { threadId };

        var node = JsonSerializer.SerializeToNode(@params, JsonOptions);
        if (node is JsonObject o)
        {
            o["threadId"] = threadId;
            return o;
        }

        return new { threadId, payload = @params };
    }

    private sealed record BrowserUseThreadBinding(
        string ThreadId,
        IAppServerTransport Transport,
        AppServerConnection Connection);

    private sealed class BrowserUseResetResult
    {
        public bool Ok { get; set; }
    }
}
