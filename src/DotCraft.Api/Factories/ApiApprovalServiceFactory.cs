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
        var autoApprove = context.AutoApprove ?? config.AutoApprove;
        var approvalMode = ApiApprovalService.ParseMode(autoApprove);

        return new ApiApprovalService(approvalMode);
    }
}
