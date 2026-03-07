using DotCraft.Abstractions;
using Spectre.Console;

namespace DotCraft.Gateway;

/// <summary>
/// Routes messages from shared infrastructure (Cron, Heartbeat) to the appropriate channel service.
/// </summary>
public sealed class MessageRouter : IMessageRouter
{
    private readonly Dictionary<string, IChannelService> _channels = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterChannel(IChannelService service)
    {
        _channels[service.Name] = service;
    }

    public async Task DeliverAsync(string channel, string target, string content)
    {
        if (_channels.TryGetValue(channel, out var service))
        {
            try
            {
                await service.DeliverMessageAsync(target, content);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine(
                    $"[grey][[Gateway]][/] [red]Delivery to {channel}/{target} failed: {Markup.Escape(ex.Message)}[/]");
            }
        }
        else
        {
            AnsiConsole.MarkupLine(
                $"[grey][[Gateway]][/] [yellow]No channel registered for '{Markup.Escape(channel)}', skipping delivery[/]");
        }
    }

    public async Task BroadcastToAdminsAsync(string content)
    {
        foreach (var channel in _channels.Values)
        {
            var targets = channel.GetAdminTargets();
            foreach (var target in targets)
            {
                try
                {
                    await channel.DeliverMessageAsync(target, content);
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine(
                        $"[grey][[Gateway]][/] [red]{Markup.Escape(channel.Name)} admin notify failed: {Markup.Escape(ex.Message)}[/]");
                }
            }
        }
    }
}
