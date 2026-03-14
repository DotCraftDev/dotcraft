using DotCraft.Configuration;

namespace DotCraft.WeCom;

[ConfigSection("WeCom", DisplayName = "WeCom", Order = 210)]
public sealed class WeComConfig
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Full webhook URL including key, e.g. https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=YOUR_KEY
    /// </summary>
    [ConfigField(Sensitive = true, Hint = "Full webhook URL including key")]
    public string WebhookUrl { get; set; } = string.Empty;
}

[ConfigSection("WeComBot", DisplayName = "WeCom Bot", Order = 220)]
public sealed class WeComBotConfig
{
    /// <summary>
    /// Enable WeCom Bot service (receive messages and events)
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Host to bind the HTTP server (default: 0.0.0.0)
    /// </summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>
    /// Port to bind the HTTP server (default: 9000)
    /// </summary>
    [ConfigField(Min = 1, Max = 65535)]
    public int Port { get; set; } = 9000;

    /// <summary>
    /// List of admin user IDs (WeCom userId strings)
    /// </summary>
    [ConfigField(Hint = "JSON array of WeCom userId strings")]
    public List<string> AdminUsers { get; set; } = [];

    /// <summary>
    /// List of whitelisted user IDs (WeCom userId strings)
    /// </summary>
    [ConfigField(Hint = "JSON array of WeCom userId strings")]
    public List<string> WhitelistedUsers { get; set; } = [];

    /// <summary>
    /// List of whitelisted chat IDs
    /// </summary>
    [ConfigField(Hint = "JSON array of chat IDs")]
    public List<string> WhitelistedChats { get; set; } = [];

    /// <summary>
    /// Approval request timeout in seconds (default: 60)
    /// </summary>
    [ConfigField(Min = 0)]
    public int ApprovalTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// List of bot configurations (each bot corresponds to a path)
    /// </summary>
    [ConfigField(Hint = "JSON array of robot configs [{Path, Token, AesKey}]")]
    public List<WeComRobotConfig> Robots { get; set; } = [];

    /// <summary>
    /// Default robot configuration (for unmatched paths)
    /// </summary>
    [ConfigField(Ignore = true)]
    public WeComRobotConfig? DefaultRobot { get; set; }
}

public sealed class WeComRobotConfig
{
    /// <summary>
    /// Bot path (e.g., /dotcraft)
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Token from WeCom bot configuration
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// EncodingAESKey (43 chars without trailing '=')
    /// </summary>
    public string AesKey { get; set; } = string.Empty;
}
