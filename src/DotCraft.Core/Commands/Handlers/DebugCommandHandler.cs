using DotCraft.Commands.Core;
using Spectre.Console;

namespace DotCraft.Commands.Handlers;

/// <summary>
/// Handles /debug command to toggle debug mode.
/// </summary>
public sealed class DebugCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string[] Commands => ["/debug"];
    
    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (!context.IsAdmin)
        {
            await responder.SendTextAsync("⚠️ 此命令仅管理员可用。");
            return CommandResult.HandledResult();
        }
        
        var newState = Diagnostics.DebugModeService.Toggle();
        var statusMsg = newState ? "✅ 调试模式已开启" : "✅ 调试模式已关闭";
        await responder.SendTextAsync(statusMsg);
        
        AnsiConsole.MarkupLine(
            $"[grey][[{context.Source}]][/] [yellow]Debug mode {(newState ? "enabled" : "disabled")}[/] by [green]{Markup.Escape(context.UserName)}[/] (uid={context.UserId})");
        
        return CommandResult.HandledResult();
    }
}
