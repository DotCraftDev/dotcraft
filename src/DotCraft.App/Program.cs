using System.Text;
using DotCraft.Acp;
using DotCraft.AppServer;
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

var isAcpMode = args.Any(a => a.Equals("-acp", StringComparison.OrdinalIgnoreCase)
                            || a.Equals("acp", StringComparison.OrdinalIgnoreCase));

var isAppServerMode = args.Any(a => a.Equals("app-server", StringComparison.OrdinalIgnoreCase));

// Both ACP and AppServer reserve stdout for JSON-RPC; redirect all diagnostics to stderr
if (isAcpMode || isAppServerMode)
{
    // stdout is reserved for JSON-RPC; redirect all console diagnostics to stderr immediately
    // so nothing pollutes the transport before the host starts.
    AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(Console.Error)
    });
    Console.SetOut(new StreamWriter(Console.OpenStandardError(), Encoding.UTF8) { AutoFlush = true });

    // Ignore Ctrl+C / SIGINT in the subprocess.
    //
    // On Windows, pressing Ctrl+C sends CTRL_C_EVENT to every process attached to the same
    // console.  The CLI process handles this via Console.CancelKeyPress (setting e.Cancel = true),
    // which prevents the CLI from terminating — but that does NOT protect child processes in
    // the same console process group.  Since .NET's Process.Start lacks a way to specify
    // CREATE_NEW_PROCESS_GROUP, we instead disable the CTRL_C_EVENT handler on the subprocess
    // side.  The AppServer's lifecycle is controlled by stdin EOF / explicit shutdown, so it
    // never needs to respond to Ctrl+C directly.
    //
    // On Unix, Process.Start does not propagate SIGINT to children (the shell does), and this
    // handler provides consistent ignore semantics across platforms.
    ConsoleSignalGuard.IgnoreInterruptSignal();
}

var workspacePath = Directory.GetCurrentDirectory();
var botPath = Path.GetFullPath(".craft");
var workspaceJustInitialized = false;

if (!Directory.Exists(botPath))
{
    if (isAcpMode || isAppServerMode)
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

var configPath = Path.Combine(botPath, "config.json");
var config = AppConfig.LoadWithGlobalFallback(configPath);

if (isAcpMode)
{
    config.SetSection("Acp", new AcpConfig { Enabled = true });
    // Dashboard is not useful when the process is managed by an external client.
    config.DashBoard.Enabled = false;
}

if (isAppServerMode)
{
    config.SetSection("AppServer", new AppServerConfig { Mode = AppServerMode.Stdio });
    // Dashboard is not useful when the process is managed by an external client.
    config.DashBoard.Enabled = false;
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
    if (isAcpMode || isAppServerMode)
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
