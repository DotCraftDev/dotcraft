using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Abstractions;

/// <summary>
/// Produces thread-scoped runtime channel tools for the matching origin channel.
/// </summary>
public interface IChannelRuntimeToolProvider
{
    IReadOnlyList<AITool> CreateToolsForThread(
        SessionThread thread,
        IReadOnlySet<string> reservedToolNames);
}

/// <summary>
/// Optional configurator for runtime tool providers that need the reserved tool-name set
/// before channel adapters finalize their dynamic registrations.
/// </summary>
public interface IReservedRuntimeToolNameConfigurator
{
    void ConfigureReservedToolNames(IEnumerable<string> toolNames);
}
