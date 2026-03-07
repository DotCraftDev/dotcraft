using DotCraft.Diagnostics;

namespace DotCraft.WeCom;

/// <summary>
/// Human-readable display formatters for WeCom tool calls.
/// </summary>
public static class WeComToolDisplays
{
    public static string WeComNotify(IDictionary<string, object?>? args)
    {
        var message = ToolDisplayHelpers.GetString(args, "message") ?? "";
        return $"Sent WeCom notification: {ToolDisplayHelpers.Truncate(message, 60)}";
    }

    public static string WeComSendVoice(IDictionary<string, object?>? args)
    {
        var filePath = ToolDisplayHelpers.GetString(args, "filePath") ?? "file";
        return $"Sent voice {Path.GetFileName(filePath)}";
    }

    public static string WeComSendFile(IDictionary<string, object?>? args)
    {
        var filePath = ToolDisplayHelpers.GetString(args, "filePath") ?? "file";
        return $"Sent file {Path.GetFileName(filePath)}";
    }
}
