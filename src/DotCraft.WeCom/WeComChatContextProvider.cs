using DotCraft.Abstractions;

namespace DotCraft.WeCom;

/// <summary>
/// Contributes WeCom-specific context to the agent prompt.
/// Reads from WeComChatContextScope (AsyncLocal) at call time.
/// </summary>
public sealed class WeComChatContextProvider : IChatContextProvider
{
    /// <inheritdoc />
    public string? GetSystemPromptSection()
    {
        var ctx = WeComChatContextScope.Current;
        if (ctx is null)
            return null;

        return
$"""
# WeCom Chat Context

You are currently in **WeCom Bot** mode.
- Chat ID: {ctx.ChatId}
- Sender User ID: {ctx.UserId}
- Sender name: {ctx.UserName}

You can use WeComSendVoice / WeComSendFile in the current chat.
""";
    }

    /// <inheritdoc />
    public IEnumerable<string> GetRuntimeContextLines()
    {
        // WeCom sessions are keyed by wecom_{chatId}_{userId}, so all context fields
        // are stable per session. No dynamic lines needed in the user message.
        return [];
    }
}
