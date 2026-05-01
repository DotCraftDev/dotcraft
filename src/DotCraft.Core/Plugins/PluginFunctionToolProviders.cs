using DotCraft.Abstractions;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Plugins;

/// <summary>
/// Bridges process-scoped plugin function providers into the existing agent tool provider pipeline.
/// </summary>
public sealed class PluginFunctionToolProvider(IEnumerable<IPluginFunctionProvider> providers) : IAgentToolProvider
{
    public int Priority => 120;

    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        return providers
            .OrderBy(provider => provider.Priority)
            .SelectMany(provider => provider.CreateFunctions(context))
            .Select(registration => (AITool)new PluginFunctionRuntimeFunction(registration));
    }
}

/// <summary>
/// Bridges thread-scoped plugin function providers into Session Core's runtime tool hook.
/// </summary>
public sealed class ThreadPluginFunctionToolProvider(IEnumerable<IThreadPluginFunctionProvider> providers)
    : IChannelRuntimeToolProvider, IReservedRuntimeToolNameConfigurator
{
    private readonly IReadOnlyList<IThreadPluginFunctionProvider> _providers =
        providers.OrderBy(provider => provider.Priority).ToArray();

    public IReadOnlyList<AITool> CreateToolsForThread(
        SessionThread thread,
        IReadOnlySet<string> reservedToolNames)
    {
        var tools = new List<AITool>();
        var seen = new HashSet<string>(reservedToolNames, StringComparer.Ordinal);
        foreach (var provider in _providers)
        {
            foreach (var registration in provider.CreateFunctionsForThread(thread, seen))
            {
                if (string.IsNullOrWhiteSpace(registration.Descriptor.Name))
                    continue;

                if (!seen.Add(registration.Descriptor.Name))
                    continue;

                tools.Add(new PluginFunctionRuntimeFunction(registration));
            }
        }

        return tools;
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
