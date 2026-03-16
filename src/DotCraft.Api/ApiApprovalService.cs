using DotCraft.Security;

namespace DotCraft.Api;

public enum ApiApprovalMode
{
    Auto,
    Reject
}

public sealed class ApiApprovalService(ApiApprovalMode mode = ApiApprovalMode.Auto)
    : IApprovalService
{
    public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
        => Task.FromResult(mode == ApiApprovalMode.Auto);

    public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null)
        => Task.FromResult(mode == ApiApprovalMode.Auto);

    public static ApiApprovalMode ParseMode(bool autoApprove)
        => autoApprove ? ApiApprovalMode.Auto : ApiApprovalMode.Reject;
}
