using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.AGUI;

/// <summary>
/// AG-UI protocol server module. Exposes the agent as a separate channel on its own port.
/// Priority: 5 (between API 10 and CLI 0).
/// </summary>
[DotCraftModule("ag-ui", Priority = 5, Description = "AG-UI protocol server")]
public sealed partial class AGUIModule : ModuleBase
{
    private readonly AGUIConfigValidator _validator = new();

    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) => config.GetSection<AGUIConfig>("AgUi").Enabled;

    /// <inheritdoc />
    public override IReadOnlyList<string> ValidateConfig(AppConfig config)
        => _validator.Validate(config.GetSection<AGUIConfig>("AgUi"));

    /// <inheritdoc />
    public override IEnumerable<IAgentToolProvider> GetToolProviders() => [];

    /// <inheritdoc />
    public override IChannelService CreateChannelService(IServiceProvider sp)
        => ActivatorUtilities.CreateInstance<AGUIChannelService>(sp);
}
