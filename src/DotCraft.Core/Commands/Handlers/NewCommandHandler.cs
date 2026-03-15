using DotCraft.Commands.Core;
using DotCraft.Sessions.Protocol;
using Spectre.Console;

namespace DotCraft.Commands.Handlers;

/// <summary>
/// Handles /new and /clear commands to clear the current session.
/// </summary>
public sealed class NewCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string[] Commands => ["/new", "/clear"];
    
    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.SessionService != null)
        {
            // Look up the active thread(s) for this user/channel identity instead of using
            // the channel-format session key, which is not a valid Session Protocol thread ID.
            var identity = new SessionIdentity
            {
                ChannelName = context.Source.ToLowerInvariant(),
                UserId = context.UserId,
                ChannelContext = context.GroupId,
                WorkspacePath = context.WorkspacePath
            };
            var threads = await context.SessionService.FindThreadsAsync(identity);
            foreach (var t in threads)
                await context.SessionService.ArchiveThreadAsync(t.Id);
        }
        context.AgentFactory?.RemoveTokenTracker(context.SessionId);
        
        await responder.SendTextAsync("会话已清除，开始新的对话。");
        AnsiConsole.MarkupLine($"[grey][[{context.Source}]][/] [green]Session cleared:[/] {Markup.Escape(context.SessionId)}");
        
        return CommandResult.HandledResult();
    }
}
