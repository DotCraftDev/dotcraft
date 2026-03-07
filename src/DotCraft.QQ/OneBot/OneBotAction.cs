using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.QQ.OneBot;

public sealed class OneBotAction
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public Dictionary<string, object?> Params { get; set; } = new();

    [JsonPropertyName("echo")]
    public string Echo { get; set; } = string.Empty;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static OneBotAction SendGroupMessage(long groupId, List<OneBotMessageSegment> message)
    {
        return new OneBotAction
        {
            Action = "send_group_msg",
            Params = new Dictionary<string, object?>
            {
                ["group_id"] = groupId,
                ["message"] = message
            }
        };
    }

    public static OneBotAction SendPrivateMessage(long userId, List<OneBotMessageSegment> message)
    {
        return new OneBotAction
        {
            Action = "send_private_msg",
            Params = new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["message"] = message
            }
        };
    }

    public static OneBotAction GetMsg(long messageId)
    {
        return new OneBotAction
        {
            Action = "get_msg",
            Params = new Dictionary<string, object?>
            {
                ["message_id"] = messageId
            }
        };
    }

    public static OneBotAction GetGroupMemberInfo(long groupId, long userId, bool noCache = false)
    {
        return new OneBotAction
        {
            Action = "get_group_member_info",
            Params = new Dictionary<string, object?>
            {
                ["group_id"] = groupId,
                ["user_id"] = userId,
                ["no_cache"] = noCache
            }
        };
    }

    public static OneBotAction GetGroupInfo(long groupId)
    {
        return new OneBotAction
        {
            Action = "get_group_info",
            Params = new Dictionary<string, object?>
            {
                ["group_id"] = groupId
            }
        };
    }

    public static OneBotAction GetLoginInfo()
    {
        return new OneBotAction
        {
            Action = "get_login_info",
            Params = new Dictionary<string, object?>()
        };
    }

    public static OneBotAction UploadGroupFile(long groupId, string file, string name, string? folder = null)
    {
        var p = new Dictionary<string, object?>
        {
            ["group_id"] = groupId,
            ["file"] = file,
            ["name"] = name
        };
        if (!string.IsNullOrEmpty(folder))
            p["folder"] = folder;
        return new OneBotAction { Action = "upload_group_file", Params = p };
    }

    public static OneBotAction UploadPrivateFile(long userId, string file, string name)
    {
        return new OneBotAction
        {
            Action = "upload_private_file",
            Params = new Dictionary<string, object?>
            {
                ["user_id"] = userId,
                ["file"] = file,
                ["name"] = name
            }
        };
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, SerializerOptions);
    }
}
