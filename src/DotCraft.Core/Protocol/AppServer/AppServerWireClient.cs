using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// JSON-RPC 2.0 client for the DotCraft AppServer stdio protocol.
/// Communicates over a pair of <see cref="Stream"/> objects (stdin/stdout of a subprocess,
/// in-memory pipes, or any other byte stream), implementing the full Session Wire Protocol.
///
/// Server-initiated requests (e.g. <c>item/approval/request</c>) are dispatched through
/// <see cref="ServerRequestHandler"/> when set; otherwise they are placed in the notification
/// queue and can be retrieved via <see cref="WaitForNotificationAsync"/>.
/// </summary>
public sealed class AppServerWireClient(Stream input, Stream output) : IAsyncDisposable
{
    private readonly StreamReader _reader = new(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
    private readonly StreamWriter _writer = new(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
    {
        AutoFlush = true
    };
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonDocument>> _pending = new();
    private readonly Channel<JsonDocument> _notifications = Channel.CreateUnbounded<JsonDocument>();
    private readonly Channel<JsonDocument> _jobResultNotifications = Channel.CreateUnbounded<JsonDocument>();

    private Task? _readerTask;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _nextId;
    private bool _disposed;

    /// <summary>
    /// Optional handler for server-initiated JSON-RPC requests (messages with both
    /// <c>method</c> and <c>id</c>). Receives the full message document and returns
    /// a result object to be sent back as the response.
    ///
    /// When null (default), server requests are placed in the notification queue.
    /// </summary>
    public Func<JsonDocument, Task<object?>>? ServerRequestHandler { get; set; }

    /// <summary>
    /// Starts the background reader loop. Must be called before sending any requests.
    /// </summary>
    public void Start() => _readerTask = Task.Run(ReaderLoopAsync);

    // -------------------------------------------------------------------------
    // Protocol helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Performs the full <c>initialize</c> → <c>initialized</c> handshake and
    /// returns the server's initialize response document.
    /// </summary>
    public async Task<JsonDocument> InitializeAsync(
        string clientName = "dotcraft-cli",
        string clientVersion = "0.1.0",
        bool approvalSupport = true,
        bool streamingSupport = true,
        IReadOnlyList<string>? optOutMethods = null,
        AcpExtensionCapability? acpExtensions = null)
    {
        object capabilities = acpExtensions is null
            ? new
            {
                approvalSupport,
                streamingSupport,
                optOutNotificationMethods = optOutMethods ?? Array.Empty<string>()
            }
            : new
            {
                approvalSupport,
                streamingSupport,
                optOutNotificationMethods = optOutMethods ?? Array.Empty<string>(),
                acpExtensions
            };

        var result = await SendRequestAsync(AppServerMethods.Initialize, new
        {
            clientInfo = new { name = clientName, version = clientVersion },
            capabilities
        });
        await SendNotificationAsync(AppServerMethods.Initialized);
        return result;
    }

    /// <summary>
    /// Reads all JSON-RPC notifications until a terminal turn event is received
    /// (<c>turn/completed</c>, <c>turn/failed</c>, or <c>turn/cancelled</c>).
    /// </summary>
    public async IAsyncEnumerable<JsonDocument> ReadTurnNotificationsAsync(
        TimeSpan? timeout = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(5));

        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) yield break;

            JsonDocument? notif;
            try { notif = await WaitForNotificationAsync(null, remaining, ct); }
            catch (OperationCanceledException) { yield break; }

            if (notif == null) yield break;

            yield return notif;

            if (notif.RootElement.TryGetProperty("method", out var m))
            {
                var method = m.GetString();
                if (method is AppServerMethods.TurnCompleted or AppServerMethods.TurnFailed or AppServerMethods.TurnCancelled)
                    yield break;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Cron management (spec Section 16)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lists cron jobs from the AppServer's in-memory CronService.
    /// Requires the server to advertise <c>cronManagement</c> capability.
    /// Throws <see cref="Exception"/> on wire errors or if the server returns a JSON-RPC error.
    /// </summary>
    public async Task<List<CronJobWireInfo>> CronListAsync(
        bool includeDisabled = false,
        CancellationToken ct = default)
    {
        var doc = await SendRequestAsync(
            AppServerMethods.CronList,
            new { includeDisabled },
            ct: ct);

        ThrowIfError(doc, "cron/list");

        var result = doc.RootElement.GetProperty("result");
        return JsonSerializer.Deserialize<CronListResult>(result.GetRawText(), SessionWireJsonOptions.Default)?.Jobs
               ?? [];
    }

    /// <summary>
    /// Removes a cron job from the AppServer's in-memory CronService.
    /// Throws <see cref="Exception"/> if the job is not found or a wire error occurs.
    /// </summary>
    public async Task CronRemoveAsync(string jobId, CancellationToken ct = default)
    {
        var doc = await SendRequestAsync(
            AppServerMethods.CronRemove,
            new { jobId },
            ct: ct);

        ThrowIfError(doc, jobId);
    }

    /// <summary>
    /// Enables or disables a cron job on the AppServer.
    /// Throws <see cref="Exception"/> if the job is not found or a wire error occurs.
    /// </summary>
    public async Task<CronJobWireInfo> CronEnableAsync(
        string jobId,
        bool enabled,
        CancellationToken ct = default)
    {
        var doc = await SendRequestAsync(
            AppServerMethods.CronEnable,
            new { jobId, enabled },
            ct: ct);

        ThrowIfError(doc, jobId);

        var result = doc.RootElement.GetProperty("result");
        return JsonSerializer.Deserialize<CronEnableResult>(result.GetRawText(), SessionWireJsonOptions.Default)?.Job
               ?? throw new InvalidOperationException($"Server returned empty job for '{jobId}'.");
    }

    /// <summary>
    /// Triggers an immediate heartbeat run on the server (spec Section 17.2).
    /// Uses a 120-second timeout because the heartbeat runs the full agent pipeline.
    /// </summary>
    public async Task<HeartbeatTriggerResult> HeartbeatTriggerAsync(CancellationToken ct = default)
    {
        var doc = await SendRequestAsync(
            AppServerMethods.HeartbeatTrigger,
            new { },
            timeout: TimeSpan.FromSeconds(120),
            ct: ct);

        ThrowIfError(doc, "heartbeat/trigger");

        var result = doc.RootElement.GetProperty("result");
        return JsonSerializer.Deserialize<HeartbeatTriggerResult>(
            result.GetRawText(), SessionWireJsonOptions.Default)
               ?? new HeartbeatTriggerResult();
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the JSON-RPC document contains an
    /// error field, using the error message from the server response.
    /// </summary>
    private static void ThrowIfError(JsonDocument doc, string context)
    {
        if (doc.RootElement.TryGetProperty("error", out var error))
        {
            var message = error.TryGetProperty("message", out var msg)
                ? msg.GetString() ?? "Unknown error"
                : "Unknown error";
            throw new InvalidOperationException(message);
        }
    }

    // -------------------------------------------------------------------------
    // Core JSON-RPC primitives
    // -------------------------------------------------------------------------

    /// <summary>Sends a JSON-RPC request and awaits the server response.</summary>
    public async Task<JsonDocument> SendRequestAsync(
        string method,
        object? @params = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            var request = JsonSerializer.Serialize(
                new { jsonrpc = "2.0", id, method, @params },
                SessionWireJsonOptions.Default);
            await WriteLineAsync(request);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token, ct);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
            await using var reg = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));

            return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>Sends a JSON-RPC notification (no id, no response expected).</summary>
    public async Task SendNotificationAsync(string method, object? @params = null)
    {
        var notification = JsonSerializer.Serialize(
            new { jsonrpc = "2.0", method, @params },
            SessionWireJsonOptions.Default);
        await WriteLineAsync(notification);
    }

    /// <summary>
    /// Waits for the next notification matching <paramref name="method"/> (null = any).
    /// Returns null on timeout or cancellation.
    /// </summary>
    public async Task<JsonDocument?> WaitForNotificationAsync(
        string? method = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
            cts.CancelAfter(remaining);

            JsonDocument notif;
            try { notif = await _notifications.Reader.ReadAsync(cts.Token); }
            catch (OperationCanceledException) { break; }
            catch (ChannelClosedException) { break; }

            if (method == null) return notif;

            if (notif.RootElement.TryGetProperty("method", out var m) && m.GetString() == method)
                return notif;

            // Not the requested method — re-enqueue and continue waiting
            _notifications.Writer.TryWrite(notif);
        }

        return null;
    }

    /// <summary>
    /// Waits for the next <c>system/jobResult</c> notification from the dedicated channel.
    /// Returns null on timeout or cancellation.
    /// </summary>
    public async Task<JsonDocument?> WaitForJobResultAsync(
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
            cts.CancelAfter(remaining);

            try { return await _jobResultNotifications.Reader.ReadAsync(cts.Token); }
            catch (OperationCanceledException) { break; }
            catch (ChannelClosedException) { break; }
        }

        return null;
    }

    /// <summary>
    /// Sends a JSON-RPC response to a server-initiated request.
    /// Used by the background reader loop when <see cref="ServerRequestHandler"/> is set.
    /// </summary>
    public async Task SendResponseAsync(int requestId, object? result)
    {
        var response = JsonSerializer.Serialize(
            new { jsonrpc = "2.0", id = requestId, result },
            SessionWireJsonOptions.Default);
        await WriteLineAsync(response);
    }

    // -------------------------------------------------------------------------
    // Background reader loop
    // -------------------------------------------------------------------------

    private async Task ReaderLoopAsync()
    {
        var ct = _disposeCts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                string? line;
                try { line = await _reader.ReadLineAsync(ct); }
                catch (OperationCanceledException) { break; }

                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                JsonDocument doc;
                try { doc = JsonDocument.Parse(line); }
                catch (JsonException) { continue; }

                var root = doc.RootElement;
                var hasMethod = root.TryGetProperty("method", out var methodEl);
                var hasId = root.TryGetProperty("id", out var idEl) &&
                            idEl.ValueKind != JsonValueKind.Null &&
                            idEl.ValueKind != JsonValueKind.Undefined;

                // Response to a pending client request
                if (!hasMethod && hasId && idEl.ValueKind == JsonValueKind.Number)
                {
                    if (_pending.TryGetValue(idEl.GetInt32(), out var tcs))
                    {
                        tcs.TrySetResult(doc);
                        continue;
                    }
                }

                // Server-initiated request (has both method and numeric id)
                if (hasMethod && hasId && idEl.ValueKind == JsonValueKind.Number)
                {
                    var handler = ServerRequestHandler;
                    if (handler != null)
                    {
                        var requestDoc = doc;
                        var reqId = idEl.GetInt32();
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var result = await handler(requestDoc);
                                await SendResponseAsync(reqId, result);
                            }
                            catch { /* failures silently suppressed; caller should handle internally */ }
                        }, ct);
                        continue;
                    }
                }

                // system/jobResult → dedicated channel to avoid being consumed during a turn
                if (hasMethod && methodEl.GetString() == AppServerMethods.SystemJobResult)
                {
                    _jobResultNotifications.Writer.TryWrite(doc);
                    continue;
                }

                // Notification or unhandled server request → notification queue
                _notifications.Writer.TryWrite(doc);
            }
        }
        catch (Exception) { /* reader loop terminated */ }
        finally
        {
            _notifications.Writer.TryComplete();
            _jobResultNotifications.Writer.TryComplete();
            foreach (var tcs in _pending.Values)
                tcs.TrySetCanceled();
        }
    }

    private async Task WriteLineAsync(string line)
    {
        await _writeLock.WaitAsync(_disposeCts.Token);
        try { await _writer.WriteLineAsync(line.AsMemory(), _disposeCts.Token); }
        finally { _writeLock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _disposeCts.CancelAsync();
        _reader.Dispose();
        await _writeLock.WaitAsync();
        try
        {
            await _writer.DisposeAsync();
        }
        finally
        {
            _writeLock.Release();
        }
        if (_readerTask != null)
        {
            try
            {
                await _readerTask;
            }
            catch
            {
                // ignored
            }
        }

        _disposeCts.Dispose();
        _writeLock.Dispose();
    }
}
