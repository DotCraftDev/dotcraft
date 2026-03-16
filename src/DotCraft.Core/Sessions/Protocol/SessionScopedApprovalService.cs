using DotCraft.Security;

namespace DotCraft.Sessions.Protocol;

/// <summary>
/// An IApprovalService wrapper that supports Turn-scoped overrides via AsyncLocal.
/// Injected into AgentFactory so that all tools call this service.
/// When SessionService runs a Turn, it calls SetOverride() to redirect approval
/// requests through the Turn's SessionApprovalService.
/// When no override is set (legacy code paths), requests delegate to the inner service.
/// </summary>
public sealed class SessionScopedApprovalService(IApprovalService inner) : IApprovalService
{
    private static readonly AsyncLocal<IApprovalService?> Override = new();

    /// <summary>
    /// Sets the Turn-scoped approval service for the current async context.
    /// Dispose the returned handle to clear the override.
    /// </summary>
    public static IDisposable SetOverride(IApprovalService service)
    {
        Override.Value = service;
        return new OverrideScope();
    }

    public Task<bool> RequestFileApprovalAsync(
        string operation,
        string path,
        ApprovalContext? context = null) =>
        (Override.Value ?? inner).RequestFileApprovalAsync(operation, path, context);

    public Task<bool> RequestShellApprovalAsync(
        string command,
        string? workingDir,
        ApprovalContext? context = null) =>
        (Override.Value ?? inner).RequestShellApprovalAsync(command, workingDir, context);

    private sealed class OverrideScope : IDisposable
    {
        public void Dispose() => Override.Value = null;
    }
}
