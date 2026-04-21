using DotCraft.Security;

namespace DotCraft.Protocol;

/// <summary>
/// Approval service that cancels the current turn instead of prompting the user.
/// When approval is required, returns false and invokes <paramref name="cancelTurn"/>
/// so streaming stops with <see cref="System.OperationCanceledException"/>.
/// </summary>
internal sealed class InterruptOnApprovalService(Action cancelTurn) : IApprovalService
{
    public Task<bool> RequestFileApprovalAsync(
        string operation,
        string path,
        ApprovalContext? context = null) =>
        DenyAndCancel();

    public Task<bool> RequestShellApprovalAsync(
        string command,
        string? workingDir,
        ApprovalContext? context = null) =>
        DenyAndCancel();

    public Task<bool> RequestResourceApprovalAsync(
        string kind,
        string operation,
        string target,
        ApprovalContext? context = null) =>
        DenyAndCancel();

    private Task<bool> DenyAndCancel()
    {
        cancelTurn();
        return Task.FromResult(false);
    }
}
