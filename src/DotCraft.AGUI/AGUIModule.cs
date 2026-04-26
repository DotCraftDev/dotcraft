using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Agui;

/// <summary>
/// AG-UI protocol server module. Exposes the agent as a separate channel on its own port.
/// Priority: 5 (between API 10 and CLI 0).
/// </summary>
[DotCraftModule("ag-ui", Priority = 5, Description = "AG-UI protocol server")]
public sealed partial class AguiModule : ModuleBase
{
    private readonly AguiConfigValidator _validator = new();

    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) => config.GetSection<AguiConfig>("AgUi").Enabled;

    /// <inheritdoc />
    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
        => _validator.Validate(config.GetSection<AguiConfig>("AgUi"));

    /// <inheritdoc />
    public override IEnumerable<IAgentToolProvider> GetToolProviders() => [];

    /// <inheritdoc />
    public override IChannelService CreateChannelService(IServiceProvider sp)
        => ActivatorUtilities.CreateInstance<AguiChannelService>(sp);
}
