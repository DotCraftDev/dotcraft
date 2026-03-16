using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DotCraft.Localization;
using Spectre.Console;

namespace DotCraft.CLI;

/// <summary>
/// 初始化辅助工具类
/// </summary>
public static class InitHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// 从嵌入资源读取模板内容
    /// </summary>
    /// <param name="templateName">模板名称（如 AGENTS, USER, MEMORY, gitignore）</param>
    /// <param name="language">语言</param>
    /// <returns>模板内容</returns>
    private static string GetTemplateContent(string templateName, Language language)
    {
        var langSuffix = language == Language.Chinese ? "zh" : "en";
        var extension = templateName == "gitignore" ? "" : ".md";
        var resourceName = $"DotCraft.Resources.Templates.{templateName}_{langSuffix}{extension}";

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            throw new InvalidOperationException($"Template resource not found: {resourceName}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 选择语言
    /// </summary>
    public static Language SelectLanguage()
    {
        // 显示欢迎面板
        Console.WriteLine();
        var welcomePanel = new Panel(
            new Markup(
                "[cyan]Welcome to DotCraft![/]\n\n" +
                "[grey]请选择语言 / Please select language:[/]"))
        {
            Header = new PanelHeader("[cyan]🌐 Language Selection[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Cyan),
            Padding = new Padding(1, 0, 1, 0)
        };
        AnsiConsole.Write(welcomePanel);
        Console.WriteLine();

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .AddChoices("中文 (Chinese)", "English"));

        return choice == "中文 (Chinese)" ? Language.Chinese : Language.English;
    }

    /// <summary>
    /// 询问用户是否确认，使用 Spectre.Console 选项（多语言支持）
    /// </summary>
    public static bool AskYesNo(string title, LanguageService? lang = null)
    {
        lang ??= new LanguageService();
        var yesOption = lang.GetString("是 (Yes)", "Yes");
        var noOption = lang.GetString("否 (No)", "No");
        
        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(title)
                .AddChoices(yesOption, noOption));

        return choice == yesOption;
    }

    /// <summary>
    /// 初始化工作区（多语言支持）
    /// </summary>
    public static int InitializeWorkspace(string workspacePath, Language language = Language.Chinese)
    {
        var lang = new LanguageService(language);
        
        AnsiConsole.MarkupLine($"[blue]🚀 {lang.GetString("开始初始化 DotCraft 工作区...", "Initializing DotCraft workspace...")}[/]");

        // 收集创建的文件用于表格显示
        var createdItems = new List<(string Status, string Path)>();

        try
        {
            // 创建工作区目录结构
            var directories = new[]
            {
                workspacePath,
                Path.Combine(workspacePath, "memory"),
                Path.Combine(workspacePath, "skills"),
                Path.Combine(workspacePath, "security")
            };

            foreach (var dir in directories)
            {
                Directory.CreateDirectory(dir);
                createdItems.Add(("[green]✓[/]", dir.EscapeMarkup()));
            }

            // 创建配置文件（只保存用户设置的字段，避免覆盖全局配置）
            var workspaceNode = new JsonObject
            {
                ["Language"] = language.ToString()
            };

            // 过滤：移除全局配置中已有的字段（避免覆盖）
            var globalConfigPath = GetGlobalConfigPath();
            if (File.Exists(globalConfigPath))
            {
                if (JsonNode.Parse(File.ReadAllText(globalConfigPath)) is JsonObject globalNode)
                {
                    foreach (var prop in globalNode.ToList())
                    {
                        workspaceNode.Remove(prop.Key);
                    }
                }
            }

            var json = workspaceNode.ToJsonString(JsonOptions);
            string configPath = Path.Combine(workspacePath, "config.json");
            File.WriteAllText(configPath, json, System.Text.Encoding.UTF8);
            createdItems.Add(("[green]✓[/]", configPath.EscapeMarkup()));

            // 创建模板文件
            var agentsContent = GetTemplateContent("AGENTS", language);
            var agentsPath = Path.Combine(workspacePath, "AGENTS.md");
            File.WriteAllText(agentsPath, agentsContent, System.Text.Encoding.UTF8);
            createdItems.Add(("[green]✓[/]", "AGENTS.md"));

            var userContent = GetTemplateContent("USER", language);
            var userPath = Path.Combine(workspacePath, "USER.md");
            File.WriteAllText(userPath, userContent, System.Text.Encoding.UTF8);
            createdItems.Add(("[green]✓[/]", "USER.md"));

            var memoryContent = GetTemplateContent("MEMORY", language);
            var memoryDir = Path.Combine(workspacePath, "memory");
            var memoryPath = Path.Combine(memoryDir, "MEMORY.md");
            File.WriteAllText(memoryPath, memoryContent, System.Text.Encoding.UTF8);
            createdItems.Add(("[green]✓[/]", "memory/MEMORY.md"));

            var gitignoreContent = GetTemplateContent("gitignore", language);
            var gitignorePath = Path.Combine(workspacePath, ".gitignore");
            File.WriteAllText(gitignorePath, gitignoreContent, System.Text.Encoding.UTF8);
            createdItems.Add(("[green]✓[/]", ".gitignore"));

            // 使用表格显示创建的文件
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn(lang.GetString("状态", "Status")).Centered())
                .AddColumn(new TableColumn(lang.GetString("路径", "Path")).LeftAligned());

            foreach (var item in createdItems)
            {
                table.AddRow(item.Status, item.Path);
            }

            AnsiConsole.Write(table);

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red]✗ {lang.GetString("初始化失败", "Initialization failed")}: {ex.Message.EscapeMarkup()}[/]");
            return 1;
        }
    }

    /// <summary>
    /// 获取全局配置文件路径
    /// </summary>
    public static string GetGlobalConfigPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".craft", "config.json");
    }

    /// <summary>
    /// 提示用户输入 API Key
    /// </summary>
    public static string? PromptApiKey(LanguageService lang)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[cyan]{lang.GetString("请输入 API Key（留空跳过）：", "Enter API Key (leave empty to skip):")}[/]");
        var apiKey = Console.ReadLine();
        return string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    /// <summary>
    /// 创建全局配置文件
    /// </summary>
    public static void CreateGlobalConfig(string configPath, string apiKey, Language language)
    {
        var configDir = Path.GetDirectoryName(configPath)!;
        Directory.CreateDirectory(configDir);

        var configNode = new JsonObject
        {
            ["ApiKey"] = apiKey,
            ["Model"] = "gpt-4o-mini",
            ["EndPoint"] = "https://api.openai.com/v1",
            ["Language"] = language.ToString()
        };

        var json = configNode.ToJsonString(JsonOptions);
        File.WriteAllText(configPath, json, System.Text.Encoding.UTF8);
    }
}
