using System.ComponentModel;
using System.Text;
using System.Text.Json;
using DotCraft.Tools;

namespace DotCraft.WeCom;

/// <summary>
/// WeCom (企业微信) group bot webhook tools.
/// Supports sending text messages to WeCom group chats via webhook.
/// </summary>
public sealed class WeComTools(string webhookUrl, HttpClient? httpClient = null)
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// Send a text message to the configured WeCom group via webhook.
    /// This is the public API for other modules (Heartbeat, Cron, etc.) to call directly.
    /// </summary>
    public async Task<string> SendTextAsync(string content, List<string>? mentionedList = null, List<string>? mentionedMobileList = null)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return JsonSerializer.Serialize(new { error = "WeCom webhook URL is not configured." });

        if (string.IsNullOrWhiteSpace(content))
            return JsonSerializer.Serialize(new { error = "Message content cannot be empty." });

        var chunks = WeComMessageSplitter.Split(content, WeComMessageSplitter.TextMaxBytes);
        string lastResult = string.Empty;

        for (var i = 0; i < chunks.Count; i++)
        {
            if (i > 0)
                await Task.Delay(WeComMessageSplitter.InterChunkDelayMs);

            var textObj = new Dictionary<string, object> { ["content"] = chunks[i] };
            if (i == 0 && mentionedList is { Count: > 0 })
                textObj["mentioned_list"] = mentionedList;
            if (i == 0 && mentionedMobileList is { Count: > 0 })
                textObj["mentioned_mobile_list"] = mentionedMobileList;

            var payload = new Dictionary<string, object>
            {
                ["msgtype"] = "text",
                ["text"] = textObj
            };

            lastResult = await PostWebhookAsync(payload);
        }

        return lastResult;
    }

    /// <summary>
    /// AI tool method: send a text notification to WeCom group chat.
    /// </summary>
    [Description("Send a text notification to WeCom (企业微信) group chat via webhook. Use this to notify the team about task completion, important findings, or alerts. Rate limit: 20 messages/minute.")]
    [Tool(Icon = "📨", DisplayType = typeof(WeComToolDisplays), DisplayMethod = nameof(WeComToolDisplays.WeComNotify))]
    public async Task<string> WeComNotify(
        [Description("The text message content to send (max 2048 bytes UTF-8).")] string message,
        [Description("Optional: comma-separated list of user IDs to @mention, use '@all' to mention everyone.")] string? mentionList = null)
    {
        List<string>? mentions = null;
        if (!string.IsNullOrWhiteSpace(mentionList))
        {
            mentions = mentionList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }

        return await SendTextAsync(message, mentions);
    }

    private async Task<string> PostWebhookAsync(Dictionary<string, object> payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            using var httpContent = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(webhookUrl, httpContent);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return JsonSerializer.Serialize(new { error = $"HTTP {(int)response.StatusCode}", detail = responseBody });

            // Parse WeCom response to check errcode
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (root.TryGetProperty("errcode", out var errcode) && errcode.GetInt32() != 0)
            {
                var errmsg = root.TryGetProperty("errmsg", out var msg) ? msg.GetString() : "unknown error";
                return JsonSerializer.Serialize(new { error = $"WeCom API error: {errmsg}", errcode = errcode.GetInt32() });
            }

            return JsonSerializer.Serialize(new { success = true, message = "Message sent successfully." });
        }
        catch (TaskCanceledException)
        {
            return JsonSerializer.Serialize(new { error = "Request timed out." });
        }
        catch (HttpRequestException ex)
        {
            return JsonSerializer.Serialize(new { error = $"HTTP request failed: {ex.Message}" });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [Description("Send a voice message in the current WeCom chat (WeCom Bot mode only). ONLY supports AMR format. For other audio formats (mp3/wav/etc.), use WeComSendFile instead.")]
    [Tool(Icon = "🎤", DisplayType = typeof(WeComToolDisplays), DisplayMethod = nameof(WeComToolDisplays.WeComSendVoice))]
    public async Task<string> WeComSendVoice(
        [Description("Local absolute path to the voice file. MUST be .amr format.")] string filePath)
    {
        var pusher = WeComPusherScope.Current;
        if (pusher == null)
            return JsonSerializer.Serialize(new { error = "WeCom bot context not available (no current chat)." });

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return JsonSerializer.Serialize(new { error = "Voice file does not exist." });

        if (!filePath.EndsWith(".amr", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { error = "WeCom voice messages only support AMR format. For other audio formats (mp3/wav/etc.), please use WeComSendFile instead." });

        try
        {
            await using var fs = File.OpenRead(filePath);
            var mediaId = await pusher.UploadMediaAsync(fs, Path.GetFileName(filePath), "voice");
            await pusher.PushVoiceAsync(mediaId);
            return JsonSerializer.Serialize(new { success = true, message = "Voice sent." });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    [Description("Send a file in the current WeCom chat (WeCom Bot mode only). The file must be a local absolute path.")]
    [Tool(Icon = "📁", DisplayType = typeof(WeComToolDisplays), DisplayMethod = nameof(WeComToolDisplays.WeComSendFile))]
    public async Task<string> WeComSendFile(
        [Description("Local absolute path to the file to send.")] string filePath)
    {
        var pusher = WeComPusherScope.Current;
        if (pusher == null)
            return JsonSerializer.Serialize(new { error = "WeCom bot context not available (no current chat)." });

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return JsonSerializer.Serialize(new { error = "File does not exist." });

        try
        {
            await using var fs = File.OpenRead(filePath);
            var mediaId = await pusher.UploadMediaAsync(fs, Path.GetFileName(filePath), "file");
            await pusher.PushFileAsync(mediaId);
            return JsonSerializer.Serialize(new { success = true, message = "File sent." });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}
