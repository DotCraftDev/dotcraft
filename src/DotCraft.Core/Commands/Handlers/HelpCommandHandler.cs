using System.Text;
using DotCraft.Commands.Core;

namespace DotCraft.Commands.Handlers;

/// <summary>
/// Handles /help command to display available commands.
/// </summary>
public sealed class HelpCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string[] Commands => ["/help"];
    
    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        var sb = new StringBuilder();
        sb.AppendLine("可用命令：");
        sb.AppendLine("/new 或 /clear - 清除当前会话");
        sb.AppendLine("/debug - 切换调试模式（仅管理员）");
        sb.AppendLine("/heartbeat trigger - 立即触发心跳检查");
        sb.AppendLine("/cron list - 查看定时任务列表");
        sb.AppendLine("/cron remove <id> - 删除定时任务");
        sb.AppendLine("/help - 显示此帮助信息");
        
        await responder.SendTextAsync(sb.ToString().TrimEnd());
        return CommandResult.HandledResult();
    }
}
