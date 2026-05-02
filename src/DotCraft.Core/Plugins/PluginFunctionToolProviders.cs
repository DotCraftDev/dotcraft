using DotCraft.Abstractions;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Plugins;

/// <summary>
/// Bridges process-scoped plugin function providers into the existing agent tool provider pipeline.
/// </summary>
public sealed class PluginFunctionToolProvider(
    IEnumerable<IPluginFunctionProvider> providers,
    PluginDiagnosticsStore? diagnosticsStore = null) : IAgentToolProvider
{
    public int Priority => 120;

    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        var diagnostics = new List<PluginDiagnostic>();
        var fallbackRegistrations = providers
            .OrderBy(provider => provider.Priority)
            .SelectMany(provider => provider.CreateFunctions(context))
            .ToArray();

        var discovery = new PluginDiscoveryService().Discover(
            context.Config,
            context.WorkspacePath,
            context.BotPath);
        diagnostics.AddRange(discovery.Diagnostics);

        var bound = PluginFunctionManifestBinder.Bind(
            fallbackRegistrations,
            discovery.Plugins,
            diagnostics);
        var resolved = PluginFunctionConflictResolver.ResolveRegistrations(bound, diagnostics);
        diagnosticsStore ??= PluginDiagnosticsStore.Shared;
        diagnosticsStore.Replace(diagnostics);
        PluginDiagnosticsLogger.Write(diagnostics);

        return resolved.Select(registration => (AITool)new PluginFunctionRuntimeFunction(registration));
    }
}

/// <summary>
/// Bridges thread-scoped plugin function providers into Session Core's runtime tool hook.
/// </summary>
public sealed class ThreadPluginFunctionToolProvider(
    IEnumerable<IThreadPluginFunctionProvider> providers,
    PluginDiagnosticsStore? diagnosticsStore = null)
    : IChannelRuntimeToolProvider, IReservedRuntimeToolNameConfigurator
{
    private readonly IReadOnlyList<IThreadPluginFunctionProvider> _providers =
        providers.OrderBy(provider => provider.Priority).ToArray();

    public IReadOnlyList<AITool> CreateToolsForThread(
        SessionThread thread,
        IReadOnlySet<string> reservedToolNames)
    {
        var diagnostics = new List<PluginDiagnostic>();
        var registrations = new List<PluginFunctionRegistration>();
        foreach (var provider in _providers)
        {
            registrations.AddRange(provider.CreateFunctionsForThread(thread, reservedToolNames));
        }

        var resolved = PluginFunctionConflictResolver.ResolveRegistrations(
            registrations,
            diagnostics,
            reservedToolNames);
        diagnosticsStore ??= PluginDiagnosticsStore.Shared;
        diagnosticsStore.Append(diagnostics);
        PluginDiagnosticsLogger.Write(diagnostics);

        return resolved
            .Select(registration => (AITool)new PluginFunctionRuntimeFunction(registration))
            .ToArray();
    }

    public void ConfigureReservedToolNames(IEnumerable<string> toolNames)
    {
        foreach (var provider in _providers)
        {
            if (provider is IReservedRuntimeToolNameConfigurator configurator)
                configurator.ConfigureReservedToolNames(toolNames);
        }
    }
}
