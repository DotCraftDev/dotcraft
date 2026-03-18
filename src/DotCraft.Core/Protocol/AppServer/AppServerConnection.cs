using System.Collections.Concurrent;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Tracks per-connection state for an AppServer client.
/// One instance exists per active transport connection.
/// </summary>
public sealed class AppServerConnection
{
    private volatile bool _isInitialized;
    private volatile bool _isClientReady;
    private AppServerClientInfo? _clientInfo;
    private AppServerClientCapabilities? _clientCapabilities;
    private HashSet<string>? _optOutMethods;

    // Active passive thread subscriptions: threadId → CancellationTokenSource
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _subscriptions = new();

    // -------------------------------------------------------------------------
    // Initialization state
    // -------------------------------------------------------------------------

    /// <summary>
    /// True after a successful <c>initialize</c> request has been processed.
    /// Methods other than <c>initialize</c> are rejected until this is true.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// True after the client sends the <c>initialized</c> notification.
    /// The server may begin proactively sending notifications only after this is true.
    /// </summary>
    public bool IsClientReady => _isClientReady;

    /// <summary>Client identity from the <c>initialize</c> params.</summary>
    public AppServerClientInfo? ClientInfo => _clientInfo;

    /// <summary>Client-declared capabilities from the <c>initialize</c> params.</summary>
    public AppServerClientCapabilities? ClientCapabilities => _clientCapabilities;

    // -------------------------------------------------------------------------
    // Channel adapter state (external-channel-adapter.md §5.1)
    // -------------------------------------------------------------------------

    /// <summary>
    /// The canonical channel name if this connection is an external channel adapter.
    /// Null for regular AppServer clients (CLI, VS Code extension, etc.).
    /// </summary>
    public string? ChannelAdapterName { get; private set; }

    /// <summary>
    /// True if this connection represents an external channel adapter.
    /// </summary>
    public bool IsChannelAdapter => ChannelAdapterName != null;

    /// <summary>
    /// Whether the adapter supports receiving <c>ext/channel/deliver</c> requests.
    /// Default true; only meaningful when <see cref="IsChannelAdapter"/> is true.
    /// </summary>
    public bool SupportsDelivery { get; private set; } = true;

    /// <summary>
    /// Marks the connection as initialized and stores the client's identity and capabilities.
    /// Returns <c>false</c> if already initialized (caller should reject with AlreadyInitialized).
    /// </summary>
    public bool TryMarkInitialized(AppServerClientInfo info, AppServerClientCapabilities? caps)
    {
        if (_isInitialized)
            return false;

        _clientInfo = info;
        _clientCapabilities = caps;
        _optOutMethods = caps?.OptOutNotificationMethods is { Count: > 0 }
            ? new HashSet<string>(caps.OptOutNotificationMethods, StringComparer.Ordinal)
            : null;

        // Extract channel adapter capability (external-channel-adapter.md §5.1)
        if (caps?.ChannelAdapter is { } ca)
        {
            ChannelAdapterName = ca.ChannelName;
            SupportsDelivery = ca.DeliverySupport != false;
        }

        _isInitialized = true;
        return true;
    }

    /// <summary>
    /// Marks the connection as ready to receive server-initiated notifications.
    /// Called when the client sends the <c>initialized</c> notification.
    /// </summary>
    public void MarkClientReady() => _isClientReady = true;

    // -------------------------------------------------------------------------
    // Notification opt-out
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns <c>true</c> if the server should send a notification with the given method name.
    /// Clients may opt out of specific methods during initialization (Section 10 of the wire spec).
    /// </summary>
    public bool ShouldSendNotification(string method) =>
        _optOutMethods == null || !_optOutMethods.Contains(method);

    /// <summary>
    /// Returns <c>true</c> if the client declared approval support (default true when not specified).
    /// </summary>
    public bool SupportsApproval =>
        _clientCapabilities?.ApprovalSupport != false;

    /// <summary>
    /// Returns <c>true</c> if the client declared streaming support (default true when not specified).
    /// </summary>
    public bool SupportsStreaming =>
        _clientCapabilities?.StreamingSupport != false;

    // -------------------------------------------------------------------------
    // Thread subscriptions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a passive subscription for the given thread.
    /// Returns <c>false</c> if a subscription already exists (duplicate subscribe is a no-op).
    /// </summary>
    public bool TryAddSubscription(string threadId, CancellationTokenSource cts)
    {
        if (!_subscriptions.TryAdd(threadId, cts))
            return false;

        return true;
    }

    /// <summary>
    /// Cancels and removes the passive subscription for the given thread.
    /// Returns <c>false</c> if no subscription was found.
    /// </summary>
    public bool TryCancelSubscription(string threadId)
    {
        if (!_subscriptions.TryRemove(threadId, out var cts))
            return false;

        cts.Cancel();
        cts.Dispose();
        return true;
    }

    /// <summary>
    /// Returns <c>true</c> if the connection has an active passive subscription for the given thread.
    /// </summary>
    public bool HasSubscription(string threadId) => _subscriptions.ContainsKey(threadId);

    /// <summary>
    /// Cancels all active subscriptions. Called on connection close or dispose.
    /// </summary>
    public void CancelAllSubscriptions()
    {
        foreach (var (key, cts) in _subscriptions)
        {
            if (_subscriptions.TryRemove(key, out _))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
    }
}
