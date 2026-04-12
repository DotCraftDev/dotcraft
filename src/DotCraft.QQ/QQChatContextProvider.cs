using DotCraft.Abstractions;

namespace DotCraft.QQ;

/// <summary>
/// Contributes QQ-specific context to the agent prompt.
/// Reads from QQChatContextScope (AsyncLocal) at call time.
/// </summary>
public sealed class QQChatContextProvider : IChatContextProvider
{
    /// <inheritdoc />
    public string? GetSystemPromptSection()
    {
        var ctx = QQChatContextScope.Current;
        if (ctx is null)
            return null;

        if (ctx.IsGroupMessage)
        {
            return
$"""
# QQ Chat Context

You are currently in **QQ Bot** mode.
- Chat type: Group
- Group ID: {ctx.GroupId}

You can use qqSendVoiceToCurrentChat / qqSendVideoToCurrentChat / qqSendFileToCurrentChat for this group.
""";
        }

        return
$"""
# QQ Chat Context

You are currently in **QQ Bot** mode.
- Chat type: Private
- Sender QQ: {ctx.UserId}
- Sender name: {ctx.SenderName}

You can use qqSendVoiceToCurrentChat / qqSendVideoToCurrentChat / qqSendFileToCurrentChat in this private chat.
""";
    }

    /// <inheritdoc />
    public IEnumerable<string> GetRuntimeContextLines()
    {
        // Only group sessions need sender info injected into the user message.
        // Group sessions are keyed by qq_{GroupId} and shared across all members,
        // so the sender changes per message and cannot live in the stable system prompt.
        // Private sessions are keyed by qq_{UserId} (one person), so sender info
        // is already stable in the system prompt above.
        var ctx = QQChatContextScope.Current;
        if (ctx is not { IsGroupMessage: true })
            yield break;

        yield return $"Sender QQ: {ctx.UserId}";
        yield return $"Sender Name: {ctx.SenderName}";
    }
}
