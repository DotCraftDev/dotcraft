using DotCraft.Configuration;

namespace DotCraft.Api;

[ConfigSection("Api", DisplayName = "API", Order = 150)]
public sealed class ApiConfig
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = "127.0.0.1";

    [ConfigField(Min = 1, Max = 65535)]
    public int Port { get; set; } = 8080;

    [ConfigField(Sensitive = true)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// When true, all file and shell operations are auto-approved.
    /// When false, all such operations are auto-rejected.
    /// </summary>
    public bool AutoApprove { get; set; } = true;
}
