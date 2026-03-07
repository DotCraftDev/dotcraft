using System.Text;
using DotCraft.CLI;
using DotCraft.Diagnostics;
using DotCraft.Configuration;
using DotCraft.Hosting;
using DotCraft.Localization;
using DotCraft.Modules;

using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;

var isAcpMode = args.Any(a => a.Equals("-acp", StringComparison.OrdinalIgnoreCase)
                            || a.Equals("acp", StringComparison.OrdinalIgnoreCase));

if (isAcpMode)
{
    // stdout is reserved for JSON-RPC; redirect all console diagnostics to stderr immediately
    // so nothing pollutes the transport before AcpHost starts.
    AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(Console.Error)
    });
    Console.SetOut(new StreamWriter(Console.OpenStandardError(), Encoding.UTF8) { AutoFlush = true });
}

var workspacePath = Directory.GetCurrentDirectory();
var botPath = Path.GetFullPath(".craft");

if (!Directory.Exists(botPath))
{
    if (isAcpMode)
    {
        await Console.Error.WriteLineAsync($"DotCraft workspace not found: {botPath}");
        Environment.Exit(1);
        return;
    }

    // First, select language
    var selectedLanguage = InitHelper.SelectLanguage();
    var lang = new LanguageService(selectedLanguage);

    // Trust folder confirmation
    Console.WriteLine();
    AnsiConsole.MarkupLine($"[cyan]{lang.GetString("当前工作区路径 / Current workspace path:", "Current workspace path:")}[/]");
    AnsiConsole.MarkupLine($"  [white]{Markup.Escape(workspacePath)}[/]");
    Console.WriteLine();
    AnsiConsole.WriteLine(lang.GetString(
        "DotCraft 将在此目录创建工作区（.craft 文件夹），用于存储会话、记忆和配置。",
        "DotCraft will create a workspace (.craft folder) in this directory to store sessions, memory, and configuration."));
    Console.WriteLine();

    if (!InitHelper.AskYesNo(
        lang.GetString("你是否信任此文件夹？",
                      "Do you trust this folder?"), lang))
    {
        AnsiConsole.MarkupLine($"\n[grey]{lang.GetString("已取消。请切换到受信任的目录后重试。", "Cancelled. Please switch to a trusted directory and try again.")}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]{lang.GetString("按任意键退出...", "Press any key to exit...")}[/]");
        Console.ReadKey(true);
        Environment.Exit(0);
        return;
    }

    // Initialize workspace
    AnsiConsole.WriteLine();
    var initResult = InitHelper.InitializeWorkspace(botPath, selectedLanguage);
    if (initResult != 0)
    {
        AnsiConsole.MarkupLine($"\n[red]{lang.GetString("初始化失败。", "Initialization failed.")}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]{lang.GetString("按任意键退出...", "Press any key to exit...")}[/]");
        Console.ReadKey(true);
        Environment.Exit(1);
        return;
    }

    // Check global config and guide user to configure API Key
    var globalConfigPath = InitHelper.GetGlobalConfigPath();
    if (!File.Exists(globalConfigPath))
    {
        Console.WriteLine();
        AnsiConsole.MarkupLine($"[yellow]💡 {lang.GetString("提示：建议配置全局 API Key", "Tip: Configure global API Key")}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine(lang.GetString(
            "你可以在全局配置文件中设置 API Key，这样所有工作区都可以共享使用：",
            "You can set API Key in global config file, which will be shared across all workspaces:"));
        AnsiConsole.WriteLine($"  {Markup.Escape(globalConfigPath)}");
        Console.WriteLine();
        AnsiConsole.WriteLine(lang.GetString("配置示例 / Configuration example:", "Configuration example:"));
        AnsiConsole.WriteLine("""
        {
          "ApiKey": "your-api-key-here",
          "Model": "gpt-4o-mini",
          "EndPoint": "https://api.openai.com/v1"
        }
        """);
        Console.WriteLine();

        if (InitHelper.AskYesNo(
            lang.GetString("是否现在创建全局配置？",
                          "Create global config now?"), lang))
        {
            var apiKey = InitHelper.PromptApiKey(lang);
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                InitHelper.CreateGlobalConfig(globalConfigPath, apiKey, selectedLanguage);
                AnsiConsole.MarkupLine($"[green]✓ {lang.GetString("全局配置已创建", "Global config created")}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]{lang.GetString("已跳过全局配置创建", "Skipped global config creation")}[/]");
            }
        }
    }

    Console.WriteLine();
    AnsiConsole.MarkupLine($"[green]✓ {lang.GetString("工作区初始化完成！请重新运行 DotCraft 开始使用。", "Workspace initialized! Please run DotCraft again to start.")}[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[grey]{lang.GetString("按任意键退出...", "Press any key to exit...")}[/]");
    Console.ReadKey(true);
    Environment.Exit(0);
    return;
}

var configPath = Path.Combine(botPath, "config.json");
var config = AppConfig.LoadWithGlobalFallback(configPath);

if (isAcpMode)
{
    config.Acp.Enabled = true;
}

// Create language service from config
var languageService = new LanguageService(config.Language);

DebugModeService.Initialize(config.DebugMode);
if (config.DebugMode)
{
    AnsiConsole.MarkupLine("[yellow]Debug mode is enabled - tool arguments and results will be shown in full[/]");
}

if (string.IsNullOrWhiteSpace(config.ApiKey))
{
    if (isAcpMode)
    {
        await Console.Error.WriteLineAsync("API Key not configured. Please set ApiKey in config.json.");
        Environment.Exit(1);
        return;
    }

    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[yellow]⚠️  {languageService.GetString("API Key 未配置", "API Key not configured")}[/]");
    AnsiConsole.WriteLine();
    AnsiConsole.WriteLine(languageService.GetString(
        "请在配置文件中设置 API Key 后再运行：",
        "Please set API Key in configuration file before running:"));
    AnsiConsole.WriteLine($"  {configPath}");
    AnsiConsole.WriteLine();
    AnsiConsole.WriteLine(languageService.GetString("配置示例：", "Configuration example:"));
    AnsiConsole.WriteLine("  \"ApiKey\": \"your-api-key-here\"");
    AnsiConsole.WriteLine("  \"Model\": \"gpt-4o-mini\"");
    AnsiConsole.WriteLine("  \"EndPoint\": \"https://api.openai.com/v1\"");
    AnsiConsole.WriteLine();
    AnsiConsole.MarkupLine($"[grey]{languageService.GetString("按任意键退出...", "Press any key to exit...")}[/]");
    Console.ReadKey(true);
    Environment.Exit(1);
    return;
}

// Create module registry and startup orchestrator
var paths = new DotCraftPaths
{
    WorkspacePath = workspacePath,
    CraftPath = botPath
};

var moduleRegistry = new ModuleRegistry();
ModuleRegistrations.RegisterAll(moduleRegistry);
var hostBuilder = new HostBuilder(moduleRegistry, config, paths);

// Create service collection with core services
var services = new ServiceCollection()
    .AddSingleton(moduleRegistry)
    .AddDotCraft(config, workspacePath, botPath);

// Create host
var (provider, host) = hostBuilder.Build(services);

await provider.InitializeServicesAsync();

try
{
    await using (host)
    {
        await host.RunAsync();
    }
}
finally
{
    await provider.DisposeServicesAsync();
}
