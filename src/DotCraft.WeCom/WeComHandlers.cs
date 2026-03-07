namespace DotCraft.WeCom;

/// <summary>
/// Text message handler delegate.
/// </summary>
/// <param name="parameters">Tokenized message content</param>
/// <param name="from">Sender info</param>
/// <param name="pusher">Message pusher</param>
public delegate Task TextMessageHandler(string[] parameters, WeComFrom from, IWeComPusher pusher);

/// <summary>
/// Common message handler delegate (for non-text messages).
/// </summary>
/// <param name="message">WeCom message object</param>
/// <param name="pusher">Message pusher</param>
public delegate Task CommonMessageHandler(WeComMessage message, IWeComPusher pusher);

/// <summary>
/// Event message handler delegate.
/// </summary>
/// <param name="eventType">Event type</param>
/// <param name="chatType">Chat type</param>
/// <param name="from">Sender info</param>
/// <param name="pusher">Message pusher</param>
/// <returns>Non-null string to reply synchronously via encrypted XML</returns>
public delegate Task<string?> EventMessageHandler(string eventType, string chatType, WeComFrom from, IWeComPusher pusher);

/// <summary>
/// Collection of message handlers for a bot path.
/// </summary>
public class WeComHandlers
{
    /// <summary>
    /// Text message handler (takes priority over CommonHandler for text messages).
    /// </summary>
    public TextMessageHandler? TextHandler { get; set; }

    /// <summary>
    /// Common message handler (text, image, attachment, mixed).
    /// </summary>
    public CommonMessageHandler? CommonHandler { get; set; }

    /// <summary>
    /// Event message handler (add_to_chat, delete_from_chat, enter_chat).
    /// </summary>
    public EventMessageHandler? EventHandler { get; set; }

    /// <summary>
    /// Whether any handler delegate has been set.
    /// </summary>
    public bool HasAnyHandler => TextHandler != null || CommonHandler != null || EventHandler != null;
}
