using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.QQ.OneBot;

public sealed class OneBotActionResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("retcode")]
    public int RetCode { get; set; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    [JsonPropertyName("echo")]
    public string Echo { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    public bool IsOk => Status == "ok" || RetCode == 0;
    
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public T? GetData<T>()
    {
        if (Data == null || Data.Value.ValueKind == JsonValueKind.Null)
            return default;

        return JsonSerializer.Deserialize<T>(Data.Value.GetRawText(), SerializerOptions);
    }

    public static OneBotActionResponse? Parse(string json)
    {
        return JsonSerializer.Deserialize<OneBotActionResponse>(json, SerializerOptions);
    }
}
