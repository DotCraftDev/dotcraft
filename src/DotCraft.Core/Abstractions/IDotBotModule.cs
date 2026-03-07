using DotCraft.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DotCraft.Abstractions;

/// <summary>
/// Defines a module that can be registered with the DotCraft application.
/// Each module represents an interaction channel (CLI, API, QQ, WeCom, etc.).
/// </summary>
public interface IDotCraftModule
{
    /// <summary>
    /// Gets the unique name of the module (e.g., "cli", "api", "qq", "wecom").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the priority of the module for host selection.
    /// Higher priority modules are preferred when multiple modules are enabled.
    /// Default priority is 0. Suggested priorities: CLI=0, API=10, WeCom=20, QQ=30.
    /// </summary>
    int Priority => 0;

    /// <summary>
    /// Determines whether this module is enabled based on the current configuration.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <returns>True if the module should be active; otherwise, false.</returns>
    bool IsEnabled(AppConfig config);

    /// <summary>
    /// Configures services for this module.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    /// <param name="context">The module context containing configuration and paths.</param>
    void ConfigureServices(IServiceCollection services, ModuleContext context);

    /// <summary>
    /// Validates this module's configuration section.
    /// </summary>
    /// <param name="config">The application configuration.</param>
    /// <returns>List of validation errors, empty if valid.</returns>
    IReadOnlyList<string> ValidateConfig(AppConfig config) => [];

    /// <summary>
    /// Creates a channel service for use in Gateway mode, where multiple channels run concurrently.
    /// Returns null if the module does not support channel service mode.
    /// </summary>
    /// <param name="sp">The service provider with all shared DI services available.</param>
    /// <param name="context">The module context containing configuration and paths.</param>
    /// <returns>A channel service instance, or null if not supported.</returns>
    IChannelService? CreateChannelService(IServiceProvider sp, ModuleContext context) => null;

    /// <summary>
    /// Gets the tool providers contributed by this module.
    /// Tool providers are used to register AI tools specific to this module's functionality.
    /// </summary>
    /// <returns>An enumerable of tool providers, or empty if the module provides no tools.</returns>
    IEnumerable<IAgentToolProvider> GetToolProviders() => [];
}
