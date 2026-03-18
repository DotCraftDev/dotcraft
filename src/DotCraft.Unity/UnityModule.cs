using System.Collections.Generic;
using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Modules;

namespace DotCraft.Unity;

/// <summary>
/// DotCraft module for Unity Editor integration via ACP extension methods.
/// Provides tools for scene manipulation, asset management, and editor operations.
/// The module is enabled only when ACP mode is active; the tool provider
/// further checks that the connected client advertises the "_unity" extension.
/// </summary>
[DotCraftModule("unity", Priority = 50, Description = "Unity Editor integration via ACP extension methods")]
public sealed partial class UnityModule : ModuleBase
{
    public override bool IsEnabled(AppConfig config) => config.IsSectionEnabled("Acp");

    public override IEnumerable<IAgentToolProvider> GetToolProviders()
    {
        yield return new UnityAcpToolProvider();
    }
}
