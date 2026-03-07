using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.QQ.OneBot;

public class OneBotEvent
{
    [JsonPropertyName("time")]
    public long Time { get; set; }

    [JsonPropertyName("self_id")]
    public long SelfId { get; set; }

    [JsonPropertyName("post_type")]
    public string PostType { get; set; } = string.Empty;

    public static OneBotEvent? Parse(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("post_type", out var postType))
            return null;

        return postType.GetString() switch
        {
            "message" => JsonSerializer.Deserialize<OneBotMessageEvent>(json, options),
            "notice" => JsonSerializer.Deserialize<OneBotNoticeEvent>(json, options),
            "request" => JsonSerializer.Deserialize<OneBotRequestEvent>(json, options),
            "meta_event" => JsonSerializer.Deserialize<OneBotMetaEvent>(json, options),
            _ => JsonSerializer.Deserialize<OneBotEvent>(json, options)
        };
    }
}
