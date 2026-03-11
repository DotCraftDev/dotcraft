using DotCraft.Commands.Core;
using Spectre.Console;

namespace DotCraft.Commands.Handlers;

/// <summary>
/// Handles /stop command to cancel an in-progress agent run for the current session.
/// Only admins may use this command.
/// </summary>
public sealed class StopCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string[] Commands => ["/stop"];

    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (!context.IsAdmin)
        {
            await responder.SendTextAsync("权限不足，仅管理员可使用 /stop 命令。");
            return CommandResult.HandledResult();
        }

        var registry = context.ActiveRunRegistry;
        if (registry == null || !registry.TryCancelAndRemove(context.SessionId))
        {
            await responder.SendTextAsync("当前没有正在运行的 Agent。");
            AnsiConsole.MarkupLine($"[grey][[{context.Source}]][/] /stop: no active run for {Markup.Escape(context.SessionId)}");
            return CommandResult.HandledResult();
        }

        await responder.SendTextAsync("Agent 已停止。");
        AnsiConsole.MarkupLine($"[grey][[{context.Source}]][/] [yellow]/stop:[/] cancelled run for {Markup.Escape(context.SessionId)}");
        return CommandResult.HandledResult();
    }
}
