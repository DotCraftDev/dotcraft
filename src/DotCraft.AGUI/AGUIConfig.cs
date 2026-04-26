using DotCraft.Configuration;

namespace DotCraft.Agui;

[ConfigSection("AgUi", DisplayName = "AG-UI", Order = 160)]
public sealed class AguiConfig
{
    /// <summary>
    /// Enable AG-UI protocol server channel.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// AG-UI endpoint path (default: /ag-ui).
    /// </summary>
    [ConfigField(Hint = "Default: /ag-ui")]
    public string Path { get; set; } = "/ag-ui";

    /// <summary>
    /// Host to bind the AG-UI HTTP server (default: 127.0.0.1).
    /// </summary>
    [ConfigField(Hint = "Bind address, default 127.0.0.1")]
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// Port to bind the AG-UI HTTP server (default: 5100).
    /// </summary>
    [ConfigField(Min = 1, Max = 65535)]
    public int Port { get; set; } = 5100;

    /// <summary>
    /// When true, require Bearer API key for AG-UI requests.
    /// </summary>
    public bool RequireAuth { get; set; }

    /// <summary>
    /// API key for AG-UI when RequireAuth is true.
    /// </summary>
    [ConfigField(Sensitive = true, Hint = "Required when RequireAuth is on")]
    public string? ApiKey { get; set; }

    /// <summary>
    /// Approval mode for sensitive tool operations: "interactive" (request frontend approval, default)
    /// or "auto" (auto-approve all, matches legacy behavior).
    /// </summary>
    [ConfigField(FieldType = "select", Options = ["interactive", "auto"])]
    public string ApprovalMode { get; set; } = "interactive";
}
