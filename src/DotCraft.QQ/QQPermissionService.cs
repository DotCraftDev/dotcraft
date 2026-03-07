namespace DotCraft.QQ;

public enum QQUserRole
{
    Unauthorized,
    Whitelisted,
    Admin
}

public enum OperationTier
{
    Chat,
    ReadOnly,
    WriteWorkspace,
    WriteOutsideWorkspace
}

public sealed class QQPermissionService(IEnumerable<long> adminUsers, IEnumerable<long> whitelistedUsers, IEnumerable<long> whitelistedGroups)
{
    private readonly HashSet<long> _adminUsers = [..adminUsers];

    private readonly HashSet<long> _whitelistedUsers = [..whitelistedUsers];

    private readonly HashSet<long> _whitelistedGroups = [..whitelistedGroups];

    public QQUserRole GetUserRole(long userId, long groupId = 0)
    {
        if (_adminUsers.Contains(userId))
            return QQUserRole.Admin;

        if (_whitelistedUsers.Contains(userId))
            return QQUserRole.Whitelisted;

        if (groupId > 0 && _whitelistedGroups.Contains(groupId))
            return QQUserRole.Whitelisted;

        return QQUserRole.Unauthorized;
    }

    public bool IsOperationAllowed(QQUserRole role, OperationTier tier)
    {
        return tier switch
        {
            OperationTier.Chat => role != QQUserRole.Unauthorized,
            OperationTier.ReadOnly => role != QQUserRole.Unauthorized,
            OperationTier.WriteWorkspace => role == QQUserRole.Admin,
            OperationTier.WriteOutsideWorkspace => role == QQUserRole.Admin,
            _ => false
        };
    }

    public static bool RequiresApproval(QQUserRole role, OperationTier tier)
    {
        return role == QQUserRole.Admin && tier is OperationTier.WriteWorkspace or OperationTier.WriteOutsideWorkspace;
    }

    public static OperationTier ClassifyFileOperation(string operation, bool isWithinWorkspace)
    {
        if (!isWithinWorkspace)
            return OperationTier.WriteOutsideWorkspace;

        return operation.ToLowerInvariant() switch
        {
            "read" or "list" => OperationTier.ReadOnly,
            "write" or "edit" => OperationTier.WriteWorkspace,
            _ => OperationTier.WriteWorkspace
        };
    }

    public static OperationTier ClassifyShellOperation(bool isOutsideWorkspace)
    {
        return isOutsideWorkspace ? OperationTier.WriteOutsideWorkspace : OperationTier.WriteWorkspace;
    }
}
