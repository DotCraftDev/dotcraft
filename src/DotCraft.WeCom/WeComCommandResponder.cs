using DotCraft.Commands.Core;

namespace DotCraft.WeCom;

/// <summary>
/// WeCom channel implementation of ICommandResponder.
/// </summary>
public sealed class WeComCommandResponder(IWeComPusher pusher) : ICommandResponder
{
    /// <inheritdoc />
    public Task SendTextAsync(string message)
    {
        return pusher.PushTextAsync(message);
    }
    
    /// <inheritdoc />
    public Task SendMarkdownAsync(string markdown)
    {
        return pusher.PushMarkdownAsync(markdown);
    }
}
