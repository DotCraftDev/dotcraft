using System.Text.Json.Serialization;

namespace DotCraft.QQ.OneBot;

public sealed class OneBotNoticeEvent : OneBotEvent
{
    [JsonPropertyName("notice_type")]
    public string NoticeType { get; set; } = string.Empty;

    [JsonPropertyName("sub_type")]
    public string SubType { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public long UserId { get; set; }

    [JsonPropertyName("group_id")]
    public long GroupId { get; set; }

    [JsonPropertyName("operator_id")]
    public long OperatorId { get; set; }

    [JsonPropertyName("target_id")]
    public long TargetId { get; set; }

    [JsonPropertyName("sender_id")]
    public long SenderId { get; set; }
}
