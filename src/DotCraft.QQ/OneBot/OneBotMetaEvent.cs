using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.QQ.OneBot;

public sealed class OneBotMetaEvent : OneBotEvent
{
    [JsonPropertyName("meta_event_type")]
    public string MetaEventType { get; set; } = string.Empty;

    [JsonPropertyName("sub_type")]
    public string SubType { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public JsonElement? Status { get; set; }

    [JsonPropertyName("interval")]
    public long Interval { get; set; }

    public bool IsLifecycle => MetaEventType == "lifecycle";

    public bool IsHeartbeat => MetaEventType == "heartbeat";
}
