using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Hosting;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Gateway;

/// <summary>
/// Gateway module that enables running multiple channel modules concurrently (QQ, WeCom, API).
/// Priority: 100 (highest) — overrides all single-channel modules when enabled.
/// </summary>
[DotCraftModule("gateway", Priority = 100,
    Description = "Gateway mode: runs all enabled channel modules concurrently")]
public sealed partial class GatewayModule : ModuleBase
{
    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config)
        => config.QQBot.Enabled || config.WeComBot.Enabled || config.Api.Enabled;

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        // Register MessageRouter as a singleton for cross-channel delivery
        services.AddSingleton(_ => new MessageRouter());
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
    {
        var errors = new List<string>();
        if (!config.QQBot.Enabled && !config.WeComBot.Enabled && !config.Api.Enabled)
            errors.Add("Gateway mode is enabled but no channel modules are enabled (QQBot, WeComBot, Api).");
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

        var subContext = new ModuleContext
        {
            Config = config,
            Paths = context.Paths,
            ServiceProvider = sp
        };

        // Collect channel services from all enabled non-gateway modules
        var channelServices = registry
            .GetEnabledModules(config)
            .Where(m => m.Name != "gateway")
            .Select(m => m.CreateChannelService(sp, subContext))
            .OfType<IChannelService>()
            .ToList();

        var router = sp.GetRequiredService<MessageRouter>();

        return new GatewayHost(
            sp,
            config,
            context.Paths,
            sp.GetRequiredService<Memory.SessionStore>(),
            sp.GetRequiredService<Skills.SkillsLoader>(),
            sp.GetRequiredService<Cron.CronService>(),
            channelServices,
            router,
            registry);
    }
}
