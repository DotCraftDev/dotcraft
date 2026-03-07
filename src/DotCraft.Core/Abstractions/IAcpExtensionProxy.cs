namespace DotCraft.Abstractions;

/// <summary>
/// Abstraction for ACP extension method calls.
/// Implemented by <c>AcpClientProxy</c> in the App project;
/// consumed by extension tool providers (e.g., Unity) that live in separate assemblies.
/// </summary>
public interface IAcpExtensionProxy
{
    /// <summary>
    /// Extension method prefixes supported by the connected client (e.g. ["_unity"]).
    /// Empty when the client has not advertised any extensions.
    /// </summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>
    /// Sends an extension method request and deserializes the result.
    /// </summary>
    Task<T?> SendExtensionAsync<T>(
        string method,
        object? @params,
        CancellationToken ct = default,
        TimeSpan? timeout = null);
}
