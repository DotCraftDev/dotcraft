using DotCraft.Memory;
using Spectre.Console;

namespace DotCraft.CLI;

/// <summary>
/// 会话选择器组件，封装 SelectionPrompt 交互逻辑
/// </summary>
public static class SessionPrompt
{
    /// <summary>
    /// 选择要加载的会话
    /// </summary>
    /// <param name="sessions">可用的会话列表</param>
    /// <param name="currentSessionId">当前会话 ID</param>
    /// <returns>选择的会话 Key，如果用户取消则返回 null</returns>
    public static string? SelectSessionToLoad(List<SessionStore.SessionInfo> sessions, string? currentSessionId)
    {
        if (sessions.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]没有可用的会话。[/]");
            return null;
        }

        AnsiConsole.WriteLine();

        // 添加 "Create New Session" 选项
        var options = sessions.Select(s => new SessionOption
        {
            Key = s.Key,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt,
            IsCurrent = s.Key == currentSessionId
        }).ToList();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<SessionOption>()
                .Title("[green]选择要加载的会话：[/]")
                .AddChoices(options)
                .UseConverter(o => FormatSessionOption(o))
                .PageSize(10));

        AnsiConsole.MarkupLine($"[green]✓[/] 已选择会话：[cyan]{EscapeMarkup(choice.Key)}[/]");
        AnsiConsole.WriteLine();

        return choice.Key;
    }

    /// <summary>
    /// 选择要删除的会话
    /// </summary>
    /// <param name="sessions">可用的会话列表</param>
    /// <param name="currentSessionId">当前会话 ID</param>
    /// <returns>选择的会话 Key，如果用户取消则返回 null</returns>
    public static string? SelectSessionToDelete(List<SessionStore.SessionInfo> sessions, string currentSessionId)
    {
        if (sessions.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]没有可删除的会话。[/]");
            return null;
        }

        AnsiConsole.WriteLine();

        var options = sessions.Select(s => new SessionOption
        {
            Key = s.Key,
            CreatedAt = s.CreatedAt,
            UpdatedAt = s.UpdatedAt,
            IsCurrent = s.Key == currentSessionId
        }).ToList();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<SessionOption>()
                .Title("[red]选择要删除的会话：[/]")
                .AddChoices(options)
                .UseConverter(o => FormatSessionOption(o))
                .PageSize(10));

        // 显示选择的会话信息
        AnsiConsole.MarkupLine($"[red]→[/] 已选择会话：[cyan]{EscapeMarkup(choice.Key)}[/]");
        AnsiConsole.WriteLine();

        return choice.Key;
    }

    /// <summary>
    /// 确认删除操作
    /// </summary>
    /// <param name="sessionId">要删除的会话 ID</param>
    /// <param name="isCurrent">是否是当前会话</param>
    /// <returns>用户是否确认删除</returns>
    public static bool ConfirmDelete(string sessionId, bool isCurrent)
    {
        var message = isCurrent
            ? $"[yellow]⚠️  您即将删除[cyan]当前[/]会话 '[cyan]{EscapeMarkup(sessionId)}[/]'。[/]\n[yellow]删除后将创建新会话。[/]"
            : $"[yellow]确定要删除会话 [cyan]{EscapeMarkup(sessionId)}[/]吗？[/]";

        var panel = new Panel(message)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();

        return AnsiConsole.Confirm("[red]删除此会话？[/]");
    }

    /// <summary>
    /// 格式化会话选项显示
    /// </summary>
    private static string FormatSessionOption(SessionOption option)
    {
        var prefix = option.IsCurrent ? "📍 " : "  ";
        var key = EscapeMarkup(option.Key);

        // 解析时间戳
        var updatedAt = ParseTimestamp(option.UpdatedAt);
        var timeAgo = GetTimeAgo(updatedAt);

        return $"{prefix}[cyan]{key}[/] [grey]({timeAgo})[/]";
    }

    /// <summary>
    /// 解析时间戳字符串
    /// </summary>
    private static DateTime ParseTimestamp(string? timestamp)
    {
        if (string.IsNullOrEmpty(timestamp))
            return DateTime.MinValue;

        if (DateTimeOffset.TryParse(timestamp, out var dto))
            return dto.LocalDateTime;

        return DateTime.MinValue;
    }

    /// <summary>
    /// 获取相对时间描述
    /// </summary>
    private static string GetTimeAgo(DateTime dateTime)
    {
        if (dateTime == DateTime.MinValue)
            return "未知";

        var now = DateTime.UtcNow;
        var diff = now - dateTime;

        if (diff.TotalMinutes < 1)
            return "刚刚";
        if (diff.TotalMinutes < 60)
            return $"{(int)diff.TotalMinutes}分钟前";
        if (diff.TotalHours < 24)
            return $"{(int)diff.TotalHours}小时前";
        if (diff.TotalDays < 7)
            return $"{(int)diff.TotalDays}天前";

        return $"{dateTime:yyyy-MM-dd}";
    }

    /// <summary>
    /// 会话选项数据结构
    /// </summary>
    private sealed class SessionOption
    {
        public string Key { get; set; } = string.Empty;
        
        public string? CreatedAt { get; set; }
        
        public string? UpdatedAt { get; set; }
        
        public bool IsCurrent { get; set; }
    }

    /// <summary>
    /// 转义 Spectre.Console 标记字符
    /// </summary>
    private static string EscapeMarkup(this string text)
    {
        return Markup.Escape(text);
    }
}
