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

    public bool AutoApprove { get; set; } = true;

    /// <summary>
    /// Approval mode for sensitive operations in API mode.
    /// "auto" = auto-approve all operations (default, same as AutoApprove=true).
    /// "reject" = auto-reject all operations (same as AutoApprove=false).
    /// "interactive" = pause and wait for approval via /v1/approvals endpoint (Human-in-the-Loop).
    /// When set, takes precedence over AutoApprove.
    /// </summary>
    [ConfigField(FieldType = "select", Options = ["", "auto", "reject", "interactive"], Hint = "When set, takes precedence over AutoApprove")]
    public string ApprovalMode { get; set; } = string.Empty;

    /// <summary>
    /// Timeout in seconds for interactive approval requests (default: 120).
    /// If no approval decision is received within this time, the operation is rejected.
    /// Only applies when ApprovalMode is "interactive".
    /// </summary>
    [ConfigField(Min = 0)]
    public int ApprovalTimeoutSeconds { get; set; } = 120;
}
