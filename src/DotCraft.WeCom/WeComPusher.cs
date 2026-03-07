using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotCraft.WeCom;

/// <summary>
/// 企业微信消息推送接口
/// </summary>
public interface IWeComPusher
{
    /// <summary>
    /// 推送文本消息
    /// </summary>
    Task PushTextAsync(string content, List<string>? mentionedList = null, List<string>? mentionedMobileList = null, List<string>? visibleToUser = null);

    /// <summary>
    /// 推送 Markdown 消息
    /// </summary>
    Task PushMarkdownAsync(string content, List<string>? visibleToUser = null);

    /// <summary>
    /// 推送图片消息
    /// </summary>
    Task PushImageAsync(byte[] imageData, List<string>? visibleToUser = null);

    /// <summary>
    /// 推送图文消息
    /// </summary>
    Task PushNewsAsync(List<WeComArticle> articles, List<string>? visibleToUser = null);

    /// <summary>
    /// 推送小程序卡片
    /// </summary>
    Task PushMiniProgramAsync(string title, string picMediaId, string appId, string page);

    /// <summary>
    /// 推送语音消息（需先通过 UploadMediaAsync 获取 media_id）
    /// </summary>
    Task PushVoiceAsync(string mediaId);

    /// <summary>
    /// 推送文件消息（需先通过 UploadMediaAsync 获取 media_id）
    /// </summary>
    Task PushFileAsync(string mediaId);

    /// <summary>
    /// 上传临时素材，返回 media_id
    /// </summary>
    Task<string> UploadMediaAsync(Stream fileStream, string filename, string type);

    /// <summary>
    /// 推送原始 JSON
    /// </summary>
    Task PushRawAsync(string jsonData);

    /// <summary>
    /// 获取 ChatId
    /// </summary>
    string GetChatId();
}

/// <summary>
/// 企业微信消息推送器实现
/// </summary>
public class WeComPusher(string chatId, string webhookUrl, HttpClient httpClient) : IWeComPusher
{
    private readonly string _chatId = chatId ?? throw new ArgumentNullException(nameof(chatId));
    
    private readonly string _webhookUrl = webhookUrl ?? throw new ArgumentNullException(nameof(webhookUrl));
    
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public string GetChatId() => _chatId;

    public async Task PushTextAsync(string content, List<string>? mentionedList = null,
        List<string>? mentionedMobileList = null, List<string>? visibleToUser = null)
    {
        var visibleToUserStr = visibleToUser != null ? string.Join("|", visibleToUser) : null;
        var chunks = WeComMessageSplitter.Split(content, WeComMessageSplitter.TextMaxBytes);

        for (var i = 0; i < chunks.Count; i++)
        {
            if (i > 0)
                await Task.Delay(WeComMessageSplitter.InterChunkDelayMs);

            var message = new WeComTextMessage
            {
                ChatId = _chatId,
                MsgType = "text",
                Text = new WeComTextContent
                {
                    Content = chunks[i],
                    MentionedList = i == 0 ? mentionedList : null,
                    MentionedMobileList = i == 0 ? mentionedMobileList : null
                },
                VisibleToUser = visibleToUserStr
            };

            await PostAsync(message);
        }
    }

    public async Task PushMarkdownAsync(string content, List<string>? visibleToUser = null)
    {
        var visibleToUserStr = visibleToUser != null ? string.Join("|", visibleToUser) : null;
        var chunks = WeComMessageSplitter.Split(content, WeComMessageSplitter.MarkdownMaxBytes);

        for (var i = 0; i < chunks.Count; i++)
        {
            if (i > 0)
                await Task.Delay(WeComMessageSplitter.InterChunkDelayMs);

            var message = new WeComMarkdownMessage
            {
                ChatId = _chatId,
                MsgType = "markdown",
                Markdown = new WeComMarkdownContent { Content = chunks[i] },
                VisibleToUser = visibleToUserStr
            };

            await PostAsync(message);
        }
    }

    public async Task PushImageAsync(byte[] imageData, List<string>? visibleToUser = null)
    {
        var base64 = Convert.ToBase64String(imageData);
        var md5 = ComputeMd5(imageData);

        var message = new WeComImageMessage
        {
            ChatId = _chatId,
            MsgType = "image",
            Image = new WeComImageContent { Base64 = base64, Md5 = md5 },
            VisibleToUser = visibleToUser != null ? string.Join("|", visibleToUser) : null
        };

        await PostAsync(message);
    }

    public async Task PushNewsAsync(List<WeComArticle> articles, List<string>? visibleToUser = null)
    {
        var message = new WeComNewsMessage
        {
            ChatId = _chatId,
            MsgType = "news",
            News = new WeComNewsContent { Articles = articles },
            VisibleToUser = visibleToUser != null ? string.Join("|", visibleToUser) : null
        };

        await PostAsync(message);
    }

    public async Task PushMiniProgramAsync(string title, string picMediaId, string appId, string page)
    {
        var message = new WeComMiniProgramMessage
        {
            ChatId = _chatId,
            MsgType = "miniprogram",
            Miniprogram = new WeComMiniProgramContent
            {
                Title = title,
                PicMediaId = picMediaId,
                AppId = appId,
                Page = page
            }
        };

        await PostAsync(message);
    }

    public async Task PushVoiceAsync(string mediaId)
    {
        var message = new WeComVoiceMessage
        {
            ChatId = _chatId,
            MsgType = "voice",
            Voice = new WeComVoiceContent { MediaId = mediaId }
        };

        await PostAsync(message);
    }

    public async Task PushFileAsync(string mediaId)
    {
        var message = new WeComFileMessage
        {
            ChatId = _chatId,
            MsgType = "file",
            File = new WeComFileContent { MediaId = mediaId }
        };

        await PostAsync(message);
    }

    public async Task<string> UploadMediaAsync(Stream fileStream, string filename, string type)
    {
        var uploadUrl = BuildUploadUrl(type);

        // WeCom upload_media API requires Content-Disposition to include filelength parameter.
        // Standard .NET MultipartFormDataContent does not add filelength, so we set it manually.
        using var ms = new MemoryStream();
        await fileStream.CopyToAsync(ms);
        var fileBytes = ms.ToArray();

        // Manually build multipart/form-data to exactly match WeCom's expected format.
        var boundary = $"----DotCraft{Guid.NewGuid():N}";
        var sb = new StringBuilder();
        sb.Append($"--{boundary}\r\n");
        sb.Append($"Content-Disposition: form-data; name=\"media\"; filename=\"{filename}\"; filelength={fileBytes.Length}\r\n");
        sb.Append("Content-Type: application/octet-stream\r\n");
        sb.Append("\r\n");

        var headerBytes = Encoding.UTF8.GetBytes(sb.ToString());
        var footerBytes = Encoding.UTF8.GetBytes($"\r\n--{boundary}--\r\n");

        var body = new byte[headerBytes.Length + fileBytes.Length + footerBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, body, 0, headerBytes.Length);
        Buffer.BlockCopy(fileBytes, 0, body, headerBytes.Length, fileBytes.Length);
        Buffer.BlockCopy(footerBytes, 0, body, headerBytes.Length + fileBytes.Length, footerBytes.Length);

        using var content = new ByteArrayContent(body);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");

        var response = await _httpClient.PostAsync(uploadUrl, content);
        var json = await response.Content.ReadAsStringAsync();

        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("errcode", out var errcode) && errcode.GetInt32() != 0)
        {
            var errmsg = root.TryGetProperty("errmsg", out var msg) ? msg.GetString() : "unknown";
            throw new Exception($"上传素材失败: {errmsg}");
        }

        if (root.TryGetProperty("media_id", out var mediaIdProp))
            return mediaIdProp.GetString() ?? throw new Exception("上传素材返回的 media_id 为空");

        throw new Exception("上传素材响应中未找到 media_id");
    }

    public async Task PushRawAsync(string jsonData)
    {
        var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
        await PostInternalAsync(content);
    }

    private async Task PostAsync(object message)
    {
        var json = JsonSerializer.Serialize(message, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        await PostInternalAsync(content);
    }

    private async Task PostInternalAsync(HttpContent content)
    {
        try
        {
            var response = await _httpClient.PostAsync(_webhookUrl, content);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            throw new Exception($"推送消息失败: {ex.Message}", ex);
        }
    }

    private static string ComputeMd5(byte[] data)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(data);
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    private string BuildUploadUrl(string type)
    {
        var uri = new Uri(_webhookUrl);
        var keyParam = System.Web.HttpUtility.ParseQueryString(uri.Query)["key"];
        if (string.IsNullOrEmpty(keyParam))
            throw new Exception("无法从 WebhookUrl 中提取 key 参数");

        return $"{uri.Scheme}://{uri.Host}/cgi-bin/webhook/upload_media?key={keyParam}&type={type}";
    }
}

internal class WeComTextMessage
{
    [JsonPropertyName("chatid")]
    public string? ChatId { get; set; }

    [JsonPropertyName("msgtype")]
    public string? MsgType { get; set; }

    [JsonPropertyName("text")]
    public WeComTextContent? Text { get; set; }

    [JsonPropertyName("visible_to_user")]
    public string? VisibleToUser { get; set; }
}

internal class WeComTextContent
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("mentioned_list")]
    public List<string>? MentionedList { get; set; }

    [JsonPropertyName("mentioned_mobile_list")]
    public List<string>? MentionedMobileList { get; set; }
}

internal class WeComMarkdownMessage
{
    [JsonPropertyName("chatid")]
    public string? ChatId { get; set; }

    [JsonPropertyName("msgtype")]
    public string? MsgType { get; set; }

    [JsonPropertyName("markdown")]
    public WeComMarkdownContent? Markdown { get; set; }

    [JsonPropertyName("visible_to_user")]
    public string? VisibleToUser { get; set; }
}

internal class WeComMarkdownContent
{
    [JsonPropertyName("content")]
    public string? Content { get; set; }
}

internal class WeComImageMessage
{
    [JsonPropertyName("chatid")]
    public string? ChatId { get; set; }

    [JsonPropertyName("msgtype")]
    public string? MsgType { get; set; }

    [JsonPropertyName("image")]
    public WeComImageContent? Image { get; set; }

    [JsonPropertyName("visible_to_user")]
    public string? VisibleToUser { get; set; }
}

internal class WeComImageContent
{
    [JsonPropertyName("base64")]
    public string? Base64 { get; set; }

    [JsonPropertyName("md5")]
    public string? Md5 { get; set; }
}

internal class WeComNewsMessage
{
    [JsonPropertyName("chatid")]
    public string? ChatId { get; set; }

    [JsonPropertyName("msgtype")]
    public string? MsgType { get; set; }

    [JsonPropertyName("news")]
    public WeComNewsContent? News { get; set; }

    [JsonPropertyName("visible_to_user")]
    public string? VisibleToUser { get; set; }
}

internal class WeComNewsContent
{
    [JsonPropertyName("articles")]
    public List<WeComArticle>? Articles { get; set; }
}

public class WeComArticle
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("picurl")]
    public string? PicUrl { get; set; }
}

internal class WeComMiniProgramMessage
{
    [JsonPropertyName("chatid")]
    public string? ChatId { get; set; }

    [JsonPropertyName("msgtype")]
    public string? MsgType { get; set; }

    [JsonPropertyName("miniprogram")]
    public WeComMiniProgramContent? Miniprogram { get; set; }
}

internal class WeComMiniProgramContent
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("pic_media_id")]
    public string? PicMediaId { get; set; }

    [JsonPropertyName("appid")]
    public string? AppId { get; set; }

    [JsonPropertyName("page")]
    public string? Page { get; set; }
}

internal class WeComVoiceMessage
{
    [JsonPropertyName("chatid")]
    public string? ChatId { get; set; }

    [JsonPropertyName("msgtype")]
    public string? MsgType { get; set; }

    [JsonPropertyName("voice")]
    public WeComVoiceContent? Voice { get; set; }
}

internal class WeComVoiceContent
{
    [JsonPropertyName("media_id")]
    public string? MediaId { get; set; }
}

internal class WeComFileMessage
{
    [JsonPropertyName("chatid")]
    public string? ChatId { get; set; }

    [JsonPropertyName("msgtype")]
    public string? MsgType { get; set; }

    [JsonPropertyName("file")]
    public WeComFileContent? File { get; set; }
}

internal class WeComFileContent
{
    [JsonPropertyName("media_id")]
    public string? MediaId { get; set; }
}
