using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Modules;
using DotCraft.WeCom.Factories;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.WeCom;

/// <summary>
/// WeCom (Enterprise WeChat) module for enterprise messaging platform interaction.
/// Priority: 20
/// </summary>
[DotCraftModule("wecom", Priority = 20, Description = "WeCom (Enterprise WeChat) module for enterprise messaging platform interaction")]
public sealed partial class WeComModule : ModuleBase
{
    private readonly WeComConfigValidator _validator = new();

    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) => config.GetSection<WeComBotConfig>("WeComBot").Enabled;

    /// <inheritdoc />
    public override IReadOnlyList<SessionChannelListEntry> GetSessionChannelListEntries() => [new("wecom", "social")];

    /// <inheritdoc />
    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
        => _validator.Validate(config.GetSection<WeComBotConfig>("WeComBot"));

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        ChatContextRegistry.Register(new WeComChatContextProvider());

        var config = context.Config.GetSection<WeComBotConfig>("WeComBot");

        // Register WeComBotRegistry
        services.AddSingleton(WeComClientFactory.CreateRegistry(context));

        // Register WeComPermissionService
        services.AddSingleton(WeComClientFactory.CreatePermissionService(context));

        // Register WeComApprovalService (depends on WeComPermissionService)
        services.AddSingleton(sp =>
        {
            var permission = sp.GetRequiredService<WeComPermissionService>();
            var factory = new WeComApprovalServiceFactory();
            return (WeComApprovalService)factory.Create(new ApprovalServiceContext
            {
                Config = context.Config,
                WorkspacePath = context.Paths.WorkspacePath,
                PermissionService = permission,
                ApprovalTimeoutSeconds = config.ApprovalTimeoutSeconds
            });
        });
    }

    /// <inheritdoc />
    public override IEnumerable<IAgentToolProvider> GetToolProviders()
        => [new WeComToolProvider()];

    /// <inheritdoc />
    public override IChannelService CreateChannelService(IServiceProvider sp, ModuleContext context)
        => ActivatorUtilities.CreateInstance<WeComChannelService>(sp);
}
