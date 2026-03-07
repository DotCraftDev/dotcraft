using DotCraft.Diagnostics;

namespace DotCraft.QQ;

/// <summary>
/// Human-readable display formatters for QQ tool calls.
/// </summary>
public static class QQToolDisplays
{
    public static string QQSendGroupVoice(IDictionary<string, object?>? args)
    {
        var groupId = ToolDisplayHelpers.GetString(args, "groupId") ?? "group";
        return $"Sent voice to group {groupId}";
    }

    public static string QQSendPrivateVoice(IDictionary<string, object?>? args)
    {
        var userId = ToolDisplayHelpers.GetString(args, "userId") ?? "user";
        return $"Sent voice to user {userId}";
    }

    public static string QQSendGroupVideo(IDictionary<string, object?>? args)
    {
        var groupId = ToolDisplayHelpers.GetString(args, "groupId") ?? "group";
        return $"Sent video to group {groupId}";
    }

    public static string QQSendPrivateVideo(IDictionary<string, object?>? args)
    {
        var userId = ToolDisplayHelpers.GetString(args, "userId") ?? "user";
        return $"Sent video to user {userId}";
    }

    public static string QQUploadGroupFile(IDictionary<string, object?>? args)
    {
        var fileName = ToolDisplayHelpers.GetString(args, "fileName")
                       ?? Path.GetFileName(ToolDisplayHelpers.GetString(args, "filePath") ?? "file");
        var groupId = ToolDisplayHelpers.GetString(args, "groupId") ?? "group";
        return $"Uploaded {fileName} to group {groupId}";
    }

    public static string QQUploadPrivateFile(IDictionary<string, object?>? args)
    {
        var fileName = ToolDisplayHelpers.GetString(args, "fileName")
                       ?? Path.GetFileName(ToolDisplayHelpers.GetString(args, "filePath") ?? "file");
        var userId = ToolDisplayHelpers.GetString(args, "userId") ?? "user";
        return $"Uploaded {fileName} to user {userId}";
    }
}
