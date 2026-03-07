using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DotCraft.Editor.Protocol;
using DotCraft.Editor.Settings;
using UnityEngine;

namespace DotCraft.Editor.Connection
{
    /// <summary>
    /// stdio-based JSON-RPC transport for ACP on the Unity side.
    /// Symmetric implementation to DotCraft's AcpTransport.
    /// </summary>
    public sealed class AcpTransportClient : IDisposable
    {
        private StreamReader _reader;
        private StreamWriter _writer;
        private readonly object _writeLock = new();
        private int _nextOutgoingId;
        private bool _isRunning;

        // Pending requests awaiting response (Client→Agent)
        private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();

        // Incoming Agent→Client requests queue
        private readonly SemaphoreSlim _incomingSemaphore = new(0);

        // Request handlers
        private readonly ConcurrentDictionary<string, Func<JsonElement, Task<object>>> _requestHandlers = new();

        // Extension method handlers keyed by prefix (e.g. "_unity/")
        private readonly ConcurrentDictionary<string, Func<string, JsonElement, Task<object>>> _extensionHandlers = new();

        private Task _readerLoopTask;
        private CancellationTokenSource _readerCts;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>
        /// Event raised when a session/update notification is received.
        /// </summary>
        public event Action<JsonElement> OnSessionUpdate;

        /// <summary>
        /// Event raised when any notification is received.
        /// </summary>
        public event Action<string, JsonElement> OnNotification;

        /// <summary>
        /// Event raised when transport encounters an error.
        /// </summary>
        public event Action<string> OnError;

        /// <summary>
        /// Initializes the transport with the given streams.
        /// </summary>
        public void Initialize(Stream input, Stream output)
        {
            _reader = new StreamReader(input, Encoding.UTF8);
            _writer = new StreamWriter(output, new UTF8Encoding(false)) { AutoFlush = true };
        }

        /// <summary>
        /// Starts the background reader loop.
        /// </summary>
        public void StartReaderLoop()
        {
            if (_isRunning) return;

            _isRunning = true;
            _readerCts = new CancellationTokenSource();
            _readerLoopTask = Task.Run(() => ReaderLoopAsync(_readerCts.Token));
        }

        /// <summary>
        /// Synchronously marks the reader loop as stopped and cancels its token.
        /// Call this before killing the process so that the reader loop sees the
        /// cancellation before it encounters the EOF from the closed pipe, preventing
        /// a spurious "Connection closed by agent." error during intentional shutdown.
        /// </summary>
        public void CancelReaderLoop()
        {
            _isRunning = false;
            _readerCts?.Cancel();
        }

        /// <summary>
        /// Stops the reader loop.
        /// </summary>
        public async Task StopReaderLoopAsync()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _readerCts?.Cancel();

            if (_readerLoopTask != null)
            {
                // ReadLineAsync has no CancellationToken overload, so the loop
                // can only exit when the underlying stream closes. Use a timeout
                // to avoid blocking indefinitely if that hasn't happened yet.
                try
                {
                    // ConfigureAwait(false) prevents the continuation from being posted back to
                    // the Unity SynchronizationContext. Without it, calling .Wait() on this
                    // method from the main thread (e.g. Dispose) would deadlock for the full
                    // 2-second timeout even though the reader loop exits almost immediately after
                    // the process is killed.
                    await Task.WhenAny(_readerLoopTask, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
                }
                catch
                {
                    // ignored
                }
            }

            _readerCts?.Dispose();
            _readerCts = null;
        }

        /// <summary>
        /// Registers a handler for Agent→Client requests.
        /// </summary>
        public void RegisterHandler(string method, Func<JsonElement, Task<object>> handler)
        {
            _requestHandlers[method] = handler;
        }

        /// <summary>
        /// Registers a handler for extension methods matching the given prefix (e.g. "_unity/").
        /// The method name is passed as a separate parameter instead of being injected into params.
        /// </summary>
        public void RegisterExtensionHandler(string prefix, Func<string, JsonElement, Task<object>> handler)
        {
            _extensionHandlers[prefix] = handler;
        }

        /// <summary>
        /// Unregisters a handler.
        /// </summary>
        public void UnregisterHandler(string method)
        {
            _requestHandlers.TryRemove(method, out _);
        }

        /// <summary>
        /// Sends a request to the Agent and awaits the response.
        /// </summary>
        public async Task<JsonElement> SendRequestAsync(string method, object @params, CancellationToken ct = default, TimeSpan? timeout = null)
        {
            if (_writer == null)
                throw new InvalidOperationException("Transport not initialized.");

            var id = Interlocked.Increment(ref _nextOutgoingId);
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingRequests[id] = tcs;

            try
            {
                var request = new
                {
                    jsonrpc = "2.0",
                    id,
                    method,
                    @params
                };

                WriteLine(request);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
                using var reg = cts.Token.Register(() => tcs.TrySetCanceled());

                return await tcs.Task;
            }
            finally
            {
                _pendingRequests.TryRemove(id, out _);
            }
        }

        /// <summary>
        /// Sends a notification to the Agent (no response expected).
        /// </summary>
        public void SendNotification(string method, object @params = null)
        {
            if (_writer == null)
                throw new InvalidOperationException("Transport not initialized.");

            var notification = new JsonRpcNotification { Method = method, Params = @params };
            WriteLine(notification);
        }

        /// <summary>
        /// Sends a response to an Agent→Client request.
        /// </summary>
        public void SendResponse(JsonElement? id, object result)
        {
            if (_writer == null) return;

            var response = new JsonRpcResponse { Id = id, Result = result };
            WriteLine(response);
        }

        /// <summary>
        /// Sends an error response to an Agent→Client request.
        /// </summary>
        public void SendError(JsonElement? id, int code, string message, object data = null)
        {
            if (_writer == null) return;

            var response = new JsonRpcResponse
            {
                Id = id,
                Error = new JsonRpcError { Code = code, Message = message, Data = data }
            };
            WriteLine(response);
        }

        private async Task ReaderLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _reader != null)
                {
                    var line = await _reader.ReadLineAsync();
                    if (line == null)
                    {
                        // EOF — only report if unexpected (not an intentional shutdown)
                        if (!ct.IsCancellationRequested)
                            OnError?.Invoke("Connection closed by agent.");
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (DotCraftSettings.Instance.VerboseLogging)
                    {
                        Debug.Log($"[DotCraft ←] {line}");
                    }

                    ProcessMessage(line);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                OnError?.Invoke($"Reader error: {ex.Message}");
            }
            catch (Exception)
            {
                // ignored
            }
        }

        private void ProcessMessage(string line)
        {
            JsonElement root;
            try
            {
                root = JsonSerializer.Deserialize<JsonElement>(line, JsonOptions);
            }
            catch (JsonException)
            {
                // Non-JSON lines (e.g. startup diagnostics) are expected; skip silently.
                if (DotCraftSettings.Instance.VerboseLogging)
                {
                    Debug.Log($"[DotCraft] Skipping non-JSON line: {line}");
                }
                return;
            }

            // Check if this is a response to one of our requests
            if (root.TryGetProperty("id", out var idProp) &&
                !root.TryGetProperty("method", out _) &&
                idProp.ValueKind == JsonValueKind.Number)
            {
                var id = idProp.GetInt32();
                if (_pendingRequests.TryRemove(id, out var tcs))
                {
                    if (root.TryGetProperty("result", out var resultProp))
                    {
                        tcs.TrySetResult(resultProp);
                    }
                    else if (root.TryGetProperty("error", out var errorProp))
                    {
                        tcs.TrySetException(new AcpTransportException(errorProp.ToString()));
                    }
                    else
                    {
                        tcs.TrySetResult(default);
                    }
                }
                return;
            }

            // Check if this is a request from the Agent
            if (root.TryGetProperty("method", out var methodProp))
            {
                var method = methodProp.GetString();
                var id = root.TryGetProperty("id", out idProp) ? idProp : (JsonElement?)null;
                var hasParams = root.TryGetProperty("params", out var paramsProp);
                var @params = hasParams ? paramsProp : (JsonElement?)null;

                // Check if it's a notification (no id)
                if (id == null)
                {
                    HandleNotification(method, @params);
                }
                else
                {
                    // It's a request - need to respond
                    HandleRequestAsync(method, @params, id.Value).Forget();
                }
            }
        }

        private void HandleNotification(string method, JsonElement? @params)
        {
            if (method == AcpMethods.SessionUpdate && @params.HasValue)
            {
                OnSessionUpdate?.Invoke(@params.Value);
            }

            OnNotification?.Invoke(method, @params ?? default);
        }

        private async Task HandleRequestAsync(string method, JsonElement? @params, JsonElement id)
        {
            try
            {
                if (_requestHandlers.TryGetValue(method, out var handler))
                {
                    var result = await handler(@params ?? default);
                    SendResponse(id, result);
                }
                else if (TryGetExtensionHandler(method, out var extHandler))
                {
                    // Extension method handling - pass method name separately
                    var result = await extHandler(method, @params ?? default);
                    SendResponse(id, result);
                }
                else
                {
                    SendError(id, -32601, $"Method not found: {method}");
                }
            }
            catch (Exception ex)
            {
                SendError(id, -32603, $"Internal error: {ex.Message}");
            }
        }

        private bool TryGetExtensionHandler(string method, out Func<string, JsonElement, Task<object>> handler)
        {
            foreach (var kvp in _extensionHandlers)
            {
                if (method.StartsWith(kvp.Key, StringComparison.Ordinal))
                {
                    handler = kvp.Value;
                    return true;
                }
            }

            handler = null;
            return false;
        }

        private void WriteLine(object message)
        {
            var json = JsonSerializer.Serialize(message, JsonOptions);

            if (DotCraftSettings.Instance.VerboseLogging)
            {
                Debug.Log($"[DotCraft →] {json}");
            }

            lock (_writeLock)
            {
                _writer?.WriteLine(json);
            }
        }

        public void Dispose()
        {
            StopReaderLoopAsync().Wait(TimeSpan.FromSeconds(2));

            try { _reader?.Dispose(); }
            catch
            {
                // ignored
            }

            try { _writer?.Dispose(); }
            catch
            {
                // ignored
            }

            _reader = null;
            _writer = null;
            _incomingSemaphore?.Dispose();

            // Cancel all pending requests
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetCanceled();
            }
            _pendingRequests.Clear();
        }
    }

    public sealed class AcpTransportException : Exception
    {
        public AcpTransportException(string message) : base(message) { }
    }

    internal static class TaskExtensions
    {
        public static async void Forget(this Task task)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
