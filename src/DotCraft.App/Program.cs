using System.Text;
using DotCraft.CLI;
using DotCraft.AppServer;
using DotCraft.Diagnostics;
using DotCraft.Configuration;
using DotCraft.Hub;
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
var isRemoteExec = cliArgs.Mode == CommandLineArgs.RunMode.Exec
               && !string.IsNullOrWhiteSpace(cliArgs.RemoteUrl);
var isHeadless = CliStartup.IsHeadlessMode(cliArgs.Mode);

// -------------------------------------------------------------------------
// 2. Prepare subprocess environment (stdout → stderr, ignore Ctrl+C)
//    Only needed when the process reserves stdout for a wire protocol.
// -------------------------------------------------------------------------
if (cliArgs.ReservesStdout)
{
    SubprocessEnvironment.Prepare();
}

// -------------------------------------------------------------------------
// 3. Hub mode: global, workspace-independent local coordinator shell.
// -------------------------------------------------------------------------
if (cliArgs.Mode == CommandLineArgs.RunMode.Hub)
{
    var hubPaths = HubPaths.ForCurrentUser();
    var globalConfig = AppConfig.Load(hubPaths.GlobalConfigPath);
    cliArgs.ApplyTo(globalConfig);

    var hubConfig = globalConfig.GetSection<HubConfig>("Hub");
    await using var hubHost = new HubHost(hubConfig, hubPaths);
    await hubHost.RunAsync();
    return;
}

// -------------------------------------------------------------------------
// 4. Workspace discovery & initialization
// -------------------------------------------------------------------------
var workspacePath = Directory.GetCurrentDirectory();
var botPath = Path.GetFullPath(".craft");
var workspaceJustInitialized = false;

if (cliArgs.Mode == CommandLineArgs.RunMode.Skill)
{
    var result = await SkillCliRunner.RunAsync(botPath, cliArgs, Console.Out, Console.Error);
    Environment.Exit(result);
    return;
}

if (cliArgs.Mode == CommandLineArgs.RunMode.Setup)
{
    static Language ParseSetupLanguage(string? value)
    {
        if (string.Equals(value, "Chinese", StringComparison.OrdinalIgnoreCase))
            return Language.Chinese;
        if (string.Equals(value, "English", StringComparison.OrdinalIgnoreCase))
            return Language.English;
        throw new ArgumentException("Missing or invalid --language. Expected Chinese or English.");
    }

    static WorkspaceBootstrapProfile ParseSetupProfile(string? value)
    {
        if (string.Equals(value, "default", StringComparison.OrdinalIgnoreCase))
            return WorkspaceBootstrapProfile.Default;
        if (string.Equals(value, "developer", StringComparison.OrdinalIgnoreCase))
            return WorkspaceBootstrapProfile.Developer;
        if (string.Equals(value, "personal-assistant", StringComparison.OrdinalIgnoreCase))
            return WorkspaceBootstrapProfile.PersonalAssistant;
        throw new ArgumentException("Missing or invalid --profile. Expected default, developer, or personal-assistant.");
    }

    try
    {
        if (cliArgs.SaveUserConfig && cliArgs.PreferExistingUserConfig)
            throw new ArgumentException("Cannot combine --save-user-config with --prefer-existing-user-config.");

        var request = new WorkspaceSetupRequest
        {
            Language = ParseSetupLanguage(cliArgs.SetupLanguage),
            Model = string.IsNullOrWhiteSpace(cliArgs.SetupModel)
                ? throw new ArgumentException("Missing --model.")
                : cliArgs.SetupModel.Trim(),
            EndPoint = string.IsNullOrWhiteSpace(cliArgs.SetupEndPoint)
                ? throw new ArgumentException("Missing --endpoint.")
                : cliArgs.SetupEndPoint.Trim(),
            ApiKey = string.IsNullOrWhiteSpace(cliArgs.SetupApiKey)
                ? cliArgs.PreferExistingUserConfig
                    ? string.Empty
                    : throw new ArgumentException("Missing --api-key.")
                : cliArgs.SetupApiKey.Trim(),
            Profile = ParseSetupProfile(cliArgs.SetupProfile),
            SaveToUserConfig = cliArgs.SaveUserConfig,
            PreferExistingUserConfig = cliArgs.PreferExistingUserConfig
        };

        var result = InitHelper.RunSetup(botPath, request);
        if (result != 0)
        {
            Environment.Exit(result);
            return;
        }

        Console.WriteLine($"Workspace setup completed: {workspacePath}");
        if (request.SaveToUserConfig)
        {
            Console.WriteLine("Saved language and AI settings to user config.");
        }
        else if (request.PreferExistingUserConfig)
        {
            Console.WriteLine("Reused user config defaults and saved workspace-only overrides.");
        }
        else
        {
            Console.WriteLine("Saved language and AI settings to workspace config.");
        }
        return;
    }
    catch (Exception ex)
    {
        await Console.Error.WriteLineAsync(ex.Message);
        Environment.Exit(1);
        return;
    }
}

var startupDecision = CliStartup.DecideWorkspaceStartup(cliArgs.Mode, Directory.Exists(botPath));
if (startupDecision == WorkspaceStartupDecision.ShowUsage)
{
    await CliStartup.WriteUsageAsync(Console.Error);
    Environment.Exit(1);
    return;
}

if (startupDecision == WorkspaceStartupDecision.MissingWorkspace)
{
    await Console.Error.WriteLineAsync($"DotCraft workspace not found: {botPath}");
    Environment.Exit(1);
    return;
}

if (startupDecision == WorkspaceStartupDecision.InitializeInteractively)
{
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
if (cliArgs.Mode == CommandLineArgs.RunMode.AppServer)
{
    ManagedAppServerEnvironment.ApplyTo(config);
}

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
if (!isRemoteExec && string.IsNullOrWhiteSpace(config.ApiKey))
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

if (cliArgs.Mode == CommandLineArgs.RunMode.None)
{
    if (workspaceJustInitialized)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[green]✓ {Strings.InitWorkspaceInitialized}[/]");
        await Console.Out.WriteLineAsync("Run `dotcraft exec <prompt>` to start a one-shot command-line task.");
    }
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

// Module config validation
var configValidationOk = ServiceRegistration.ValidateConfigurations(config, moduleRegistry);
if (!configValidationOk && isHeadless)
{
    await Console.Error.WriteLineAsync("Configuration validation failed.");
    Environment.Exit(1);
    return;
}

var preferredPrimaryModuleName = cliArgs.Mode switch
{
    CommandLineArgs.RunMode.Exec => "cli",
    CommandLineArgs.RunMode.AppServer => "app-server",
    CommandLineArgs.RunMode.Gateway => "gateway",
    CommandLineArgs.RunMode.Acp => "acp",
    _ => null
};

var hostBuilder = new HostBuilder(moduleRegistry, config, paths, preferredPrimaryModuleName);

var services = new ServiceCollection()
    .AddSingleton(moduleRegistry)
    .AddSingleton(cliArgs)
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
