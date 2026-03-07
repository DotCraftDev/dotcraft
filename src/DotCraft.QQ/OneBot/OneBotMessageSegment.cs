using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.QQ.OneBot;

[JsonConverter(typeof(OneBotMessageSegmentConverter))]
public sealed class OneBotMessageSegment
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public JsonElement Data { get; set; }

    public static OneBotMessageSegment Text(string text)
    {
        return new OneBotMessageSegment
        {
            Type = "text",
            Data = JsonSerializer.SerializeToElement(new { text })
        };
    }

    public static OneBotMessageSegment Image(string file)
    {
        return new OneBotMessageSegment
        {
            Type = "image",
            Data = JsonSerializer.SerializeToElement(new { file })
        };
    }

    public static OneBotMessageSegment At(string qq)
    {
        return new OneBotMessageSegment
        {
            Type = "at",
            Data = JsonSerializer.SerializeToElement(new { qq })
        };
    }

    public static OneBotMessageSegment Face(int id)
    {
        return new OneBotMessageSegment
        {
            Type = "face",
            Data = JsonSerializer.SerializeToElement(new { id = id.ToString() })
        };
    }

    public static OneBotMessageSegment Reply(string id)
    {
        return new OneBotMessageSegment
        {
            Type = "reply",
            Data = JsonSerializer.SerializeToElement(new { id })
        };
    }

    public static OneBotMessageSegment Record(string file)
    {
        return new OneBotMessageSegment
        {
            Type = "record",
            Data = JsonSerializer.SerializeToElement(new { file })
        };
    }

    public static OneBotMessageSegment Video(string file)
    {
        return new OneBotMessageSegment
        {
            Type = "video",
            Data = JsonSerializer.SerializeToElement(new { file })
        };
    }

    public string? GetText()
    {
        if (Type != "text") return null;
        return Data.TryGetProperty("text", out var val) ? val.GetString() : null;
    }

    public string? GetAtQQ()
    {
        if (Type != "at") return null;
        return Data.TryGetProperty("qq", out var val) ? val.GetString() : null;
    }

    public string? GetImageUrl()
    {
        if (Type != "image") return null;
        if (Data.TryGetProperty("url", out var url)) return url.GetString();
        if (Data.TryGetProperty("file", out var file)) return file.GetString();
        return null;
    }

    public string? GetReplyId()
    {
        if (Type != "reply") return null;
        return Data.TryGetProperty("id", out var val) ? val.GetString() : null;
    }
}

internal sealed class OneBotMessageSegmentConverter : JsonConverter<OneBotMessageSegment>
{
    public override OneBotMessageSegment Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var segment = new OneBotMessageSegment();

        if (root.TryGetProperty("type", out var typeProp))
            segment.Type = typeProp.GetString() ?? string.Empty;

        if (root.TryGetProperty("data", out var dataProp))
            segment.Data = dataProp.Clone();

        return segment;
    }

    public override void Write(Utf8JsonWriter writer, OneBotMessageSegment value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", value.Type);
        writer.WritePropertyName("data");
        value.Data.WriteTo(writer);
        writer.WriteEndObject();
    }
}
