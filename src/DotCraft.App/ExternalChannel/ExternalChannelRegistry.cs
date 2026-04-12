using System.Collections.Concurrent;

namespace DotCraft.ExternalChannel;

/// <summary>
/// Thread-safe registry of WebSocket-mode external channels.
/// <para>
/// <see cref="ExternalChannelManager"/> registers WebSocket-mode <see cref="ExternalChannelHost"/>
/// instances here during construction. <c>AppServerHost</c> queries this registry when
/// a WebSocket client completes the <c>initialize</c> handshake with a <c>channelAdapter</c>
/// capability, routing the connection to the matching host.
/// </para>
/// </summary>
public sealed class ExternalChannelRegistry
{
    private readonly ConcurrentDictionary<string, ExternalChannelHost> _hosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a WebSocket-mode external channel host.
    /// </summary>
    public void Register(string channelName, ExternalChannelHost host)
    {
        if (!_hosts.TryAdd(channelName, host))
            throw new InvalidOperationException(
                $"External channel '{channelName}' is already registered.");
    }

    /// <summary>
    /// Attempts to find a registered external channel host by name.
    /// </summary>
    public bool TryGet(string channelName, out ExternalChannelHost? host)
        => _hosts.TryGetValue(channelName, out host);

    /// <summary>
    /// Checks whether a channel name is registered (for validation during config check).
    /// </summary>
    public bool IsRegistered(string channelName)
        => _hosts.ContainsKey(channelName);

    /// <summary>
    /// Returns a point-in-time snapshot of all registered hosts.
    /// </summary>
    public IReadOnlyList<ExternalChannelHost> SnapshotHosts()
        => _hosts.Values.ToArray();

    /// <summary>
    /// Removes a channel from the registry (e.g. on shutdown).
    /// </summary>
    public bool Unregister(string channelName)
        => _hosts.TryRemove(channelName, out _);
}
