using DotCraft.Abstractions;
using Microsoft.Extensions.AI;

namespace DotCraft.QQ;

/// <summary>
/// Provides QQ channel-specific tools for voice and file messaging.
/// Only available when QQBotClient is configured.
/// </summary>
public sealed class QQToolProvider : IAgentToolProvider
{
    /// <inheritdoc />
    public int Priority => 50; // Channel tools have medium priority

    /// <inheritdoc />
    public IEnumerable<AITool> CreateTools(ToolProviderContext context) => [];
}
