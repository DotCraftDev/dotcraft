using DotCraft.Commands.Core;
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
            await context.SessionService.ArchiveThreadAsync(context.SessionId);
        context.AgentFactory?.RemoveTokenTracker(context.SessionId);
        
        await responder.SendTextAsync("会话已清除，开始新的对话。");
        AnsiConsole.MarkupLine($"[grey][[{context.Source}]][/] [green]Session cleared:[/] {Markup.Escape(context.SessionId)}");
        
        return CommandResult.HandledResult();
    }
}
