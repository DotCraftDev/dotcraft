using System.Text.Json.Serialization;

namespace DotCraft.QQ.OneBot;

public sealed class OneBotRequestEvent : OneBotEvent
{
    [JsonPropertyName("request_type")]
    public string RequestType { get; set; } = string.Empty;

    [JsonPropertyName("sub_type")]
    public string SubType { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public long UserId { get; set; }

    [JsonPropertyName("group_id")]
    public long GroupId { get; set; }

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = string.Empty;

    [JsonPropertyName("flag")]
    public string Flag { get; set; } = string.Empty;
}
