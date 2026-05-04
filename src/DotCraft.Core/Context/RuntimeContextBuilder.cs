using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Context;

/// <summary>
/// Builds the [Runtime Context] block that is appended to each user message.
/// Keeping dynamic values (time, per-message sender) out of the system prompt
/// ensures the system prompt prefix stays stable across requests, enabling
/// LLM prompt cache reuse.
/// </summary>
public static class RuntimeContextBuilder
{
    /// <summary>
    /// Appends a [Runtime Context] <see cref="TextContent"/> with optional turn initiator metadata.
    /// </summary>
    public static IList<AIContent> AppendRuntimeContext(this IList<AIContent> contents, TurnInitiatorContext? initiator = null)
    {
        contents.Add(new TextContent($"\n{BuildBlock(initiator)}"));
        return contents;
    }

    private static string BuildBlock(TurnInitiatorContext? initiator = null)
    {
        var lines = new List<string>();

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm (dddd)");
        lines.Add($"Current Time: {now} ({TimeZoneInfo.Local.DisplayName})");

        foreach (var provider in ChatContextRegistry.All)
            lines.AddRange(provider.GetRuntimeContextLines());

        AddInitiatorLines(lines, initiator);

        return $"[Runtime Context]\n{string.Join("\n", lines)}";
    }

    private static void AddInitiatorLines(List<string> lines, TurnInitiatorContext? initiator)
    {
        if (initiator is null)
            return;

        AddLine(lines, "Channel", initiator.ChannelName);
        AddLine(lines, "Channel Context", initiator.ChannelContext);
        AddLine(lines, "Sender ID", initiator.UserId);
        AddLine(lines, "Sender Name", initiator.UserName);
        AddLine(lines, "Sender Role", initiator.UserRole);
        AddLine(lines, "Group/Chat ID", initiator.GroupId);

        var channel = initiator.ChannelName.Trim().ToLowerInvariant();
        switch (channel)
        {
            case "qq":
                AddLine(lines, "Sender QQ", initiator.UserId);
                AddLine(lines, "QQ Group ID", GetPrefixedContextId(initiator.ChannelContext, "group:") ?? initiator.GroupId);
                break;
            case "wecom":
                AddLine(lines, "WeCom User ID", initiator.UserId);
                AddLine(lines, "WeCom Chat ID", initiator.GroupId ?? GetPrefixedContextId(initiator.ChannelContext, "chat:"));
                break;
        }
    }

    private static void AddLine(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            lines.Add($"{label}: {value}");
    }

    private static string? GetPrefixedContextId(string? channelContext, string prefix)
    {
        if (string.IsNullOrWhiteSpace(channelContext))
            return null;

        return channelContext.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? channelContext[prefix.Length..]
            : null;
    }
}
