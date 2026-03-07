namespace DotCraft.WeCom;

public enum WeComUserRole
{
    Unauthorized,
    Whitelisted,
    Admin
}

public sealed class WeComPermissionService(
    IEnumerable<string> adminUsers,
    IEnumerable<string> whitelistedUsers,
    IEnumerable<string> whitelistedChats)
{
    private readonly HashSet<string> _adminUsers = [..adminUsers];

    private readonly HashSet<string> _whitelistedUsers = [..whitelistedUsers];

    private readonly HashSet<string> _whitelistedChats = [..whitelistedChats];

    public WeComUserRole GetUserRole(string userId, string? chatId = null)
    {
        if (_adminUsers.Contains(userId))
            return WeComUserRole.Admin;

        if (_whitelistedUsers.Contains(userId))
            return WeComUserRole.Whitelisted;

        if (!string.IsNullOrEmpty(chatId) && _whitelistedChats.Contains(chatId))
            return WeComUserRole.Whitelisted;

        return WeComUserRole.Unauthorized;
    }

    public bool IsOperationAllowed(WeComUserRole role, OperationTier tier)
    {
        return tier switch
        {
            OperationTier.Chat => role != WeComUserRole.Unauthorized,
            OperationTier.ReadOnly => role != WeComUserRole.Unauthorized,
            OperationTier.WriteWorkspace => role == WeComUserRole.Admin,
            OperationTier.WriteOutsideWorkspace => role == WeComUserRole.Admin,
            _ => false
        };
    }

    public static bool RequiresApproval(WeComUserRole role, OperationTier tier)
    {
        return role == WeComUserRole.Admin && tier is OperationTier.WriteWorkspace or OperationTier.WriteOutsideWorkspace;
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
