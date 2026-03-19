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
var isRemoteCli = cliArgs.Mode == CommandLineArgs.RunMode.Cli
               && !string.IsNullOrWhiteSpace(cliArgs.RemoteUrl);
var isHeadless = cliArgs.Mode is CommandLineArgs.RunMode.Acp or CommandLineArgs.RunMode.AppServer
              || isRemoteCli;

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
    LanguageService.Current = lang;

    // Trust folder confirmation
    Console.WriteLine();
    var trustPanel = new Panel(
        new Markup(
            $"[cyan]{Strings.InitTrustFolderWorkspacePath}[/]\n" +
            $"  [white]{Markup.Escape(workspacePath)}[/]\n\n" +
            Strings.InitTrustFolderDescription))
    {
        Header = new PanelHeader($"[cyan]🔐 {Strings.InitTrustFolderTitle}[/]"),
        Border = BoxBorder.Rounded,
        BorderStyle = new Style(Color.Cyan),
        Padding = new Padding(1, 0, 1, 0)
    };
    AnsiConsole.Write(trustPanel);
    Console.WriteLine();

    if (!InitHelper.AskYesNo(Strings.InitTrustFolderQuestion))
    {
        AnsiConsole.MarkupLine($"\n[grey]{Strings.InitTrustFolderCancelled}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]{Strings.InitPressAnyKey}[/]");
        Console.ReadKey(true);
        Environment.Exit(0);
        return;
    }

    // Initialize workspace
    AnsiConsole.WriteLine();
    var initResult = InitHelper.InitializeWorkspace(botPath, selectedLanguage);
    if (initResult != 0)
    {
        AnsiConsole.MarkupLine($"\n[red]{Strings.InitFailedShort}[/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]{Strings.InitPressAnyKey}[/]");
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
// Ensure LanguageService.Current is set for the main flow
// (may already be set during first-run setup above)
if (LanguageService.Current.CurrentLanguage != config.Language)
{
    LanguageService.Current = new LanguageService(config.Language);
}

DebugModeService.Initialize(config.DebugMode);
if (config.DebugMode)
{
    AnsiConsole.MarkupLine("[yellow]Debug mode is enabled - tool arguments and results will be shown in full[/]");
}

// -------------------------------------------------------------------------
// 6. API Key validation
// -------------------------------------------------------------------------
if (!isRemoteCli && string.IsNullOrWhiteSpace(config.ApiKey))
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
        AnsiConsole.MarkupLine($"[green]✓ {Strings.InitWorkspaceInitialized}[/]");
    }
    AnsiConsole.MarkupLine($"[yellow]⚠️  {Strings.InitApiKeyNotConfigured}[/]");
    AnsiConsole.WriteLine();
    var setupHost = new SetupHost(config, new DotCraftPaths
    {
        WorkspacePath = workspacePath,
        CraftPath = botPath
    });
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
