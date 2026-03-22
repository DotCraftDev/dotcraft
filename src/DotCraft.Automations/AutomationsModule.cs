using DotCraft.Abstractions;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Orchestrator;
using DotCraft.Automations.Workspace;
using DotCraft.Configuration;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Automations;

/// <summary>
/// Automation task orchestration module (Gateway channel).
/// </summary>
[DotCraftModule("automations", Priority = 55, Description = "Local and remote automation task orchestrator")]
public sealed partial class AutomationsModule : ModuleBase
{
    public override bool IsEnabled(AppConfig config) =>
        config.GetSection<AutomationsConfig>("Automations").Enabled;

    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
    {
        var errors = new List<string>();
        var a = config.GetSection<AutomationsConfig>("Automations");
        if (a.Enabled && a.MaxConcurrentTasks < 1)
            errors.Add("Automations: MaxConcurrentTasks must be at least 1.");
        return errors;
    }

    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        var cfg = context.Config.GetSection<AutomationsConfig>("Automations");
        services.AddSingleton(cfg);
        services.AddSingleton<AutomationWorkspaceManager>();
        services.AddSingleton<IEnumerable<IAutomationSource>>(_ => Array.Empty<IAutomationSource>());
        services.AddSingleton<AutomationOrchestrator>();
    }

    public override IChannelService? CreateChannelService(IServiceProvider sp, ModuleContext context) =>
        ActivatorUtilities.CreateInstance<AutomationsChannelService>(sp);
}
