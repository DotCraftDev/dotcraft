using DotCraft.Common;
using DotCraft.Localization;
using DotCraft.Memory;
using DotCraft.Mcp;
using DotCraft.Protocol;
using Spectre.Console;
using static DotCraft.Skills.SkillsLoader;

namespace DotCraft.CLI;

public static class StatusPanel
{
    public static void ShowWelcome(
        string? currentSessionId = null,
        string? dashBoardUrl = null,
        CliBackendInfo? backendInfo = null)
    {
        AnsiConsole.Clear();

        AnsiConsole.Write(
            new FigletText("DotCraft")
                .LeftJustified()
                .Color(Color.Blue));

        AnsiConsole.Write(new Text($"Version {AppVersion.Short}", new Style(Color.Grey)).LeftJustified());
        AnsiConsole.WriteLine();

        RenderBackendStatus(backendInfo);

        if (!string.IsNullOrEmpty(currentSessionId))
        {
            AnsiConsole.MarkupLine($"[grey]{Strings.CurrentSession}：[cyan]{currentSessionId.Escape()}[/][/]");
        }
        if (!string.IsNullOrEmpty(dashBoardUrl))
        {
            var escapedUrl = dashBoardUrl.Escape();
            AnsiConsole.MarkupLine($"[blue]●[/] [bold]Dashboard[/]  [grey][link={escapedUrl}]{escapedUrl}[/][/]");
        }
        AnsiConsole.WriteLine();

        // Quick command tips
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow(
            new Markup("[blue]/exit[/]"),
            new Markup($"[grey]{Strings.CmdExit}[/]"),
            new Markup("[blue]/help[/]"),
            new Markup($"[grey]{Strings.CmdHelp}[/]"),
            new Markup("[blue]/new[/]"),
            new Markup($"[grey]{Strings.CmdNew}[/]"));
        grid.AddRow(
            new Markup("[blue]/load[/]"),
            new Markup($"[grey]{Strings.CmdLoad}[/]"),
            new Markup("[blue]/agent[/]"),
            new Markup($"[grey]{Strings.CmdAgent}[/]"),
            new Markup("[blue]/plan[/]"),
            new Markup($"[grey]{Strings.CmdPlan}[/]"));

        var panel = new Panel(grid)
        {
            Header = new PanelHeader($"[yellow]💡 {Strings.QuickCommands}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Grey)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }
    
    public static void ShowSkillsTable(List<SkillInfo> skills, string? workspaceSkillsPath = null, string? userSkillsPath = null)
    {
        if (skills.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]📝 {Strings.NoSkills}[/]");
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);

        table.AddColumn(new TableColumn($"[u]{Strings.Skill}[/]").Width(20));
        table.AddColumn(new TableColumn($"[u]{Strings.Status}[/]").Width(12));
        table.AddColumn(new TableColumn($"[u]{Strings.Source}[/]").Width(12));
        table.AddColumn(new TableColumn($"[u]{Strings.Description}[/]"));

        foreach (var skill in skills)
        {
            var status = skill.Available
                ? $"[green]✓ {Strings.Available}[/]"
                : $"[red]✗ {skill.UnavailableReason?.Escape() ?? Strings.Unavailable}[/]";

            var (sourceColor, sourceLabel) = skill.Source switch
            {
                "workspace" => ("blue", "workspace"),
                "builtin" => ("yellow", "builtin"),
                _ => ("grey", skill.Source)
            };
            var source = $"[{sourceColor}]{sourceLabel}[/]";

            var description = GetSkillDescription(skill).Escape();

            table.AddRow(
                $"[white]{skill.Name.Escape()}[/]",
                status,
                source,
                $"[grey]{description}[/]");
        }

        var panel = new Panel(table)
        {
            Header = new PanelHeader($"[blue]📚 {Strings.AvailableSkills}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Blue)
        };

        AnsiConsole.Write(panel);

        // Show paths
        if (!string.IsNullOrEmpty(workspaceSkillsPath) || !string.IsNullOrEmpty(userSkillsPath))
        {
            AnsiConsole.WriteLine();
            var pathTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .Expand();

            pathTable.AddColumn(new TableColumn("[u]类型[/]").Width(15));
            pathTable.AddColumn(new TableColumn("[u]路径[/]"));

            if (!string.IsNullOrEmpty(workspaceSkillsPath))
            {
                pathTable.AddRow(
                    "[blue]Workspace[/]",
                    $"[grey]{workspaceSkillsPath.Escape()}[/]");
            }

            if (!string.IsNullOrEmpty(userSkillsPath))
            {
                pathTable.AddRow(
                    "[grey]User[/]",
                    $"[grey]{userSkillsPath.Escape()}[/]");
            }

            var pathPanel = new Panel(pathTable)
            {
                Header = new PanelHeader($"[yellow]📁 {Strings.SkillsPath}[/]"),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(Color.Yellow)
            };

            AnsiConsole.Write(pathPanel);
        }
    }
    
    /// <summary>
    /// Displays a table of Session Protocol threads.
    /// </summary>
    public static void ShowThreadsTable(IReadOnlyList<ThreadSummary> threads)
    {
        if (threads.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]💬 {Strings.NoSessions}[/]");
            return;
        }

        const int SummaryMaxLength = 50;

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);

        table.AddColumn(new TableColumn($"[u]{Strings.Session}[/]").Width(30));
        table.AddColumn(new TableColumn($"[u]{Strings.CreatedAt}[/]").Width(20));
        table.AddColumn(new TableColumn($"[u]{Strings.UpdatedAt}[/]").Width(20));
        table.AddColumn(new TableColumn($"[u]{Strings.Summary}[/]").Width(50));

        foreach (var thread in threads)
        {
            var id = thread.Id.Escape();
            var created = thread.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            var updated = thread.LastActiveAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            string summary;
            if (!string.IsNullOrWhiteSpace(thread.DisplayName))
            {
                var msg = thread.DisplayName.ReplaceLineEndings(" ").Trim();
                if (msg.Length > SummaryMaxLength)
                    msg = msg[..SummaryMaxLength] + "...";
                summary = "[dim]" + msg.Escape() + "[/]";
            }
            else
            {
                summary = "[dim]-[/]";
            }

            table.AddRow(
                $"[white]{id}[/]",
                $"[grey]{created}[/]",
                $"[grey]{updated}[/]",
                summary);
        }

        var panel = new Panel(table)
        {
            Header = new PanelHeader($"[green]💬 {Strings.SavedSessions}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Green)
        };

        AnsiConsole.Write(panel);
    }

    public static void ShowHelp()
    {
        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();

        grid.AddRow(new Markup($"[yellow]{Strings.Commands}:[/]"), new Markup(""));
        grid.AddRow("  /exit", Strings.CmdExit);
        grid.AddRow("  /help", Strings.CmdHelp);
        grid.AddRow("  /clear", Strings.CmdClear);
        grid.AddRow("  /new", Strings.CmdNew);
        grid.AddRow("  /load", Strings.CmdLoad);
        grid.AddRow("  /delete", Strings.CmdDelete);
        grid.AddRow("  /init", Strings.CmdInit);
        grid.AddRow("  /debug", Strings.CmdDebug);
        grid.AddRow("  /skills", Strings.CmdSkills);
        grid.AddRow("  /mcp", Strings.CmdMcp);
        grid.AddRow("  /sessions", Strings.CmdSessions);
        grid.AddRow("  /memory", Strings.CmdMemory);
        grid.AddRow("  /lang", Strings.CmdLang);
        grid.AddRow("  /agent", Strings.CmdAgent);
        grid.AddRow("  /plan", Strings.CmdPlan);
        grid.AddRow("  /heartbeat trigger", Strings.CmdHeartbeat);
        grid.AddRow("  /cron list", Strings.CmdCronList);
        grid.AddRow("  /cron remove <id>", Strings.CmdCronRemove);
        grid.AddRow("  /cron enable|disable <id>", Strings.CmdCronToggle);
        grid.AddRow("  /commands", Strings.CmdCommands);
        grid.AddRow("", "");
        grid.AddRow(new Markup($"[yellow]{Strings.UsageTips}:[/]"), new Markup(""));
        grid.AddRow($"  • {Strings.TipDirectInput}", "");
        grid.AddRow($"  • {Strings.TipArrowKeys}", "");
        grid.AddRow($"  • {Strings.TipTabComplete}", "");
        grid.AddRow($"  • {Strings.TipShiftTabMode}", "");
        grid.AddRow($"  • {Strings.TipAutoSave}", "");

        var panel = new Panel(grid)
        {
            Header = new PanelHeader($"[blue]❓ {Strings.CmdHelp}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Blue)
        };

        AnsiConsole.Write(panel);
    }

    public static void ShowMcpServersTable(McpClientManager? mcpManager)
    {
        if (mcpManager == null || mcpManager.Tools.Count == 0)
        {
            AnsiConsole.MarkupLine($"[grey]{Strings.NoMcpServers}[/]");
            AnsiConsole.MarkupLine($"[grey]{Strings.McpConfigTip}[/]");
            AnsiConsole.WriteLine();
            return;
        }

        var serverTools = new Dictionary<string, List<string>>();
        foreach (var tool in mcpManager.Tools)
        {
            var serverName = mcpManager.ToolServerMap.GetValueOrDefault(tool.Name, Strings.Unknown);
            if (!serverTools.TryGetValue(serverName, out var list))
            {
                list = [];
                serverTools[serverName] = list;
            }
            list.Add(tool.Name);
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);

        table.AddColumn(new TableColumn($"[u]{Strings.Server}[/]").Width(20));
        table.AddColumn(new TableColumn($"[u]{Strings.Tools}[/]").Width(10));
        table.AddColumn(new TableColumn($"[u]{Strings.ToolNames}[/]"));

        foreach (var (server, tools) in serverTools)
        {
            table.AddRow(
                $"[white]{server.Escape()}[/]",
                $"[cyan]{tools.Count}[/]",
                $"[grey]{string.Join(", ", tools.Select(t => t.Escape()))}[/]");
        }

        var panel = new Panel(table)
        {
            Header = new PanelHeader($"[blue]{Strings.McpServices}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Blue)
        };

        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private static string GetSkillDescription(SkillInfo skill)
    {
        try
        {
            if (File.Exists(skill.Path))
            {
                var content = File.ReadAllText(skill.Path);
                var lines = content.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("description:"))
                    {
                        return line["description:".Length..].Trim().Trim('"', '\'');
                    }
                }
            }
        }
        catch
        {
            // ignored
        }

        return Strings.NoDescription;
    }

    public static void ShowPlanStatus(StructuredPlan plan)
    {
        AnsiConsole.WriteLine();

        var titlePanel = new Panel(
            plan.Overview.Length > 0
                ? $"[white]{plan.Title.Escape()}[/]\n[grey]{plan.Overview.Escape()}[/]"
                : $"[white]{plan.Title.Escape()}[/]")
        {
            Border = BoxBorder.None,
            Padding = new Padding(1, 0)
        };
        AnsiConsole.Write(titlePanel);

        if (plan.Todos.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]  (no tasks)[/]");
            AnsiConsole.WriteLine();
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Grey);

        table.AddColumn(new TableColumn("[grey]Status[/]").Width(12));
        table.AddColumn(new TableColumn("[grey]ID[/]").Width(20));
        table.AddColumn(new TableColumn("[grey]Task[/]"));

        foreach (var todo in plan.Todos)
        {
            var (icon, label, color) = todo.Status switch
            {
                PlanTodoStatus.Completed  => ("✓", "done",    "green"),
                PlanTodoStatus.InProgress => ("●", "working", "yellow"),
                PlanTodoStatus.Cancelled  => ("✗", "skipped", "red"),
                _                         => ("○", "pending", "grey")
            };

            table.AddRow(
                $"[{color}]{icon} {label}[/]",
                $"[grey]{todo.Id.Escape()}[/]",
                todo.Content.Escape());
        }

        AnsiConsole.Write(table);

        var completed = plan.Todos.Count(t => t.Status == PlanTodoStatus.Completed);
        var cancelled = plan.Todos.Count(t => t.Status == PlanTodoStatus.Cancelled);
        var total = plan.Todos.Count;
        var active = total - cancelled;
        var progressColor = completed == active && active > 0 ? "green" : "grey";
        AnsiConsole.MarkupLine($"  [{progressColor}]{completed}/{active} completed[/]");
        AnsiConsole.WriteLine();
    }

    private static void RenderBackendStatus(CliBackendInfo? backendInfo)
    {
        if (backendInfo == null) return;

        var shortVer = backendInfo.ServerVersion?.Split('+')[0];
        var ver = shortVer != null ? $"server v{shortVer}" : null;

        string? location = backendInfo.ServerUrl is not null
            ? backendInfo.ServerUrl.Escape()
            : backendInfo.ProcessId.HasValue ? $"PID {backendInfo.ProcessId}" : null;

        var detail = string.Join(" · ", new[] { location, ver }.Where(s => s != null));
        var detailPart = detail.Length > 0 ? $"  [grey]{detail.Escape()}[/]" : string.Empty;
        AnsiConsole.MarkupLine($"[green]●[/] [bold]AppServer[/]{detailPart}");
    }

    private static string Escape(this string text)
    {
        return Markup.Escape(text);
    }
}
