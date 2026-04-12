using DotCraft.Abstractions;
using DotCraft.Automations;
using DotCraft.Configuration;
using DotCraft.ExternalChannel;
using DotCraft.GitHubTracker;
using DotCraft.Hosting;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DotCraft.Gateway;

/// <summary>
/// Gateway module that enables running multiple channel modules concurrently (QQ, WeCom, API).
/// Priority: 100 (highest) — overrides all single-channel modules when enabled.
/// </summary>
[DotCraftModule("gateway", Priority = 100, Description = "Gateway mode: runs all enabled channel modules concurrently", CanBePrimaryHost = true)]
public sealed partial class GatewayModule : ModuleBase
{
    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) => HasGatewayChannelsEnabled(config);

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        // Register MessageRouter as a singleton for cross-channel delivery (TryAdd: app-server may register first)
        services.TryAddSingleton<IChannelRuntimeRegistry, ChannelRuntimeRegistry>();
        services.TryAddSingleton(sp => new MessageRouter(sp.GetRequiredService<IChannelRuntimeRegistry>()));

        // Register ExternalChannelRegistry as a singleton (all external hosts + WebSocket routing)
        services.TryAddSingleton<ExternalChannelRegistry>();
        services.TryAddSingleton<IChannelRuntimeToolProvider, ExternalChannelToolProvider>();
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
    {
        var errors = new List<string>();
        if (!HasGatewayChannelsEnabled(config))
            errors.Add("Gateway mode is enabled but no channel modules are enabled.");
        return errors;
    }

    private static bool HasGatewayChannelsEnabled(AppConfig config)
        => config.IsSectionEnabled("QQBot")
           || config.IsSectionEnabled("WeComBot")
           || config.IsSectionEnabled("Api")
           || config.IsSectionEnabled("AgUi")
           || config.GetSection<GitHubTrackerConfig>("GitHubTracker").Enabled
           || config.GetSection<AutomationsConfig>("Automations").Enabled
           || ExternalChannelManager.HasEnabledChannels(config);
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
