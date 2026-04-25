namespace DotCraft.Abstractions;

/// <summary>
/// Thread-bound proxy for Desktop-hosted browser-use runtime calls.
/// Core owns the tool and wire contract; Desktop owns browser automation.
/// </summary>
public interface IBrowserUseProxy
{
    /// <summary>
    /// Returns whether the current thread is bound to a client that declared browser-use support.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Evaluates JavaScript in the Desktop browser-use runtime for the current thread.
    /// </summary>
    Task<BrowserUseEvaluateResult?> EvaluateAsync(string code, int? timeoutSeconds = null, CancellationToken ct = default);

    /// <summary>
    /// Resets the Desktop browser-use runtime for the current thread.
    /// </summary>
    Task<bool> ResetAsync(CancellationToken ct = default);
}

public sealed class BrowserUseEvaluateResult
{
    public string? Text { get; set; }

    public string? ResultText { get; set; }

    public List<BrowserUseImageResult> Images { get; set; } = [];

    public List<string> Logs { get; set; } = [];

    public string? Error { get; set; }
}

public sealed class BrowserUseImageResult
{
    public string MediaType { get; set; } = "image/png";

    public string DataBase64 { get; set; } = string.Empty;
}
