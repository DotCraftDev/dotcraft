namespace DotCraft.Sessions.Protocol.AppServer;

/// <summary>
/// Abstraction over the physical transport layer for the AppServer JSON-RPC protocol.
/// V1 implements stdio JSONL; future versions may add WebSocket.
/// </summary>
public interface IAppServerTransport : IAsyncDisposable
{
    /// <summary>
    /// Reads the next client-initiated message (request or notification) from the transport.
    /// Returns <c>null</c> on EOF or when the transport is closed.
    /// Responses to server-initiated requests are routed internally and never returned here.
    /// </summary>
    Task<AppServerIncomingMessage?> ReadMessageAsync(CancellationToken ct = default);

    /// <summary>
    /// Sends any outbound message to the client. Thread-safe: may be called concurrently
    /// by the main loop, event dispatchers, and subscription handlers.
    /// </summary>
    Task WriteMessageAsync(object message, CancellationToken ct = default);

    /// <summary>
    /// Sends a server-initiated JSON-RPC request to the client and awaits the response.
    /// Used exclusively for the bidirectional approval flow (item/approval/request).
    /// </summary>
    /// <param name="method">The JSON-RPC method name.</param>
    /// <param name="params">Request parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="timeout">Optional response timeout. Defaults to 120 seconds.</param>
    Task<AppServerIncomingMessage> SendClientRequestAsync(
        string method,
        object? @params,
        CancellationToken ct = default,
        TimeSpan? timeout = null);
}
