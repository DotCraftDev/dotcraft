using System.Text.Json.Serialization;

namespace DotCraft.QQ.OneBot;

public sealed class OneBotSender
{
    [JsonPropertyName("user_id")]
    public long UserId { get; set; }

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = string.Empty;

    [JsonPropertyName("card")]
    public string Card { get; set; } = string.Empty;

    [JsonPropertyName("sex")]
    public string Sex { get; set; } = string.Empty;

    [JsonPropertyName("age")]
    public int Age { get; set; }

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    public string DisplayName => !string.IsNullOrEmpty(Card) ? Card : Nickname;
}
