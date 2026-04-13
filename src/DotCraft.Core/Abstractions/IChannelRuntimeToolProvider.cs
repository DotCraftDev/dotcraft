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
