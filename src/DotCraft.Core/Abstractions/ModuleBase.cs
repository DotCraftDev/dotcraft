using DotCraft.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Abstractions;

/// <summary>
/// Base class for DotCraft modules providing common functionality.
/// </summary>
public abstract class ModuleBase : IDotCraftModule
{
    /// <inheritdoc />
    public virtual string Name => "";

    /// <inheritdoc />
    public virtual int Priority => 0;

    /// <inheritdoc />
    public abstract bool IsEnabled(AppConfig config);

    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services, ModuleContext context)
    {
        // Default implementation does nothing.
        // Derived classes can override to register module-specific services.
    }

    /// <inheritdoc />
    public virtual IReadOnlyList<string> ValidateConfig(AppConfig config) => [];

    /// <inheritdoc />
    public virtual IChannelService? CreateChannelService(IServiceProvider sp, ModuleContext context) => null;

    /// <inheritdoc />
    public virtual IEnumerable<IAgentToolProvider> GetToolProviders() => [];
}
