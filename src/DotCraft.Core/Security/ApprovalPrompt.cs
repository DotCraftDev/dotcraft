using Spectre.Console;

namespace DotCraft.Security;

/// <summary>
/// 审批选项枚举
/// </summary>
public enum ApprovalOption
{
    Once,      // 仅此一次
    Session,   // 本次会话
    Always,    // 永久
    Reject     // 拒绝
}

/// <summary>
/// 审批提示工具类，封装SelectionPrompt交互逻辑
/// </summary>
public static class ApprovalPrompt
{
    /// <summary>
    /// 请求文件操作审批
    /// </summary>
    /// <param name="operation">操作名称（read, write, edit, list）</param>
    /// <param name="path">文件路径</param>
    /// <returns>审批选项</returns>
    public static ApprovalOption RequestFileApproval(string operation, string path)
    {
        AnsiConsole.WriteLine();
        var panel = new Panel($"[yellow]操作：[/] {EscapeMarkup(operation)}\n[yellow]路径：[/] {EscapeMarkup(path)}")
        {
            Header = new PanelHeader("[yellow]⚠️  需要审批：工作区外的文件操作[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        };
        AnsiConsole.Write(panel);

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<ApprovalOption>()
                .Title("[green]是否批准此操作？[/]")
                .AddChoices(
                    ApprovalOption.Once,
                    ApprovalOption.Session,
                    ApprovalOption.Always,
                    ApprovalOption.Reject)
                .UseConverter(option => option switch
                {
                    ApprovalOption.Once => "✅  批准（仅此一次）",
                    ApprovalOption.Session => "✅  批准（本次会话有效）",
                    ApprovalOption.Always => "✅  批准（永久）",
                    ApprovalOption.Reject => "❌  拒绝",
                    _ => option.ToString()
                })
                .PageSize(4));

        DisplayResult(choice);
        return choice;
    }

    /// <summary>
    /// 请求Shell命令审批
    /// </summary>
    /// <param name="command">Shell命令</param>
    /// <param name="workingDir">工作目录</param>
    /// <returns>审批选项</returns>
    public static ApprovalOption RequestShellApproval(string command, string? workingDir)
    {
        AnsiConsole.WriteLine();
        var message = $"[yellow]命令：[/] {EscapeMarkup(command)}";
        if (!string.IsNullOrWhiteSpace(workingDir))
        {
            message += $"\n[yellow]工作目录：[/] {EscapeMarkup(workingDir)}";
        }

        var panel = new Panel(message)
        {
            Header = new PanelHeader("[yellow]⚠️  需要审批：工作区外的 Shell 命令[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Yellow)
        };
        AnsiConsole.Write(panel);

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<ApprovalOption>()
                .Title("[green]是否批准此命令？[/]")
                .AddChoices(new[] {
                    ApprovalOption.Once,
                    ApprovalOption.Session,
                    ApprovalOption.Always,
                    ApprovalOption.Reject
                })
                .UseConverter(option => option switch
                {
                    ApprovalOption.Once => "✅ 批准（仅此一次）",
                    ApprovalOption.Session => "✅ 批准（本次会话有效）",
                    ApprovalOption.Always => "✅ 批准（永久）",
                    ApprovalOption.Reject => "❌ 拒绝",
                    _ => option.ToString()
                })
                .PageSize(4));

        DisplayResult(choice);
        return choice;
    }

    /// <summary>
    /// 显示审批结果
    /// </summary>
    private static void DisplayResult(ApprovalOption option)
    {
        switch (option)
        {
            case ApprovalOption.Once:
                AnsiConsole.MarkupLine("[green]✓ 已批准（仅此一次）[/]");
                break;
            case ApprovalOption.Session:
                AnsiConsole.MarkupLine("[green]✓ 已批准（本次会话有效）[/]");
                break;
            case ApprovalOption.Always:
                AnsiConsole.MarkupLine("[green]✓ 已批准并永久保存[/]");
                break;
            case ApprovalOption.Reject:
                AnsiConsole.MarkupLine("[red]✗ 已拒绝[/]");
                break;
        }
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// 转义 Spectre.Console 标记字符
    /// </summary>
    private static string EscapeMarkup(this string text)
    {
        return Markup.Escape(text);
    }
}
