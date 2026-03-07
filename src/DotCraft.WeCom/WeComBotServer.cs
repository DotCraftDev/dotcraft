using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DotCraft.WeCom;

/// <summary>
/// WeCom bot HTTP server - handles URL verification and message callbacks.
/// </summary>
public class WeComBotServer(WeComBotRegistry registry, HttpClient? httpClient = null, IWeComLogger? logger = null)
{
    private readonly WeComBotRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>
    /// Map routes to an ASP.NET Core endpoint route builder.
    /// </summary>
    public void MapRoutes(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/{*path}", HandleVerifyUrl);
        endpoints.MapPost("/{*path}", HandleMessage);
    }

    private async Task HandleVerifyUrl(HttpContext context)
    {
        var path = context.GetRouteValue("path")?.ToString() ?? "";
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }
        var msgSignature = context.Request.Query["msg_signature"].ToString();
        var timestamp = context.Request.Query["timestamp"].ToString();
        var nonce = context.Request.Query["nonce"].ToString();
        var echoStr = context.Request.Query["echostr"].ToString();

        logger?.LogInformation("[WeComBot] URL verification: path={Path}", path);

        if (string.IsNullOrEmpty(msgSignature) || string.IsNullOrEmpty(timestamp) ||
            string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(echoStr))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Missing parameters");
            return;
        }

        var crypt = _registry.GetCrypt(path);
        if (crypt == null)
        {
            logger?.LogWarning("[WeComBot] Bot not found: {Path}", path);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsync("Bot not found");
            return;
        }

        try
        {
            var result = crypt.VerifyUrl(msgSignature, timestamp, nonce, echoStr);
            logger?.LogInformation("[WeComBot] URL verified: path={Path}", path);
            await context.Response.WriteAsync(result);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[WeComBot] URL verification failed: path={Path}", path);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync($"Verification failed: {ex.Message}");
        }
    }

    private async Task HandleMessage(HttpContext context)
    {
        var path = context.GetRouteValue("path")?.ToString() ?? "";
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }
        var msgSignature = context.Request.Query["msg_signature"].ToString();
        var timestamp = context.Request.Query["timestamp"].ToString();
        var nonce = context.Request.Query["nonce"].ToString();

        logger?.LogInformation("[WeComBot] Message received: path={Path}", path);

        if (string.IsNullOrEmpty(msgSignature) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(nonce))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Missing parameters");
            return;
        }

        var crypt = _registry.GetCrypt(path);
        if (crypt == null)
        {
            logger?.LogWarning("[WeComBot] Bot not found: {Path}", path);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var handlers = _registry.GetHandlers(path);
        if (handlers == null || !handlers.HasAnyHandler)
        {
            logger?.LogWarning("[WeComBot] No handlers registered: {Path}", path);
            context.Response.StatusCode = StatusCodes.Status200OK;
            return;
        }

        try
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();

            var plainText = crypt.DecryptMsg(msgSignature, timestamp, nonce, body);

            var message = ParseMessage(plainText);
            if (message == null)
            {
                logger?.LogWarning("[WeComBot] Failed to parse message");
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            logger?.LogInformation("[WeComBot] Parsed: ChatId={ChatId}, MsgType={MsgType}, From={From}",
                message.ChatId, message.MsgType, message.From?.Alias ?? "unknown");

            if (!ValidateMessage(message))
            {
                logger?.LogWarning("[WeComBot] Message validation failed");
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            // Event messages need synchronous reply
            if (message.MsgType == WeComMsgType.Event)
            {
                await HandleEventMessage(context, message, handlers, crypt, timestamp, nonce);
                return;
            }

            // Other messages: return 200 immediately, process asynchronously
            context.Response.StatusCode = StatusCodes.Status200OK;
            await context.Response.CompleteAsync();

            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessMessage(message, handlers);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "[WeComBot] Message processing error");
                }
            });
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[WeComBot] Failed to handle message");
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
    }

    private async Task HandleEventMessage(HttpContext context, WeComMessage message, WeComHandlers handlers,
        WeComBizMsgCrypt crypt, string timestamp, string nonce)
    {
        if (handlers.EventHandler == null)
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return;
        }

        _registry.CacheWebhookUrl(message.ChatId, message.WebhookUrl);

        var pusher = new WeComPusher(message.ChatId, message.WebhookUrl, _httpClient);
        var responseText = await handlers.EventHandler(
            message.Event?.EventType ?? "",
            message.ChatType,
            message.From ?? new WeComFrom(),
            pusher);

        if (string.IsNullOrEmpty(responseText))
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            return;
        }

        var encryptedResponse = crypt.EncryptMsg(responseText, timestamp, nonce);
        context.Response.ContentType = "application/xml";
        await context.Response.WriteAsync(encryptedResponse);
    }

    private async Task ProcessMessage(WeComMessage message, WeComHandlers handlers)
    {
        var webhookUrl = !string.IsNullOrEmpty(message.WebhookUrl) ? message.WebhookUrl
            : !string.IsNullOrEmpty(message.ResponseUrl) ? message.ResponseUrl
            : "";
        _registry.CacheWebhookUrl(message.ChatId, webhookUrl);
        var pusher = new WeComPusher(message.ChatId, webhookUrl, _httpClient);

        if (message.MsgType == WeComMsgType.Voice && handlers.TextHandler != null)
        {
            var content = NormalizeContent(message.Voice?.Content ?? "");
            var parameters = ParseParameters(content, message.ChatType);
            await handlers.TextHandler(parameters, message.From ?? new WeComFrom(), pusher);
        }
        else if (message.MsgType == WeComMsgType.Text && handlers.TextHandler != null)
        {
            var content = NormalizeContent(message.Text?.Content ?? "");
            var parameters = ParseParameters(content, message.ChatType);
            await handlers.TextHandler(parameters, message.From ?? new WeComFrom(), pusher);
        }
        else if (handlers.CommonHandler != null)
        {
            await handlers.CommonHandler(message, pusher);
        }
    }

    private WeComMessage? ParseMessage(string content)
    {
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith('{'))
            return ParseJsonMessage(trimmed);
        return ParseXmlMessage(trimmed);
    }

    private WeComMessage? ParseXmlMessage(string xml)
    {
        try
        {
            var serializer = new XmlSerializer(typeof(WeComMessage));
            using var stringReader = new StringReader(xml);
            return serializer.Deserialize(stringReader) as WeComMessage;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[WeComBot] XML parse failed");
            return null;
        }
    }

    private WeComMessage? ParseJsonMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var message = new WeComMessage
            {
                MsgId = root.TryGetProperty("msgid", out var msgid) ? msgid.GetString() ?? "" : "",
                ChatType = root.TryGetProperty("chattype", out var chattype) ? chattype.GetString() ?? "" : "",
                MsgType = root.TryGetProperty("msgtype", out var msgtype) ? msgtype.GetString() ?? "" : "",
                ResponseUrl = root.TryGetProperty("response_url", out var respUrl) ? respUrl.GetString() : null
            };

            if (root.TryGetProperty("chatid", out var chatid))
                message.ChatId = chatid.GetString() ?? "";

            if (root.TryGetProperty("from", out var from))
            {
                message.From = new WeComFrom
                {
                    UserId = from.TryGetProperty("userid", out var uid) ? uid.GetString() ?? "" : "",
                    Name = from.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    Alias = from.TryGetProperty("alias", out var alias) ? alias.GetString() ?? ""
                        : from.TryGetProperty("userid", out var uid2) ? uid2.GetString() ?? "" : ""
                };
            }

            if (root.TryGetProperty("webhook_url", out var webhookUrl))
                message.WebhookUrl = webhookUrl.GetString() ?? "";

            switch (message.MsgType)
            {
                case WeComMsgType.Text:
                    if (root.TryGetProperty("text", out var textObj) &&
                        textObj.TryGetProperty("content", out var textContent))
                        message.Text = new WeComText { Content = textContent.GetString() ?? "" };
                    break;

                case WeComMsgType.Image:
                    if (root.TryGetProperty("image", out var imageObj) &&
                        imageObj.TryGetProperty("url", out var imageUrl))
                        message.Image = new WeComImage { ImageUrl = imageUrl.GetString() ?? "" };
                    break;

                case WeComMsgType.Voice:
                    if (root.TryGetProperty("voice", out var voiceObj) &&
                        voiceObj.TryGetProperty("content", out var voiceContent))
                        message.Voice = new WeComVoice { Content = voiceContent.GetString() ?? "" };
                    break;

                case WeComMsgType.File:
                    if (root.TryGetProperty("file", out var fileObj) &&
                        fileObj.TryGetProperty("url", out var fileUrl))
                        message.File = new WeComFile { Url = fileUrl.GetString() ?? "" };
                    break;

                case WeComMsgType.Mixed:
                    if (root.TryGetProperty("mixed", out var mixedObj) &&
                        mixedObj.TryGetProperty("msg_item", out var items))
                    {
                        var mixedMessage = new WeComMixedMessage();
                        foreach (var item in items.EnumerateArray())
                        {
                            var itemType = item.TryGetProperty("msgtype", out var it) ? it.GetString() ?? "" : "";
                            var msgItem = new WeComMsgItem { MsgType = itemType };

                            if (itemType == "text" && item.TryGetProperty("text", out var itemText) &&
                                itemText.TryGetProperty("content", out var itemTextContent))
                                msgItem.Text = new WeComText { Content = itemTextContent.GetString() ?? "" };

                            if (itemType == "image" && item.TryGetProperty("image", out var itemImage) &&
                                itemImage.TryGetProperty("url", out var itemImageUrl))
                                msgItem.Image = new WeComImage { ImageUrl = itemImageUrl.GetString() ?? "" };

                            mixedMessage.MsgItems.Add(msgItem);
                        }
                        message.MixedMessage = mixedMessage;
                    }
                    break;
            }

            return message;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "[WeComBot] JSON parse failed");
            return null;
        }
    }

    private static bool ValidateMessage(WeComMessage message)
    {
        return message.MsgType switch
        {
            WeComMsgType.Text => message.Text != null,
            WeComMsgType.Image => message.Image != null,
            WeComMsgType.Event => message.Event != null,
            WeComMsgType.Attachment => message.Attachment != null,
            WeComMsgType.Mixed => message.MixedMessage != null,
            WeComMsgType.Voice => message.Voice != null,
            WeComMsgType.File => message.File != null,
            _ => false
        };
    }

    private static string NormalizeContent(string content)
    {
        if (string.IsNullOrEmpty(content))
            return "";
        content = content.Replace("\r", " ").Replace("\n", " ");
        content = Regex.Replace(content, @"\s+", " ");
        return content.Trim();
    }

    private static string[] ParseParameters(string content, string chatType)
    {
        var parts = content.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // In group chat, skip the leading @mention
        if (chatType == WeComChatType.Group && parts.Length > 0 && parts[0].StartsWith('@'))
            return parts.Skip(1).ToArray();

        return parts;
    }
}

/// <summary>
/// Logger interface for WeComBotServer (named to avoid conflict with Microsoft.Extensions.Logging.ILogger).
/// </summary>
public interface IWeComLogger
{
    void LogInformation(string message, params object[] args);
    void LogWarning(string message, params object[] args);
    void LogError(Exception? exception, string message, params object[] args);
}
