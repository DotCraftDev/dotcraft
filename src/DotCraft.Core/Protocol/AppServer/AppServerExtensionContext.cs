namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Context passed to module-provided AppServer protocol extensions.
/// </summary>
public sealed class AppServerExtensionContext(
    AppServerConnection connection,
    IAppServerTransport transport,
    string? workspaceCraftPath,
    string? hostWorkspacePath,
    CancellationToken cancellationToken)
{
    public AppServerConnection Connection { get; } = connection;

    public IAppServerTransport Transport { get; } = transport;

    public string? WorkspaceCraftPath { get; } = workspaceCraftPath;

    public string? HostWorkspacePath { get; } = hostWorkspacePath;

    public CancellationToken CancellationToken { get; } = cancellationToken;
}
