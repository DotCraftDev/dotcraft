using DotCraft.Abstractions;
using DotCraft.Security;

namespace DotCraft.Api.Factories;

/// <summary>
/// Factory for creating API approval service instances.
/// </summary>
public sealed class ApiApprovalServiceFactory : IApprovalServiceFactory
{
    /// <inheritdoc />
    public IApprovalService Create(ApprovalServiceContext context)
    {
        var config = context.Config.GetSection<ApiConfig>("Api");
        var approvalMode = ApiApprovalService.ParseMode(
            context.ApprovalMode ?? config.ApprovalMode,
            context.AutoApprove ?? config.AutoApprove);
        var timeoutSeconds = context.ApprovalTimeoutSeconds ?? config.ApprovalTimeoutSeconds;

        return new ApiApprovalService(approvalMode, timeoutSeconds);
    }
}
