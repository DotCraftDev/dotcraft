using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Hosting;
using DotCraft.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.CLI;

/// <summary>
/// CLI module for interactive console-based interaction.
/// This is the default module when no other modules are enabled.
/// Priority: 0 (lowest - used as fallback)
/// </summary>
[DotCraftModule("cli", Priority = 0, Description = "CLI module for interactive console-based interaction (default fallback)", CanBePrimaryHost = true)]
public sealed partial class CliModule : ModuleBase
{
    /// <inheritdoc />
    public override bool IsEnabled(AppConfig config) => true;

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
    }

    /// <inheritdoc />
    public override IEnumerable<IAgentToolProvider> GetToolProviders()
        => [];

    /// <inheritdoc />
    public override IReadOnlyList<SessionChannelListEntry> GetSessionChannelListEntries() => [new("cli", "builtin")];
}

/// <summary>
/// Host factory for CLI mode.
/// </summary>
[HostFactory("cli")]
public sealed class CliHostFactory : IHostFactory
{
    /// <inheritdoc />
    public IDotCraftHost CreateHost(IServiceProvider serviceProvider, ModuleContext context)
    {
        return ActivatorUtilities.CreateInstance<CliHost>(serviceProvider);
    }
}
