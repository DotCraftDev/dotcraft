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
    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        // Only create tools if QQ client is available
        if (context.ChannelClient is not QQBotClient qqClient)
            return [];

        var qqTools = new QQTools(qqClient);
        
        return
        [
            AIFunctionFactory.Create(qqTools.QQSendGroupVoice),
            AIFunctionFactory.Create(qqTools.QQSendPrivateVoice),
            AIFunctionFactory.Create(qqTools.QQSendGroupVideo),
            AIFunctionFactory.Create(qqTools.QQSendPrivateVideo),
            AIFunctionFactory.Create(qqTools.QQUploadGroupFile),
            AIFunctionFactory.Create(qqTools.QQUploadPrivateFile)
        ];
    }
}
