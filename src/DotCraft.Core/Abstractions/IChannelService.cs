using DotCraft.Cron;
using DotCraft.Heartbeat;
using DotCraft.Protocol.AppServer;
using DotCraft.Security;

namespace DotCraft.Abstractions;

/// <summary>
/// Represents a channel service that handles communication for a specific platform.
/// Used by GatewayHost to run multiple channels concurrently.
/// </summary>
public interface IChannelService : IAsyncDisposable
{
    /// <summary>
    /// Gets the unique name of this channel.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The shared HeartbeatService injected by GatewayHost before the channel starts.
    /// Allows slash commands (/heartbeat) to operate within this channel.
    /// </summary>
    HeartbeatService? HeartbeatService { get; set; }

    /// <summary>
    /// The shared CronService injected by GatewayHost before the channel starts.
    /// Allows slash commands (/cron) to operate within this channel.
    /// </summary>
    CronService? CronService { get; set; }

    /// <summary>
    /// The channel-specific approval service, if any.
    /// Used by GatewayHost to route background-task approvals back to the originating channel.
    /// </summary>
    IApprovalService? ApprovalService { get; }

    /// <summary>
    /// The underlying channel client, if any.
    /// Used by GatewayHost to pass channel-specific tools (voice, file) to the shared agent runner.
    /// </summary>
    object? ChannelClient { get; }

    /// <summary>
    /// Starts the channel service. This is a long-running task that completes
    /// only when the channel is stopped or the cancellation token is triggered.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the channel service gracefully.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Delivers a message to a specific target within this channel.
    /// Used by GatewayHost for cross-channel routing (Cron results, Heartbeat notifications).
    /// </summary>
    /// <param name="target">The target identifier (e.g., user ID, group ID, chat ID).</param>
    /// <param name="content">The message content to deliver.</param>
    Task DeliverMessageAsync(string target, string content);

    /// <summary>
    /// Delivers a structured outbound message to a specific target within this channel.
    /// The default implementation preserves compatibility for text-only channels.
    /// </summary>
    async Task<ExtChannelSendResult> DeliverAsync(
        string target,
        ChannelOutboundMessage message,
        object? metadata = null,
        CancellationToken cancellationToken = default)
    {
        _ = metadata;
        _ = cancellationToken;

        if (string.Equals(message.Kind, "text", StringComparison.OrdinalIgnoreCase))
        {
            await DeliverMessageAsync(target, message.Text ?? string.Empty);
            return new ExtChannelSendResult { Delivered = true };
        }

        return new ExtChannelSendResult
        {
            Delivered = false,
            ErrorCode = "UnsupportedDeliveryKind",
            ErrorMessage = $"Channel '{Name}' does not support structured '{message.Kind}' delivery."
        };
    }

    /// <summary>
    /// Returns the list of delivery targets for admin notifications (e.g. Heartbeat results).
    /// Each target is passed to <see cref="DeliverMessageAsync"/> when broadcasting to admins.
    /// Return an empty list if this channel does not support admin notifications.
    /// </summary>
    IReadOnlyList<string> GetAdminTargets() => [];
}
