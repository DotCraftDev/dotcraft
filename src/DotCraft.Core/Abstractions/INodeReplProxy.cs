namespace DotCraft.Abstractions;

/// <summary>
/// Thread-bound proxy for Desktop-hosted persistent Node REPL runtime calls.
/// Core owns the tool and wire contract; Desktop owns browser automation.
/// </summary>
public interface INodeReplProxy
{
    /// <summary>
    /// Returns whether the current thread is bound to a client that declared Node REPL and browser-use support.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Evaluates JavaScript in the Desktop Node REPL runtime for the current thread.
    /// </summary>
    Task<NodeReplEvaluateResult?> EvaluateAsync(string code, int? timeoutSeconds = null, CancellationToken ct = default);

    /// <summary>
    /// Resets the Desktop Node REPL runtime for the current thread.
    /// </summary>
    Task<bool> ResetAsync(CancellationToken ct = default);
}

public sealed class NodeReplEvaluateResult
{
    public string? Text { get; set; }

    public string? ResultText { get; set; }

    public List<NodeReplImageResult> Images { get; set; } = [];

    public List<string> Logs { get; set; } = [];

    public string? Error { get; set; }
}

public sealed class NodeReplImageResult
{
    public string MediaType { get; set; } = "image/png";

    public string DataBase64 { get; set; } = string.Empty;
}
