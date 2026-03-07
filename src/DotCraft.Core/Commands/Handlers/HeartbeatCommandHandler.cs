using DotCraft.Commands.Core;

namespace DotCraft.Commands.Handlers;

/// <summary>
/// Handles /heartbeat command to trigger heartbeat checks.
/// </summary>
public sealed class HeartbeatCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string[] Commands => ["/heartbeat"];
    
    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.HeartbeatService == null)
        {
            await responder.SendTextAsync("心跳服务未启用。");
            return CommandResult.HandledResult();
        }
        
        var args = context.Arguments;
        var subCmd = args.Length > 0 ? args[0] : "trigger";
        
        if (subCmd == "trigger")
        {
            await responder.SendTextAsync("正在触发心跳检查...");
            var result = await context.HeartbeatService.TriggerNowAsync();
            if (result != null)
                await responder.SendTextAsync($"心跳结果：\n{result}");
            else
                await responder.SendTextAsync("无心跳响应（HEARTBEAT.md 可能不存在或为空）。");
        }
        else
        {
            await responder.SendTextAsync("用法：/heartbeat trigger");
        }
        
        return CommandResult.HandledResult();
    }
}
