using DotCraft.Abstractions;
using Microsoft.Extensions.AI;

namespace DotCraft.WeCom;

/// <summary>
/// Provides WeCom (企业微信) channel-specific tools.
/// Only available when WeCom webhook is configured.
/// </summary>
public sealed class WeComToolProvider : IAgentToolProvider
{
    /// <inheritdoc />
    public int Priority => 50; // Channel tools have medium priority

    /// <inheritdoc />
    public IEnumerable<AITool> CreateTools(ToolProviderContext context)
    {
        var config = context.Config;
        var weComConfig = config.GetSection<WeComConfig>("WeCom");
        var weComBotConfig = config.GetSection<WeComBotConfig>("WeComBot");

        // Only create tools if WeCom is enabled with webhook or WeComBot is enabled
        var isWeComEnabled = (weComConfig.Enabled && !string.IsNullOrWhiteSpace(weComConfig.WebhookUrl))
                             || weComBotConfig.Enabled;
        
        if (!isWeComEnabled)
            return [];

        var weComTools = new WeComTools(weComConfig.WebhookUrl, fileSystem: context.AgentFileSystem);
        
        return
        [
            AIFunctionFactory.Create(weComTools.WeComNotify),
            AIFunctionFactory.Create(weComTools.WeComSendVoice),
            AIFunctionFactory.Create(weComTools.WeComSendFile)
        ];
    }
}
