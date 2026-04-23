using DotCraft.Abstractions;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.DashBoard;
using DotCraft.Automations.Local;
using DotCraft.Automations.Orchestrator;
using DotCraft.Automations.Protocol;
using DotCraft.Automations.Templates;
using DotCraft.Automations.Workspace;
using DotCraft.Configuration;
using DotCraft.DashBoard;
using DotCraft.Modules;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.AddSingleton<LocalTaskFileStore>();
        services.AddSingleton<UserTemplateFileStore>();
        services.AddSingleton<LocalWorkflowLoader>();
        services.AddSingleton<LocalAutomationSource>();
        services.AddSingleton<IAutomationSource>(sp => sp.GetRequiredService<LocalAutomationSource>());
        services.AddSingleton<AutomationOrchestrator>();
        services.AddSingleton<AutomationsRequestHandler>();
        services.AddSingleton<IAutomationsRequestHandler>(sp => sp.GetRequiredService<AutomationsRequestHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOrchestratorSnapshotProvider, AutomationsDashboardSnapshotProvider>());
    }

    public override IChannelService CreateChannelService(IServiceProvider sp) =>
        ActivatorUtilities.CreateInstance<AutomationsChannelService>(sp);

    /// <inheritdoc />
    public override IReadOnlyList<SessionChannelListEntry> GetSessionChannelListEntries() =>
        [new("automations", "system")];
}
