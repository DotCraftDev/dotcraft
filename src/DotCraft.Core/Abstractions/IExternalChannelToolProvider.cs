using Microsoft.Extensions.AI;
using DotCraft.Protocol;

namespace DotCraft.Abstractions;

/// <summary>
/// Produces runtime tools declared by connected external channel adapters.
/// </summary>
public interface IExternalChannelToolProvider
{
    /// <summary>
    /// Builds the channel-scoped tools available to the specified thread.
    /// </summary>
    /// <param name="thread">The thread being prepared for tool injection.</param>
    /// <param name="reservedToolNames">
    /// Tool names already present in the thread's tool list. External tools that conflict with these names
    /// must be rejected for the current registration snapshot.
    /// </param>
    IReadOnlyList<AITool> CreateToolsForThread(
        SessionThread thread,
        IReadOnlySet<string> reservedToolNames);
}
