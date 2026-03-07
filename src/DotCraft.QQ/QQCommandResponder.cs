using DotCraft.Commands.Core;
using DotCraft.QQ.OneBot;

namespace DotCraft.QQ;

/// <summary>
/// QQ channel implementation of ICommandResponder.
/// </summary>
public sealed class QQCommandResponder(QQBotClient client, OneBotMessageEvent evt) : ICommandResponder
{
    /// <inheritdoc />
    public Task SendTextAsync(string message)
    {
        return client.SendMessageAsync(evt, message);
    }
    
    /// <inheritdoc />
    public Task SendMarkdownAsync(string markdown)
    {
        // QQ doesn't support markdown, send as plain text
        return client.SendMessageAsync(evt, markdown);
    }
}
