using DotCraft.Configuration;

namespace DotCraft.QQ;

[ConfigSection("QQBot", DisplayName = "QQ Bot", Order = 200)]
public sealed class QQBotConfig
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = "127.0.0.1";

    [ConfigField(Min = 1, Max = 65535)]
    public int Port { get; set; } = 6700;

    [ConfigField(Sensitive = true)]
    public string AccessToken { get; set; } = string.Empty;

    [ConfigField(Hint = "JSON array of QQ user IDs (numbers)")]
    public List<long> AdminUsers { get; set; } = [];

    [ConfigField(Hint = "JSON array of QQ user IDs")]
    public List<long> WhitelistedUsers { get; set; } = [];

    [ConfigField(Hint = "JSON array of QQ group IDs")]
    public List<long> WhitelistedGroups { get; set; } = [];

    [ConfigField(Min = 0)]
    public int ApprovalTimeoutSeconds { get; set; } = 60;
}
