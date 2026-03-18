using System.Text;
using DotCraft.CLI;
using DotCraft.Diagnostics;
using DotCraft.Configuration;
using DotCraft.Hosting;
using DotCraft.Localization;
using DotCraft.Modules;
using DotCraft.Setup;

using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

Console.OutputEncoding = Encoding.UTF8;

// -------------------------------------------------------------------------
// 1. Parse command-line arguments
// -------------------------------------------------------------------------
var cliArgs = CommandLineArgs.Parse(args);
var isHeadless = cliArgs.Mode is CommandLineArgs.RunMode.Acp or CommandLineArgs.RunMode.AppServer
              || (cliArgs.Mode == CommandLineArgs.RunMode.Cli && !string.IsNullOrWhiteSpace(cliArgs.RemoteUrl));

// -------------------------------------------------------------------------
// 2. Prepare subprocess environment (stdout → stderr, ignore Ctrl+C)
//    Only needed when the process reserves stdout for a wire protocol.
// -------------------------------------------------------------------------
if (cliArgs.ReservesStdout)
{
    SubprocessEnvironment.Prepare();
}

// -------------------------------------------------------------------------
// 3. Workspace discovery & initialization
// -------------------------------------------------------------------------
var workspacePath = Directory.GetCurrentDirectory();
var botPath = Path.GetFullPath(".craft");
var workspaceJustInitialized = false;

if (!Directory.Exists(botPath))
{
    if (isHeadless)
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
    var trustPanel = new Panel(
        new Markup(
            $"[cyan]{lang.GetString("当前工作区路径 / Current workspace path:", "Current workspace path:")}[/]\n" +
            $"  [white]{Markup.Escape(workspacePath)}[/]\n\n" +
            lang.GetString(
                "DotCraft 将在此目录创建工作区（.craft 文件夹），用于存储会话、记忆和配置。",
                "DotCraft will create a workspace (.craft folder) in this directory to store sessions, memory, and configuration.")))
    {
        Header = new PanelHeader($"[cyan]🔐 {lang.GetString("信任文件夹确认", "Trust Folder Confirmation")}[/]"),
        Border = BoxBorder.Rounded,
        BorderStyle = new Style(Color.Cyan),
        Padding = new Padding(1, 0, 1, 0)
    };
    AnsiConsole.Write(trustPanel);
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
    workspaceJustInitialized = true;
}

// -------------------------------------------------------------------------
// 4. Load configuration & apply CLI overrides
// -------------------------------------------------------------------------
var configPath = Path.Combine(botPath, "config.json");
var config = AppConfig.LoadWithGlobalFallback(configPath);

// CLI arguments take precedence over config.json values.
cliArgs.ApplyTo(config);

// -------------------------------------------------------------------------
// 5. Language & debug mode
// -------------------------------------------------------------------------
var languageService = new LanguageService(config.Language);

DebugModeService.Initialize(config.DebugMode);
if (config.DebugMode)
{
    AnsiConsole.MarkupLine("[yellow]Debug mode is enabled - tool arguments and results will be shown in full[/]");
}

// -------------------------------------------------------------------------
// 6. API Key validation
// -------------------------------------------------------------------------
if (string.IsNullOrWhiteSpace(config.ApiKey))
{
    if (isHeadless)
    {
        await Console.Error.WriteLineAsync("API Key not configured. Please set ApiKey in config.json.");
        Environment.Exit(1);
        return;
    }

    AnsiConsole.WriteLine();
    if (workspaceJustInitialized)
    {
        AnsiConsole.MarkupLine($"[green]✓ {languageService.GetString("工作区初始化完成。", "Workspace initialized.")}[/]");
    }
    AnsiConsole.MarkupLine($"[yellow]⚠️  {languageService.GetString("API Key 未配置，正在进入初始化配置模式。", "API Key not configured. Entering setup mode.")}[/]");
    AnsiConsole.WriteLine();
    var setupHost = new SetupHost(config, new DotCraftPaths
    {
        WorkspacePath = workspacePath,
        CraftPath = botPath
    }, languageService);
    await setupHost.RunAsync();
    return;
}

// -------------------------------------------------------------------------
// 7. Module registry, DI, and host startup
// -------------------------------------------------------------------------
var paths = new DotCraftPaths
{
    WorkspacePath = workspacePath,
    CraftPath = botPath
};

var moduleRegistry = new ModuleRegistry();
ModuleRegistrations.RegisterAll(moduleRegistry);
var hostBuilder = new HostBuilder(moduleRegistry, config, paths);

var services = new ServiceCollection()
    .AddSingleton(moduleRegistry)
    .AddDotCraft(config, workspacePath, botPath);

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
