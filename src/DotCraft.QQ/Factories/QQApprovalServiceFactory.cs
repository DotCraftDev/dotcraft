using DotCraft.Abstractions;
using DotCraft.Security;

namespace DotCraft.QQ.Factories;

/// <summary>
/// Factory for creating QQ approval service instances.
/// </summary>
public sealed class QQApprovalServiceFactory : IApprovalServiceFactory
{
    /// <inheritdoc />
    public IApprovalService Create(ApprovalServiceContext context)
    {
        var client = context.ChannelClient as QQBotClient
            ?? throw new InvalidOperationException("ChannelClient must be a QQBotClient for QQ approval service");

        var permissionService = context.PermissionService as QQPermissionService
            ?? throw new InvalidOperationException("PermissionService must be a QQPermissionService for QQ approval service");

        var timeoutSeconds = context.ApprovalTimeoutSeconds ?? context.Config.QQBot.ApprovalTimeoutSeconds;

        return new QQApprovalService(client, permissionService, timeoutSeconds);
    }
}
