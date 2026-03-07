using System.Xml.Serialization;

namespace DotCraft.WeCom;

/// <summary>
/// 企业微信消息类型常量
/// </summary>
public static class WeComMsgType
{
    public const string Event = "event";
    public const string Text = "text";
    public const string Image = "image";
    public const string Attachment = "attachment";
    public const string Mixed = "mixed";
    public const string Voice = "voice";
    public const string File = "file";
}

/// <summary>
/// 企业微信事件类型常量
/// </summary>
public static class WeComEventType
{
    public const string AddToChat = "add_to_chat";
    public const string DeleteFromChat = "delete_from_chat";
    public const string EnterChat = "enter_chat";
}

/// <summary>
/// 企业微信会话类型常量
/// </summary>
public static class WeComChatType
{
    public const string Single = "single";
    public const string Group = "group";
}

/// <summary>
/// 企业微信机器人消息
/// </summary>
[XmlRoot("xml")]
public class WeComMessage
{
    [XmlElement("From")]
    public WeComFrom? From { get; set; }

    [XmlElement("WebhookUrl")]
    public string WebhookUrl { get; set; } = string.Empty;

    [XmlElement("ChatId")]
    public string ChatId { get; set; } = string.Empty;

    [XmlElement("PostId")]
    public string? PostId { get; set; }

    [XmlElement("GetChatInfoUrl")]
    public string? GetChatInfoUrl { get; set; }

    [XmlElement("MsgId")]
    public string MsgId { get; set; } = string.Empty;

    [XmlElement("ChatType")]
    public string ChatType { get; set; } = string.Empty;

    [XmlElement("MsgType")]
    public string MsgType { get; set; } = string.Empty;

    [XmlElement("Text")]
    public WeComText? Text { get; set; }

    [XmlElement("Event")]
    public WeComEvent? Event { get; set; }

    [XmlElement("Image")]
    public WeComImage? Image { get; set; }

    [XmlElement("Attachment")]
    public WeComAttachment? Attachment { get; set; }

    [XmlElement("MixedMessage")]
    public WeComMixedMessage? MixedMessage { get; set; }

    [XmlElement("Voice")]
    public WeComVoice? Voice { get; set; }

    [XmlElement("File")]
    public WeComFile? File { get; set; }

    public string? ResponseUrl { get; set; }
}

/// <summary>
/// 发送者信息
/// </summary>
public class WeComFrom
{
    [XmlElement("UserId")]
    public string UserId { get; set; } = string.Empty;

    [XmlElement("Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement("Alias")]
    public string Alias { get; set; } = string.Empty;
}

/// <summary>
/// 文本消息
/// </summary>
public class WeComText
{
    [XmlElement("Content")]
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// 事件消息
/// </summary>
public class WeComEvent
{
    [XmlElement("EventType")]
    public string EventType { get; set; } = string.Empty;
}

/// <summary>
/// 图片消息
/// </summary>
public class WeComImage
{
    [XmlElement("ImageUrl")]
    public string ImageUrl { get; set; } = string.Empty;
}

/// <summary>
/// Attachment 消息
/// </summary>
public class WeComAttachment
{
    [XmlElement("CallbackId")]
    public string CallbackId { get; set; } = string.Empty;

    [XmlElement("Actions")]
    public WeComActions? Actions { get; set; }
}

/// <summary>
/// Attachment 动作
/// </summary>
public class WeComActions
{
    [XmlElement("Name")]
    public string Name { get; set; } = string.Empty;

    [XmlElement("Value")]
    public string Value { get; set; } = string.Empty;

    [XmlElement("Type")]
    public string Type { get; set; } = string.Empty;
}

/// <summary>
/// 语音消息（智能机器人回调中语音已转文本）
/// </summary>
public class WeComVoice
{
    [XmlElement("Content")]
    public string Content { get; set; } = string.Empty;
}

/// <summary>
/// 文件消息
/// </summary>
public class WeComFile
{
    [XmlElement("Url")]
    public string Url { get; set; } = string.Empty;
}

/// <summary>
/// 图文混排消息
/// </summary>
public class WeComMixedMessage
{
    [XmlElement("MsgItem")]
    public List<WeComMsgItem> MsgItems { get; set; } = new();
}

/// <summary>
/// 图文混排消息项
/// </summary>
public class WeComMsgItem
{
    [XmlElement("MsgType")]
    public string MsgType { get; set; } = string.Empty;

    [XmlElement("Text")]
    public WeComText? Text { get; set; }

    [XmlElement("Image")]
    public WeComImage? Image { get; set; }
}
