using System.Text.Json;
using DotCraft.Abstractions;

namespace DotCraft.Acp;

/// <summary>
/// Proxy for making Agent→Client JSON-RPC method calls.
/// Provides typed wrappers for fs and terminal operations.
/// </summary>
public sealed class AcpClientProxy : IAcpExtensionProxy
{
    private readonly AcpTransport _transport;
    private volatile ClientCapabilities? _capabilities;

    /// <summary>
    /// Shared JSON serialization options (camelCase, case-insensitive).
    /// Reused by extension tool providers to avoid duplicating the configuration.
    /// </summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public AcpClientProxy(AcpTransport transport, ClientCapabilities? capabilities)
    {
        _transport = transport;
        _capabilities = capabilities;
    }

    /// <summary>
    /// Gets or sets the client capabilities.
    /// Thread-safe: the backing field is volatile so readers always see the latest value
    /// published by the writer (typically the ACP initialize handler).
    /// </summary>
    public ClientCapabilities? Capabilities
    {
        get => _capabilities;
        set => _capabilities = value;
    }

    /// <summary>
    /// Gets the list of extension method prefixes supported by the client
    /// (e.g. ["_unity"]). Empty when the client has not advertised any extensions.
    /// </summary>
    public IReadOnlyList<string> Extensions => _capabilities?.Extensions ?? [];

    public bool SupportsFileRead => _capabilities?.Fs?.ReadTextFile == true;
    public bool SupportsFileWrite => _capabilities?.Fs?.WriteTextFile == true;
    public bool SupportsTerminal => _capabilities?.Terminal?.Create == true;

    // ───── File system operations ─────

    /// <summary>
    /// Reads a text file via the client. Returns file content including unsaved editor changes.
    /// </summary>
    public async Task<string?> ReadTextFileAsync(string path, int? offset = null, int? limit = null, CancellationToken ct = default)
    {
        if (!SupportsFileRead) return null;

        var result = await _transport.SendClientRequestAsync(AcpMethods.FsReadTextFile,
            new FsReadTextFileParams { Path = path, Offset = offset, Limit = limit }, ct);

        var typed = result.Deserialize<FsReadTextFileResult>(JsonOptions);
        return typed?.Content;
    }

    /// <summary>
    /// Writes a text file via the client. The editor may show a diff preview.
    /// </summary>
    public async Task<bool> WriteTextFileAsync(string path, string content, CancellationToken ct = default)
    {
        if (!SupportsFileWrite) return false;

        var result = await _transport.SendClientRequestAsync(AcpMethods.FsWriteTextFile,
            new FsWriteTextFileParams { Path = path, Content = content }, ct);

        var typed = result.Deserialize<FsWriteTextFileResult>(JsonOptions);
        return typed?.Success ?? false;
    }

    // ───── Terminal operations ─────

    /// <summary>
    /// Creates a terminal in the client and executes the given command.
    /// </summary>
    public async Task<string?> CreateTerminalAsync(string command, string? cwd = null, Dictionary<string, string>? env = null, CancellationToken ct = default)
    {
        if (!SupportsTerminal) return null;

        var result = await _transport.SendClientRequestAsync(AcpMethods.TerminalCreate,
            new TerminalCreateParams { Command = command, Cwd = cwd, Env = env }, ct);

        var typed = result.Deserialize<TerminalCreateResult>(JsonOptions);
        return typed?.TerminalId;
    }

    /// <summary>
    /// Gets the output and optional exit code from a terminal.
    /// </summary>
    public async Task<(string output, int? exitCode)> GetTerminalOutputAsync(string terminalId, CancellationToken ct = default)
    {
        var result = await _transport.SendClientRequestAsync(AcpMethods.TerminalGetOutput,
            new TerminalGetOutputParams { TerminalId = terminalId }, ct);

        var typed = result.Deserialize<TerminalGetOutputResult>(JsonOptions);
        return (typed?.Output ?? "", typed?.ExitCode);
    }

    /// <summary>
    /// Waits for a terminal command to exit.
    /// </summary>
    public async Task<(string output, int? exitCode)> WaitForTerminalExitAsync(string terminalId, int? timeoutSeconds = null, CancellationToken ct = default)
    {
        var result = await _transport.SendClientRequestAsync(AcpMethods.TerminalWaitForExit,
            new TerminalWaitForExitParams { TerminalId = terminalId, Timeout = timeoutSeconds }, ct);

        var typed = result.Deserialize<TerminalGetOutputResult>(JsonOptions);
        return (typed?.Output ?? "", typed?.ExitCode);
    }

    /// <summary>
    /// Kills a terminal command.
    /// </summary>
    public async Task KillTerminalAsync(string terminalId, CancellationToken ct = default)
    {
        await _transport.SendClientRequestAsync(AcpMethods.TerminalKill,
            new TerminalKillParams { TerminalId = terminalId }, ct);
    }

    /// <summary>
    /// Releases a terminal.
    /// </summary>
    public async Task ReleaseTerminalAsync(string terminalId, CancellationToken ct = default)
    {
        await _transport.SendClientRequestAsync(AcpMethods.TerminalRelease,
            new TerminalReleaseParams { TerminalId = terminalId }, ct);
    }

    // ───── Extension methods ─────

    /// <summary>
    /// Sends a generic extension method request to the client.
    /// Used for ACP extension methods like _unity/* for Unity-specific operations.
    /// </summary>
    /// <param name="method">The extension method name (e.g., "_unity/scene_query")</param>
    /// <param name="params">The parameters for the method</param>
    /// <param name="ct">Cancellation token</param>
    /// <param name="timeout">Optional timeout override</param>
    /// <returns>The raw JSON result from the client</returns>
    public async Task<JsonElement> SendExtensionAsync(
        string method,
        object? @params,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        return await _transport.SendClientRequestAsync(method, @params, ct, timeout);
    }

    /// <summary>
    /// Sends a generic extension method request and deserializes the result.
    /// </summary>
    public async Task<T?> SendExtensionAsync<T>(
        string method,
        object? @params,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
    {
        var result = await SendExtensionAsync(method, @params, ct, timeout);
        return result.Deserialize<T>(JsonOptions);
    }
}
