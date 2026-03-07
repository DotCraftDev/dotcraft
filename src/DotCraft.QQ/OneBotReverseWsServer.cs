using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DotCraft.QQ.OneBot;

namespace DotCraft.QQ;

public sealed class OneBotReverseWsServer(string host = "0.0.0.0", int port = 6700, string? accessToken = null)
    : IAsyncDisposable
{
    private HttpListener? _httpListener;
    
    private CancellationTokenSource? _cts;
    
    private readonly ConcurrentDictionary<string, WebSocket> _connections = new();
    
    private readonly ConcurrentDictionary<string, TaskCompletionSource<OneBotActionResponse>> _pendingRequests = new();
    
    private long _echoCounter;

    public event Func<OneBotMessageEvent, Task>? OnMessageEvent;
    
    public event Func<OneBotNoticeEvent, Task>? OnNoticeEvent;
    
    public event Func<OneBotRequestEvent, Task>? OnRequestEvent;
    
    public event Func<OneBotMetaEvent, Task>? OnMetaEvent;
    
    public event Action<string>? OnConnected;
    
    public event Action<string>? OnDisconnected;
    
    public event Action<string>? OnLog;

    public bool IsRunning => _httpListener?.IsListening == true;

    public int ConnectionCount => _connections.Count;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add($"http://{host}:{port}/");

        try
        {
            await Task.Run(_httpListener.Start, cancellationToken);
            Log($"OneBot reverse WebSocket server started on ws://{host}:{port}/");
            _ = AcceptConnectionsAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            Log($"Failed to start server: {ex.Message}");
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
            _cts = null;
        }

        foreach (var (_, ws) in _connections)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server shutting down", CancellationToken.None);
            }
            catch
            {
                // ignored
            }
        }
        _connections.Clear();

        _httpListener?.Stop();
        _httpListener?.Close();
        _httpListener = null;

        Log("OneBot reverse WebSocket server stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    public async Task<OneBotActionResponse> SendActionAsync(OneBotAction action, TimeSpan? timeout = null)
    {
        var echo = Interlocked.Increment(ref _echoCounter).ToString();
        action.Echo = echo;

        var tcs = new TaskCompletionSource<OneBotActionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[echo] = tcs;

        try
        {
            var json = action.ToJson();
            var bytes = Encoding.UTF8.GetBytes(json);

            var sent = false;
            foreach (var (_, ws) in _connections)
            {
                if (ws.State == WebSocketState.Open)
                {
                    await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                    sent = true;
                    break;
                }
            }

            if (!sent)
                throw new InvalidOperationException("No active WebSocket connection available.");

            var actualTimeout = timeout ?? TimeSpan.FromSeconds(30);
            using var cts = new CancellationTokenSource(actualTimeout);
            cts.Token.Register(() => tcs.TrySetCanceled());

            return await tcs.Task;
        }
        finally
        {
            _pendingRequests.TryRemove(echo, out _);
        }
    }

    private async Task AcceptConnectionsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _httpListener?.IsListening == true)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 400;
                    context.Response.Close();
                    continue;
                }

                if (!ValidateAccessToken(context.Request))
                {
                    context.Response.StatusCode = 401;
                    context.Response.Close();
                    Log("Connection rejected: invalid access token.");
                    continue;
                }

                _ = HandleWebSocketAsync(context, ct);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log($"Error accepting connection: {ex.Message}");
            }
        }
    }

    private bool ValidateAccessToken(HttpListenerRequest request)
    {
        if (string.IsNullOrEmpty(accessToken))
            return true;

        var auth = request.Headers["Authorization"];
        if (auth != null && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return auth.Substring(7) == accessToken;
        }

        var token = request.QueryString["access_token"];
        return token == accessToken;
    }

    private async Task HandleWebSocketAsync(HttpListenerContext httpContext, CancellationToken ct)
    {
        WebSocket? ws = null;
        var connectionId = Guid.NewGuid().ToString("N");

        try
        {
            var wsContext = await httpContext.AcceptWebSocketAsync(null);
            ws = wsContext.WebSocket;
            _connections[connectionId] = ws;

            Log($"Client connected: {connectionId} from {httpContext.Request.RemoteEndPoint}");
            OnConnected?.Invoke(connectionId);

            var buffer = new byte[64 * 1024];
            var messageBuffer = new MemoryStream();

            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None);
                    break;
                }

                messageBuffer.Write(buffer, 0, result.Count);

                if (result.EndOfMessage)
                {
                    var json = Encoding.UTF8.GetString(messageBuffer.ToArray());
                    messageBuffer.SetLength(0);

                    _ = Task.Run(() => ProcessMessageAsync(json), CancellationToken.None);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException ex)
        {
            Log($"WebSocket error for {connectionId}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Log($"Error handling connection {connectionId}: {ex.Message}");
        }
        finally
        {
            _connections.TryRemove(connectionId, out _);
            if (ws != null)
            {
                try
                {
                    ws.Dispose();
                }
                catch
                {
                }
            }
            Log($"Client disconnected: {connectionId}");
            OnDisconnected?.Invoke(connectionId);
        }
    }

    private async Task ProcessMessageAsync(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("echo", out var echoElement))
            {
                var echo = echoElement.GetString() ?? echoElement.GetRawText();
                if (_pendingRequests.TryGetValue(echo, out var tcs))
                {
                    var response = OneBotActionResponse.Parse(json);
                    if (response != null)
                        tcs.TrySetResult(response);
                    return;
                }
            }

            var evt = OneBotEvent.Parse(json);
            if (evt == null)
            {
                Log($"Failed to parse event: {json[..Math.Min(json.Length, 200)]}");
                return;
            }

            switch (evt)
            {
                case OneBotMessageEvent msgEvent:
                    if (OnMessageEvent != null)
                        await OnMessageEvent(msgEvent);
                    break;
                case OneBotNoticeEvent noticeEvent:
                    if (OnNoticeEvent != null)
                        await OnNoticeEvent(noticeEvent);
                    break;
                case OneBotRequestEvent requestEvent:
                    if (OnRequestEvent != null)
                        await OnRequestEvent(requestEvent);
                    break;
                case OneBotMetaEvent metaEvent:
                    if (OnMetaEvent != null)
                        await OnMetaEvent(metaEvent);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log($"Error processing message: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        OnLog?.Invoke(message);
    }
}
