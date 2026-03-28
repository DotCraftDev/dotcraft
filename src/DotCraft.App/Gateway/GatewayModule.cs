using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.ExternalChannel;
using DotCraft.Hosting;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.Gateway;

/// <summary>
/// Gateway module that enables running multiple channel modules concurrently (QQ, WeCom, API).
/// Priority: 100 (highest) — overrides all single-channel modules when enabled.
/// </summary>
[DotCraftModule("gateway", Priority = 100, Description = "Gateway mode: runs all enabled channel modules concurrently")]
public sealed partial class GatewayModule : ModuleBase
{
    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config)
        => config.IsSectionEnabled("QQBot") || config.IsSectionEnabled("WeComBot")
           || config.IsSectionEnabled("Api") || config.IsSectionEnabled("AgUi")
           || config.IsSectionEnabled("GitHubTracker")
           || config.IsSectionEnabled("Automations")
           || ExternalChannelManager.HasEnabledChannels(config);

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        // Register MessageRouter as a singleton for cross-channel delivery (TryAdd: app-server may register first)
        services.TryAddSingleton(_ => new MessageRouter());

        // Register ExternalChannelRegistry as a singleton for WebSocket adapter routing
        services.TryAddSingleton<ExternalChannelRegistry>();
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
    {
        var errors = new List<string>();
        if (!config.IsSectionEnabled("QQBot") && !config.IsSectionEnabled("WeComBot")
            && !config.IsSectionEnabled("Api") && !config.IsSectionEnabled("AgUi")
            && !config.IsSectionEnabled("GitHubTracker")
            && !config.IsSectionEnabled("Automations")
            && !ExternalChannelManager.HasEnabledChannels(config))
            errors.Add("Gateway mode is enabled but no channel modules are enabled.");
        return errors;
    }
}

/// <summary>
/// Host factory for Gateway mode.
/// </summary>
[HostFactory("gateway")]
public sealed class GatewayHostFactory : IHostFactory
{
    /// <inheritdoc />
    public IDotCraftHost CreateHost(IServiceProvider sp, ModuleContext context)
    {
        var registry = sp.GetRequiredService<ModuleRegistry>();
        var config = sp.GetRequiredService<AppConfig>();

        // Collect channel services from all enabled non-gateway modules
        var channelServices = registry
            .GetEnabledModules(config)
            .Where(m => m.Name != "gateway")
            .Select(m => m.CreateChannelService(sp))
            .OfType<IChannelService>()
            .ToList();

        var router = sp.GetRequiredService<MessageRouter>();
        var externalChannelRegistry = sp.GetRequiredService<ExternalChannelRegistry>();

        return new GatewayHost(
            sp,
            config,
            context.Paths,
            sp.GetRequiredService<Skills.SkillsLoader>(),
            sp.GetRequiredService<Cron.CronService>(),
            channelServices,
            router,
            registry,
            externalChannelRegistry);
    }
}
