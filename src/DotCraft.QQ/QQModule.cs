using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Modules;
using DotCraft.QQ.Factories;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.QQ;

/// <summary>
/// QQ Bot module for QQ platform interaction via OneBot protocol.
/// Priority: 30 (highest)
/// </summary>
[DotCraftModule("qq", Priority = 30, Description = "QQ Bot module for QQ platform interaction via OneBot protocol")]
public sealed partial class QQModule : ModuleBase
{
    private readonly QQConfigValidator _validator = new();

    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) => config.GetSection<QQBotConfig>("QQBot").Enabled;

    /// <inheritdoc />
    public override IReadOnlyList<SessionChannelListEntry> GetSessionChannelListEntries() => [new("qq", "social")];

    /// <inheritdoc />
    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
        => _validator.Validate(config.GetSection<QQBotConfig>("QQBot"));

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        ChatContextRegistry.Register(new QQChatContextProvider());

        var config = context.Config.GetSection<QQBotConfig>("QQBot");

        // Register QQBotClient
        services.AddSingleton(_ => QQClientFactory.CreateClient(context));

        // Register QQPermissionService
        services.AddSingleton(QQClientFactory.CreatePermissionService(context));

        // Register QQApprovalService (depends on QQBotClient and QQPermissionService)
        services.AddSingleton(sp =>
        {
            var permission = sp.GetRequiredService<QQPermissionService>();
            var factory = new QQApprovalServiceFactory(sp.GetRequiredService<QQBotClient>());
            return (QQApprovalService)factory.Create(new ApprovalServiceContext
            {
                Config = context.Config,
                WorkspacePath = context.Paths.WorkspacePath,
                PermissionService = permission,
                ApprovalTimeoutSeconds = config.ApprovalTimeoutSeconds
            });
        });
    }

    /// <inheritdoc />
    public override IEnumerable<IAgentToolProvider> GetToolProviders() => [];

    /// <inheritdoc />
    public override IChannelService CreateChannelService(IServiceProvider sp)
        => ActivatorUtilities.CreateInstance<QQChannelService>(sp);
}
