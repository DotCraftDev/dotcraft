using System.Reflection;
using System.Text;
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
    private static string GetTemplateContent(
        string templateName,
        Language language,
        WorkspaceBootstrapProfile profile = WorkspaceBootstrapProfile.Default)
    {
        var langSuffix = language == Language.Chinese ? "zh" : "en";
        var extension = templateName == "gitignore" ? string.Empty : ".md";
        string resourceName;

        if (profile == WorkspaceBootstrapProfile.Default)
        {
            resourceName = $"DotCraft.Resources.Templates.{templateName}_{langSuffix}{extension}";
        }
        else
        {
            var profileSuffix = profile switch
            {
                WorkspaceBootstrapProfile.Developer => "developer",
                WorkspaceBootstrapProfile.PersonalAssistant => "personal_assistant",
                _ => "default"
            };
            resourceName = $"DotCraft.Resources.Templates.{templateName}_{profileSuffix}_{langSuffix}{extension}";
        }

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            throw new InvalidOperationException($"Template resource not found: {resourceName}");
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static JsonObject LoadJsonObject(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        return JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject ?? [];
    }

    private static void SaveJsonObject(string path, JsonObject node)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, node.ToJsonString(JsonOptions), Encoding.UTF8);
    }

    private static void EnsureWorkspaceStructure(string craftPath, List<(string Status, string Path)>? createdItems = null)
    {
        var directories = new[]
        {
            craftPath,
            Path.Combine(craftPath, "memory"),
            Path.Combine(craftPath, "skills"),
            Path.Combine(craftPath, "security")
        };

        foreach (var dir in directories)
        {
            Directory.CreateDirectory(dir);
            createdItems?.Add(("[green]✓[/]", dir.EscapeMarkup()));
        }
    }

    private static void WriteWorkspaceTemplates(
        string craftPath,
        Language language,
        WorkspaceBootstrapProfile profile,
        List<(string Status, string Path)>? createdItems = null)
    {
        var agentsPath = Path.Combine(craftPath, "AGENTS.md");
        File.WriteAllText(agentsPath, GetTemplateContent("AGENTS", language, profile), Encoding.UTF8);
        createdItems?.Add(("[green]✓[/]", "AGENTS.md"));

        var userPath = Path.Combine(craftPath, "USER.md");
        File.WriteAllText(userPath, GetTemplateContent("USER", language, profile), Encoding.UTF8);
        createdItems?.Add(("[green]✓[/]", "USER.md"));

        var memoryPath = Path.Combine(craftPath, "memory", "MEMORY.md");
        File.WriteAllText(memoryPath, GetTemplateContent("MEMORY", language), Encoding.UTF8);
        createdItems?.Add(("[green]✓[/]", "memory/MEMORY.md"));

        var gitignorePath = Path.Combine(craftPath, ".gitignore");
        File.WriteAllText(gitignorePath, GetTemplateContent("gitignore", language), Encoding.UTF8);
        createdItems?.Add(("[green]✓[/]", ".gitignore"));
    }

    private static void ApplyCoreConfigFields(JsonObject node, Language language, string apiKey, string endPoint, string model)
    {
        node["Language"] = language.ToString();
        node["ApiKey"] = apiKey;
        node["EndPoint"] = endPoint;
        node["Model"] = model;
    }

    private static void RemoveCoreConfigFields(JsonObject node)
    {
        node.Remove("Language");
        node.Remove("ApiKey");
        node.Remove("EndPoint");
        node.Remove("Model");
    }

    private static void ApplyWorkspaceOverridesFromGlobal(
        JsonObject workspaceNode,
        JsonObject globalNode,
        WorkspaceSetupRequest request)
    {
        static string? ReadTrimmedString(JsonObject node, string key)
        {
            return node[key]?.GetValue<string>()?.Trim();
        }

        var globalLanguage = ReadTrimmedString(globalNode, "Language");
        if (!string.Equals(globalLanguage, request.Language.ToString(), StringComparison.Ordinal))
            workspaceNode["Language"] = request.Language.ToString();
        else
            workspaceNode.Remove("Language");

        var globalEndpoint = ReadTrimmedString(globalNode, "EndPoint");
        if (!string.Equals(globalEndpoint, request.EndPoint, StringComparison.Ordinal))
            workspaceNode["EndPoint"] = request.EndPoint;
        else
            workspaceNode.Remove("EndPoint");

        var globalModel = ReadTrimmedString(globalNode, "Model");
        if (!string.Equals(globalModel, request.Model, StringComparison.Ordinal))
            workspaceNode["Model"] = request.Model;
        else
            workspaceNode.Remove("Model");

        var apiKey = request.ApiKey.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            workspaceNode.Remove("ApiKey");
            return;
        }

        var globalApiKey = ReadTrimmedString(globalNode, "ApiKey");
        if (!string.Equals(globalApiKey, apiKey, StringComparison.Ordinal))
            workspaceNode["ApiKey"] = apiKey;
        else
            workspaceNode.Remove("ApiKey");
    }

    /// <summary>
    /// 选择语言
    /// </summary>
    public static Language SelectLanguage()
    {
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
    public static bool AskYesNo(string title)
    {
        var yesOption = Strings.InitAskYes;
        var noOption = Strings.InitAskNo;

        var choice = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(title)
                .AddChoices(yesOption, noOption));

        return choice == yesOption;
    }

    /// <summary>
    /// 初始化工作区（多语言支持）
    /// </summary>
    public static int InitializeWorkspace(
        string craftPath,
        Language language = Language.Chinese,
        WorkspaceBootstrapProfile profile = WorkspaceBootstrapProfile.Default)
    {
        if (LanguageService.Current.CurrentLanguage != language)
        {
            LanguageService.Current = new LanguageService(language);
        }

        AnsiConsole.MarkupLine($"[blue]🚀 {Strings.InitInitializing}[/]");

        var createdItems = new List<(string Status, string Path)>();

        try
        {
            EnsureWorkspaceStructure(craftPath, createdItems);

            var workspaceNode = new JsonObject
            {
                ["Language"] = language.ToString()
            };

            var globalConfigPath = GetGlobalConfigPath();
            if (File.Exists(globalConfigPath))
            {
                var globalNode = LoadJsonObject(globalConfigPath);
                foreach (var prop in globalNode.ToList())
                {
                    workspaceNode.Remove(prop.Key);
                }
            }

            var configPath = Path.Combine(craftPath, "config.json");
            SaveJsonObject(configPath, workspaceNode);
            createdItems.Add(("[green]✓[/]", configPath.EscapeMarkup()));

            WriteWorkspaceTemplates(craftPath, language, profile, createdItems);

            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .AddColumn(new TableColumn(Strings.InitStatus).Centered())
                .AddColumn(new TableColumn(Strings.InitPath).LeftAligned());

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
            AnsiConsole.MarkupLine($"[red]✗ {Strings.InitFailedShort}: {ex.Message.EscapeMarkup()}[/]");
            return 1;
        }
    }

    public static int RunSetup(string craftPath, WorkspaceSetupRequest request)
    {
        return RunSetup(craftPath, request, GetGlobalConfigPath());
    }

    internal static int RunSetup(string craftPath, WorkspaceSetupRequest request, string globalConfigPath)
    {
        EnsureWorkspaceStructure(craftPath);

        if (request.SaveToUserConfig && request.PreferExistingUserConfig)
            throw new InvalidOperationException("SaveToUserConfig and PreferExistingUserConfig cannot both be enabled.");

        if (request.SaveToUserConfig)
        {
            var globalNode = LoadJsonObject(globalConfigPath);
            ApplyCoreConfigFields(globalNode, request.Language, request.ApiKey, request.EndPoint, request.Model);
            SaveJsonObject(globalConfigPath, globalNode);
        }

        var workspaceConfigPath = Path.Combine(craftPath, "config.json");
        var workspaceNode = LoadJsonObject(workspaceConfigPath);
        if (request.SaveToUserConfig)
        {
            RemoveCoreConfigFields(workspaceNode);
        }
        else if (request.PreferExistingUserConfig)
        {
            if (!File.Exists(globalConfigPath))
                throw new InvalidOperationException("User config was not found.");

            var globalNode = LoadJsonObject(globalConfigPath);
            ApplyWorkspaceOverridesFromGlobal(workspaceNode, globalNode, request);
        }
        else
        {
            ApplyCoreConfigFields(workspaceNode, request.Language, request.ApiKey, request.EndPoint, request.Model);
        }

        SaveJsonObject(workspaceConfigPath, workspaceNode);
        WriteWorkspaceTemplates(craftPath, request.Language, request.Profile);
        return 0;
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
    public static string? PromptApiKey()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[cyan]{Strings.InitEnterApiKey}[/]");
        var apiKey = Console.ReadLine();
        return string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    /// <summary>
    /// 创建全局配置文件
    /// </summary>
    public static void CreateGlobalConfig(string configPath, string apiKey, Language language)
    {
        var configNode = new JsonObject();
        ApplyCoreConfigFields(configNode, language, apiKey, "https://api.openai.com/v1", "gpt-4o-mini");
        SaveJsonObject(configPath, configNode);
    }
}
