using System.Text.Json.Serialization;

namespace DotCraft.QQ.OneBot;

public sealed class OneBotMessageEvent : OneBotEvent
{
    [JsonPropertyName("message_type")]
    public string MessageType { get; set; } = string.Empty;

    [JsonPropertyName("sub_type")]
    public string SubType { get; set; } = string.Empty;

    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }

    [JsonPropertyName("user_id")]
    public long UserId { get; set; }

    [JsonPropertyName("group_id")]
    public long GroupId { get; set; }

    [JsonPropertyName("message")]
    public List<OneBotMessageSegment> Message { get; set; } = new();

    [JsonPropertyName("raw_message")]
    public string RawMessage { get; set; } = string.Empty;

    [JsonPropertyName("sender")]
    public OneBotSender Sender { get; set; } = new();

    [JsonPropertyName("font")]
    public int Font { get; set; }

    public bool IsGroupMessage => MessageType == "group";

    public bool IsPrivateMessage => MessageType == "private";

    public string GetPlainText()
    {
        var parts = new List<string>();
        foreach (var seg in Message)
        {
            var text = seg.GetText();
            if (text != null)
                parts.Add(text);
        }
        return string.Join("", parts);
    }

    public string GetSessionId()
    {
        return IsGroupMessage ? GroupId.ToString() : UserId.ToString();
    }
}
