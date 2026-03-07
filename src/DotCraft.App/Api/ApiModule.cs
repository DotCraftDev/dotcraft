using DotCraft.Abstractions;
using DotCraft.Api.Factories;
using DotCraft.Configuration;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Api;

/// <summary>
/// API module for OpenAI-compatible HTTP API interaction.
/// Priority: 10
/// </summary>
[DotCraftModule("api", Priority = 10, Description = "API module for OpenAI-compatible HTTP API interaction")]
public sealed partial class ApiModule : ModuleBase
{
    private readonly ApiConfigValidator _validator = new();

    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) => config.Api.Enabled;

    /// <inheritdoc />
    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
        => _validator.Validate(config.Api);

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        var config = context.Config.Api;

        // Register ApiApprovalService
        services.AddSingleton(_ =>
        {
            var factory = new ApiApprovalServiceFactory();
            return (ApiApprovalService)factory.Create(new ApprovalServiceContext
            {
                Config = context.Config,
                WorkspacePath = context.Paths.WorkspacePath,
                ApprovalMode = config.ApprovalMode,
                AutoApprove = config.AutoApprove,
                ApprovalTimeoutSeconds = config.ApprovalTimeoutSeconds
            });
        });
    }

    /// <inheritdoc />
    public override IEnumerable<IAgentToolProvider> GetToolProviders()
        => [];

    /// <inheritdoc />
    public override IChannelService? CreateChannelService(IServiceProvider sp, ModuleContext context)
        => ActivatorUtilities.CreateInstance<ApiChannelService>(sp);
}
