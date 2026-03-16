using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using DotCraft.Protocol;

namespace DotCraft.AppServerTestClient;

/// <summary>
/// JSON-RPC 2.0 client for the DotCraft AppServer stdio protocol.
/// Spawns <c>dotcraft app-server</c> as a child process, writes requests to its stdin
/// and reads responses and notifications from its stdout.
///
/// Usage pattern:
/// <code>
/// await using var client = await AppServerClient.SpawnAsync(dotcraftBin, workspacePath);
/// await client.InitializeAsync();
/// var thread = await client.SendRequestAsync("thread/start", params);
/// </code>
///
/// Server-initiated requests (e.g. <c>item/approval/request</c>) are dispatched through
/// <see cref="ServerRequestHandler"/> if set, or enqueued in the notification queue otherwise.
/// </summary>
public sealed class AppServerClient : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StreamReader _stdout;
    private readonly StreamWriter _stdin;

    // Pending client-initiated requests keyed by id, awaiting a server response
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonDocument>> _pending = new();

    // Server-to-client notifications (no id), in arrival order
    private readonly ConcurrentQueue<JsonDocument> _notifications = new();
    private readonly SemaphoreSlim _notificationSignal = new(0);

    private Task? _readerTask;
    private readonly CancellationTokenSource _disposeCts = new();
    private int _nextId;

    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Optional handler for server-initiated JSON-RPC requests (messages with both
    /// <c>method</c> and <c>id</c>). The handler receives the full message document
    /// and returns a result object. The client sends a well-formed JSON-RPC response
    /// automatically using the same <c>id</c>.
    ///
    /// When null (default), server requests are placed in the notification queue so
    /// callers can handle them via <see cref="WaitForNotificationAsync"/>.
    /// </summary>
    public Func<JsonDocument, Task<object?>>? ServerRequestHandler { get; set; }

    private AppServerClient(Process process)
    {
        _process = process;
        _stdout = process.StandardOutput;
        _stdin = process.StandardInput;
    }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Spawns <c>dotcraft app-server</c> as a child process and starts the background reader.
    /// </summary>
    /// <param name="dotcraftBin">Path to the <c>dotcraft</c> executable.</param>
    /// <param name="workspacePath">Workspace path for the subprocess. Defaults to current directory.</param>
    public static Task<AppServerClient> SpawnAsync(
        string dotcraftBin,
        string? workspacePath = null)
    {
        var psi = new ProcessStartInfo(dotcraftBin, "app-server")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8
        };
        if (workspacePath != null)
            psi.WorkingDirectory = workspacePath;

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {dotcraftBin} app-server");

        var client = new AppServerClient(process);
        client._readerTask = Task.Run(client.ReaderLoopAsync);
        return Task.FromResult(client);
    }

    // -------------------------------------------------------------------------
    // High-level helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Performs the full initialize → initialized handshake.
    /// Returns the server's initialize response.
    /// </summary>
    public async Task<JsonDocument> InitializeAsync(
        bool approvalSupport = true,
        bool streamingSupport = true,
        List<string>? optOutMethods = null)
    {
        var result = await SendRequestAsync("initialize", new
        {
            clientInfo = new { name = "dotcraft-test-client", version = "0.1.0" },
            capabilities = new
            {
                approvalSupport,
                streamingSupport,
                optOutNotificationMethods = optOutMethods ?? new List<string>()
            }
        });
        // Send the initialized notification to complete the handshake
        await SendNotificationAsync("initialized");
        return result;
    }

    /// <summary>
    /// Streams notifications for the given turn until <c>turn/completed</c>,
    /// <c>turn/failed</c>, or <c>turn/cancelled</c> is received.
    /// Each notification (and unhandled server request) is passed to <paramref name="onNotification"/>.
    /// </summary>
    public async Task StreamTurnAsync(
        string threadId,
        string turnId,
        Action<JsonDocument>? onNotification = null,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(5));

        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            var notif = await WaitForNotificationAsync(null, remaining);
            if (notif == null)
                throw new TimeoutException($"Timed out waiting for turn {turnId} notifications");

            onNotification?.Invoke(notif);

            if (!notif.RootElement.TryGetProperty("method", out var method))
                continue;

            var m = method.GetString();
            if (m is "turn/completed" or "turn/failed" or "turn/cancelled")
                return;
        }

        throw new TimeoutException($"Turn {turnId} did not complete within the allowed time.");
    }

    // -------------------------------------------------------------------------
    // Core JSON-RPC primitives
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sends a JSON-RPC request and awaits the response.
    /// </summary>
    public async Task<JsonDocument> SendRequestAsync(
        string method,
        object? @params = null,
        TimeSpan? timeout = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            var request = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                method,
                @params
            }, SessionWireJsonOptions.Default);
            await WriteLineAsync(request);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
            await using var reg = cts.Token.Register(() => tcs.TrySetCanceled(cts.Token));

            return await tcs.Task;
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Sends a JSON-RPC notification (no id, no response expected).
    /// </summary>
    public async Task SendNotificationAsync(string method, object? @params = null)
    {
        var notification = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method,
            @params
        }, SessionWireJsonOptions.Default);
        await WriteLineAsync(notification);
    }

    /// <summary>
    /// Waits for the next notification matching <paramref name="method"/> (null means any).
    /// Returns null on timeout.
    /// </summary>
    public async Task<JsonDocument?> WaitForNotificationAsync(
        string? method = null,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));

        while (DateTime.UtcNow < deadline)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            try
            {
                await _notificationSignal.WaitAsync(remaining, _disposeCts.Token);
            }
            catch (OperationCanceledException) { break; }

            if (!_notifications.TryDequeue(out var notif))
                continue;

            if (method == null)
                return notif;

            if (notif.RootElement.TryGetProperty("method", out var m) && m.GetString() == method)
                return notif;

            // Not the one we wanted — re-enqueue and wait again
            _notifications.Enqueue(notif);
        }

        return null;
    }

    /// <summary>
    /// Sends a JSON-RPC response to a server-initiated request (for manual approval handling).
    /// </summary>
    public async Task SendResponseAsync(JsonElement requestId, object? result)
    {
        var response = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = requestId,
            result
        }, SessionWireJsonOptions.Default);
        await WriteLineAsync(response);
    }

    /// <summary>
    /// Sends an approval response back to the server (for item/approval/request).
    /// </summary>
    public Task SendApprovalResponseAsync(JsonElement requestId, string decision) =>
        SendResponseAsync(requestId, new { decision });

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
                var line = await _stdout.ReadLineAsync(ct);
                if (line == null) break; // EOF

                if (string.IsNullOrWhiteSpace(line)) continue;

                JsonDocument doc;
                try { doc = JsonDocument.Parse(line); }
                catch (JsonException) { continue; }

                var root = doc.RootElement;
                var hasMethod = root.TryGetProperty("method", out _);
                var hasId = root.TryGetProperty("id", out var idEl) &&
                            idEl.ValueKind != JsonValueKind.Null &&
                            idEl.ValueKind != JsonValueKind.Undefined;

                // Response to a client-initiated request (no method, numeric id)
                if (!hasMethod && hasId && idEl.ValueKind == JsonValueKind.Number)
                {
                    var id = idEl.GetInt32();
                    if (_pending.TryRemove(id, out var tcs))
                    {
                        tcs.TrySetResult(doc);
                        continue;
                    }
                }

                // Server-initiated request (has both method and id)
                if (hasMethod && hasId)
                {
                    if (ServerRequestHandler != null)
                    {
                        // Dispatch to handler on a background task so the reader loop isn't blocked
                        var capture = doc;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var result = await ServerRequestHandler(capture);
                                await SendResponseAsync(idEl, result);
                            }
                            catch (Exception ex)
                            {
                                // Send a JSON-RPC error response so the server isn't left waiting
                                var errResponse = JsonSerializer.Serialize(new
                                {
                                    jsonrpc = "2.0",
                                    id = idEl,
                                    error = new { code = -32603, message = ex.Message }
                                }, SessionWireJsonOptions.Default);
                                await WriteLineAsync(errResponse);
                            }
                        }, ct);
                        continue;
                    }

                    // No handler registered — fall through to notification queue so callers
                    // can handle it manually via WaitForNotificationAsync
                }

                // Pure notifications (has method, no id) and unhandled server requests
                if (hasMethod)
                {
                    _notifications.Enqueue(doc);
                    _notificationSignal.Release();
                }
            }
        }
        catch (OperationCanceledException) { /* Expected */ }
        finally
        {
            // Unblock all waiters
            foreach (var tcs in _pending.Values)
                tcs.TrySetCanceled();
            _pending.Clear();
        }
    }

    private async Task WriteLineAsync(string json)
    {
        await _stdin.WriteLineAsync(json);
        await _stdin.FlushAsync();
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public async ValueTask DisposeAsync()
    {
        await _disposeCts.CancelAsync();
        try { _stdin.Close(); } catch { }
        if (_readerTask != null)
            try { await _readerTask; } catch { }
        if (!_process.HasExited)
            try { _process.Kill(entireProcessTree: true); } catch { }
        _process.Dispose();
        _disposeCts.Dispose();
        _notificationSignal.Dispose();
    }
}
