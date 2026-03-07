using DotCraft.Abstractions;
using DotCraft.Security;

namespace DotCraft.WeCom.Factories;

/// <summary>
/// Factory for creating WeCom approval service instances.
/// </summary>
public sealed class WeComApprovalServiceFactory : IApprovalServiceFactory
{
    /// <inheritdoc />
    public IApprovalService Create(ApprovalServiceContext context)
    {
        var permissionService = context.PermissionService as WeComPermissionService
            ?? throw new InvalidOperationException("PermissionService must be a WeComPermissionService for WeCom approval service");

        var timeoutSeconds = context.ApprovalTimeoutSeconds ?? context.Config.WeComBot.ApprovalTimeoutSeconds;

        return new WeComApprovalService(permissionService, timeoutSeconds);
    }
}
