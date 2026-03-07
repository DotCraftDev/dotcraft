using System.Text;
using DotCraft.Commands.Core;
using DotCraft.Cron;

namespace DotCraft.Commands.Handlers;

/// <summary>
/// Handles /cron command to manage scheduled tasks.
/// </summary>
public sealed class CronCommandHandler : ICommandHandler
{
    /// <inheritdoc />
    public string[] Commands => ["/cron"];
    
    /// <inheritdoc />
    public async Task<CommandResult> HandleAsync(CommandContext context, ICommandResponder responder)
    {
        if (context.CronService == null)
        {
            await responder.SendTextAsync("定时任务服务未启用。");
            return CommandResult.HandledResult();
        }
        
        var args = context.Arguments;
        var subCmd = args.Length > 0 ? args[0] : "list";
        
        switch (subCmd)
        {
            case "list":
                await HandleListAsync(context.CronService, responder);
                break;
            case "remove":
                await HandleRemoveAsync(context.CronService, args, responder);
                break;
            default:
                await responder.SendTextAsync("用法：/cron list | /cron remove <任务ID>");
                break;
        }
        
        return CommandResult.HandledResult();
    }
    
    private static async Task HandleListAsync(CronService cronService, ICommandResponder responder)
    {
        var jobs = cronService.ListJobs(includeDisabled: true);
        if (jobs.Count == 0)
        {
            await responder.SendTextAsync("暂无定时任务。");
            return;
        }
        
        var sb = new StringBuilder();
        sb.AppendLine($"定时任务 ({jobs.Count})：");
        foreach (var job in jobs)
        {
            var status = job.Enabled ? "已启用" : "已禁用";
            var schedDesc = job.Schedule.Kind switch
            {
                "at" when job.Schedule.AtMs.HasValue =>
                    $"一次性 {DateTimeOffset.FromUnixTimeMilliseconds(job.Schedule.AtMs.Value):u}",
                "every" when job.Schedule.EveryMs.HasValue =>
                    $"每 {TimeSpan.FromMilliseconds(job.Schedule.EveryMs.Value)}",
                _ => job.Schedule.Kind
            };
            var next = job.State.NextRunAtMs.HasValue
                ? DateTimeOffset.FromUnixTimeMilliseconds(job.State.NextRunAtMs.Value).ToString("u")
                : "-";
            sb.AppendLine($"[{job.Id}] {job.Name} ({status})");
            sb.AppendLine($"  计划：{schedDesc}");
            sb.AppendLine($"  下次执行：{next}");
        }
        
        await responder.SendTextAsync(sb.ToString().TrimEnd());
    }
    
    private static async Task HandleRemoveAsync(CronService cronService, string[] args, ICommandResponder responder)
    {
        if (args.Length < 2)
        {
            await responder.SendTextAsync("用法：/cron remove <任务ID>");
            return;
        }
        
        var jobId = args[1];
        if (cronService.RemoveJob(jobId))
            await responder.SendTextAsync($"任务 '{jobId}' 已删除。");
        else
            await responder.SendTextAsync($"未找到任务 '{jobId}'。");
    }
}
