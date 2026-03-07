using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace DotCraft.Acp;

/// <summary>
/// stdio-based JSON-RPC transport for ACP.
/// Reads newline-delimited JSON-RPC messages from stdin and writes to stdout.
/// Supports bidirectional request/response for both Client→Agent and Agent→Client calls.
/// </summary>
public sealed class AcpTransport(Stream input, Stream output) : IAsyncDisposable
{
    private readonly StreamReader _reader = new(input, Encoding.UTF8);
    private readonly StreamWriter _writer = new(output, new UTF8Encoding(false)) { AutoFlush = true };
    private readonly Lock _writeLock = new();
    private int _nextOutgoingId;

    // Pending Agent→Client requests awaiting a response
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pendingClientRequests = new();

    // Incoming messages that are actual Client→Agent requests (not responses to our requests)
    private readonly ConcurrentQueue<JsonRpcRequest> _incomingRequests = new();
    private readonly SemaphoreSlim _incomingSemaphore = new(0);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private Task? _readerLoop;
    private readonly CancellationTokenSource _disposeCts = new();

    /// <summary>
    /// Optional protocol logger. Set before calling <see cref="StartReaderLoop"/>.
    /// </summary>
    public AcpLogger? Logger { get; set; }

    /// <summary>
    /// Creates a transport using stdin/stdout.
    /// </summary>
    public static AcpTransport CreateStdio()
    {
        return new AcpTransport(Console.OpenStandardInput(), Console.OpenStandardOutput());
    }

    /// <summary>
    /// Starts the background reader loop that dispatches incoming messages.
    /// Must be called before using ReadRequestAsync or SendClientRequestAsync.
    /// </summary>
    public void StartReaderLoop()
    {
        _readerLoop = Task.Run(ReaderLoopAsync);
    }

    private async Task ReaderLoopAsync()
    {
        var ct = _disposeCts.Token;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(ct);
                if (line == null) break; // EOF
                if (string.IsNullOrWhiteSpace(line)) continue;

                Logger?.LogIncoming(line);

                JsonElement root;
                try
                {
                    root = JsonSerializer.Deserialize<JsonElement>(line, JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                // Check if this is a response to one of our outgoing requests
                if (root.TryGetProperty("id", out var idProp) &&
                    !root.TryGetProperty("method", out _) &&
                    idProp.ValueKind == JsonValueKind.Number)
                {
                    var id = idProp.GetInt32();
                    if (_pendingClientRequests.TryRemove(id, out var tcs))
                    {
                        if (root.TryGetProperty("result", out var resultProp))
                            tcs.TrySetResult(resultProp);
                        else if (root.TryGetProperty("error", out var errorProp))
                            tcs.TrySetException(new AcpClientException(errorProp.ToString()));
                        else
                            tcs.TrySetResult(default);
                        continue;
                    }
                }

                // Otherwise, treat as an incoming request/notification
                try
                {
                    var request = JsonSerializer.Deserialize<JsonRpcRequest>(line, JsonOptions);
                    if (request != null)
                    {
                        _incomingRequests.Enqueue(request);
                        _incomingSemaphore.Release();
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed
                }
            }
        }
        catch (OperationCanceledException) { /* Expected on dispose */ }
        finally
        {
            // Signal EOF to unblock ReadRequestAsync and prevent process hang
            try { await _disposeCts.CancelAsync(); } catch { /* already disposed */ }
        }
    }

    /// <summary>
    /// Reads the next Client→Agent request/notification.
    /// Returns null on EOF / cancellation.
    /// </summary>
    public async Task<JsonRpcRequest?> ReadRequestAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _disposeCts.Token);
        try
        {
            await _incomingSemaphore.WaitAsync(linked.Token);
            if (_incomingRequests.TryDequeue(out var request))
                return request;
        }
        catch (OperationCanceledException) { /* Expected */ }

        return null;
    }

    /// <summary>
    /// Sends a JSON-RPC response.
    /// </summary>
    public void SendResponse(JsonElement? id, object? result)
    {
        var response = new JsonRpcResponse { Id = id, Result = result };
        WriteLine(response);
    }

    /// <summary>
    /// Sends a JSON-RPC error response.
    /// </summary>
    public void SendError(JsonElement? id, int code, string message, object? data = null)
    {
        var response = new JsonRpcResponse
        {
            Id = id,
            Error = new JsonRpcError { Code = code, Message = message, Data = data }
        };
        WriteLine(response);
    }

    /// <summary>
    /// Sends a JSON-RPC notification (no id, no response expected).
    /// </summary>
    public void SendNotification(string method, object? @params = null)
    {
        var notification = new JsonRpcNotification { Method = method, Params = @params };
        WriteLine(notification);
    }

    /// <summary>
    /// Sends a JSON-RPC request to the client (fire-and-forget, for permission requests).
    /// </summary>
    public void SendRequest(int id, string method, object? @params = null)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params
        };
        WriteLine(request);
    }

    /// <summary>
    /// Sends a JSON-RPC request to the client and awaits the response.
    /// Used for Agent→Client method calls (fs/readTextFile, terminal/create, etc.).
    /// An optional <paramref name="timeout"/> overrides the default 30-second limit.
    /// </summary>
    public async Task<JsonElement> SendClientRequestAsync(string method, object? @params, CancellationToken ct = default, TimeSpan? timeout = null)
    {
        var id = Interlocked.Increment(ref _nextOutgoingId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingClientRequests[id] = tcs;

        try
        {
            SendRequest(id, method, @params);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
            await using var reg = cts.Token.Register(() => tcs.TrySetCanceled());
            return await tcs.Task;
        }
        finally
        {
            _pendingClientRequests.TryRemove(id, out _);
        }
    }

    private void WriteLine(object message)
    {
        var json = JsonSerializer.Serialize(message, JsonOptions);
        Logger?.LogOutgoing(json);
        lock (_writeLock)
        {
            _writer.WriteLine(json);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _disposeCts.CancelAsync();
        _reader.Dispose();
        lock (_writeLock)
        {
            _writer.Dispose();
        }
        if (_readerLoop != null)
        {
            try { await _readerLoop; } catch { /* Expected */ }
        }
        _disposeCts.Dispose();
        _incomingSemaphore.Dispose();
    }
}

public sealed class AcpClientException(string message) : Exception(message);
