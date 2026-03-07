using DotCraft.Abstractions;

namespace DotCraft.Gateway;

/// <summary>
/// Routes messages from shared infrastructure (Cron, Heartbeat) to the appropriate channel.
/// </summary>
public interface IMessageRouter
{
    /// <summary>
    /// Delivers a message to a specific target within the named channel.
    /// </summary>
    /// <param name="channel">The channel name (e.g., "qq", "wecom", "api").</param>
    /// <param name="target">The target identifier within the channel (user ID, group ID, etc.).</param>
    /// <param name="content">The message content to deliver.</param>
    Task DeliverAsync(string channel, string target, string content);

    /// <summary>
    /// Delivers a message to all admin-capable channels (for Heartbeat notifications).
    /// </summary>
    Task BroadcastToAdminsAsync(string content);

    /// <summary>
    /// Registers a channel service with the router.
    /// </summary>
    void RegisterChannel(IChannelService service);
}
