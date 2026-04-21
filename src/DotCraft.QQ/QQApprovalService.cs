using System.Collections.Concurrent;
using DotCraft.QQ.OneBot;
using DotCraft.Security;

namespace DotCraft.QQ;

public sealed class QQApprovalService(QQBotClient client, QQPermissionService permissionService, int timeoutSeconds = 60) 
    : IApprovalService
{
    private readonly ConcurrentDictionary<string, PendingApproval> _pendingApprovals = new();
    private readonly ConcurrentDictionary<string, HashSet<string>> _sessionApprovals = new();

    private sealed class PendingApproval(TaskCompletionSource<ApprovalResult> tcs, string operationKey)
    {
        public TaskCompletionSource<ApprovalResult> Tcs { get; } = tcs;
        public string OperationKey { get; } = operationKey;
    }

    private enum ApprovalResult { Approved, Rejected, ApprovedForSession }

    public async Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
    {
        if (context == null)
            return false;

        var userId = long.Parse(context.UserId);
        var role = permissionService.GetUserRole(userId, context.GroupId);
        var tier = QQPermissionService.ClassifyFileOperation(operation, isWithinWorkspace: false);

        if (!permissionService.IsOperationAllowed(role, tier))
            return false;

        if (!QQPermissionService.RequiresApproval(role, tier))
            return true;

        var operationKey = $"file:{operation.ToLowerInvariant()}";
        if (IsSessionApproved(context, operationKey))
            return true;

        var description = $"文件操作: {operation}\n路径: {path}";
        var result = await RequestApprovalViaQQAsync(context, description, operationKey);
        if (result == ApprovalResult.ApprovedForSession)
            AddSessionApproval(context, operationKey);
        return result != ApprovalResult.Rejected;
    }

    public async Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null)
    {
        if (context == null)
            return false;

        var userId = long.Parse(context.UserId);
        var role = permissionService.GetUserRole(userId, context.GroupId);
        var tier = QQPermissionService.ClassifyShellOperation(isOutsideWorkspace: true);

        if (!permissionService.IsOperationAllowed(role, tier))
            return false;

        if (!QQPermissionService.RequiresApproval(role, tier))
            return true;

        const string operationKey = "shell:*";
        if (IsSessionApproved(context, operationKey))
            return true;

        var description = $"Shell命令: {command}";
        if (!string.IsNullOrWhiteSpace(workingDir))
            description += $"\n工作目录: {workingDir}";
        var result = await RequestApprovalViaQQAsync(context, description, operationKey);
        if (result == ApprovalResult.ApprovedForSession)
            AddSessionApproval(context, operationKey);
        return result != ApprovalResult.Rejected;
    }

    public async Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null)
    {
        if (context == null)
            return false;

        var userId = long.Parse(context.UserId);
        var role = permissionService.GetUserRole(userId, context.GroupId);
        // Treat remote resource mutations conservatively as sensitive; require approval from admins/users.
        var tier = QQPermissionService.ClassifyFileOperation("write", isWithinWorkspace: false);

        if (!permissionService.IsOperationAllowed(role, tier))
            return false;

        if (!QQPermissionService.RequiresApproval(role, tier))
            return true;

        var operationKey = $"{kind}:{operation}".ToLowerInvariant();
        if (IsSessionApproved(context, operationKey))
            return true;

        var description = $"远端资源操作\n类型: {kind}\n操作: {operation}\n目标: {target}";
        var result = await RequestApprovalViaQQAsync(context, description, operationKey);
        if (result == ApprovalResult.ApprovedForSession)
            AddSessionApproval(context, operationKey);
        return result != ApprovalResult.Rejected;
    }

    public bool HasPendingApprovals => !_pendingApprovals.IsEmpty;

    public bool TryHandleApprovalReply(OneBotMessageEvent evt)
    {
        if (_pendingApprovals.IsEmpty)
            return false;

        var plainText = evt.GetPlainText().Trim();
        var userId = evt.UserId;
        var groupId = evt.IsGroupMessage ? evt.GroupId : 0;

        var keyPrefix = groupId > 0
            ? $"group_{groupId}_{userId}"
            : $"private_{userId}";

        var approved = plainText is "同意" or "允许" or "yes" or "y" or "approve";
        var approvedForSession = plainText is "同意全部" or "允许全部" or "yes all" or "approve all";
        var rejected = plainText is "拒绝" or "不同意" or "no" or "n" or "reject" or "deny";

        if (!approved && !approvedForSession && !rejected)
            return false;

        foreach (var key in _pendingApprovals.Keys)
        {
            if (!key.StartsWith(keyPrefix))
                continue;

            if (_pendingApprovals.TryRemove(key, out var pending))
            {
                var result = approvedForSession ? ApprovalResult.ApprovedForSession
                    : approved ? ApprovalResult.Approved
                    : ApprovalResult.Rejected;
                pending.Tcs.TrySetResult(result);
                return true;
            }
        }
        return false;
    }

    public void ClearSessionApprovals(ApprovalContext context)
    {
        var sessionKey = GetSessionKey(context);
        _sessionApprovals.TryRemove(sessionKey, out _);
    }

    private bool IsSessionApproved(ApprovalContext context, string operationKey)
    {
        var sessionKey = GetSessionKey(context);
        return _sessionApprovals.TryGetValue(sessionKey, out var ops) && ops.Contains(operationKey);
    }

    private void AddSessionApproval(ApprovalContext context, string operationKey)
    {
        var sessionKey = GetSessionKey(context);
        var ops = _sessionApprovals.GetOrAdd(sessionKey, _ => []);
        lock (ops) { ops.Add(operationKey); }
    }

    private static string GetSessionKey(ApprovalContext context)
    {
        return context.IsGroupContext
            ? $"group_{context.GroupId}_{context.UserId}"
            : $"private_{context.UserId}";
    }

    private async Task<ApprovalResult> RequestApprovalViaQQAsync(ApprovalContext context, string description, string operationKey)
    {
        var contextUserId = long.Parse(context.UserId);
        var approvalId = Guid.NewGuid().ToString("N")[..8];
        var approvalKey = context.IsGroupContext
            ? $"group_{context.GroupId}_{context.UserId}_{approvalId}"
            : $"private_{context.UserId}_{approvalId}";

        var tcs = new TaskCompletionSource<ApprovalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingApprovals[approvalKey] = new PendingApproval(tcs, operationKey);

        try
        {
            var message = $"⚠️ 操作审批请求\n{description}\n\n请回复: 同意 / 同意全部 / 拒绝 (超时{timeoutSeconds}秒自动拒绝)\n(同意全部: 本会话中不再询问同类操作)";

            if (context.IsGroupContext)
            {
                var segments = new List<OneBotMessageSegment>
                {
                    OneBotMessageSegment.At(context.UserId),
                    OneBotMessageSegment.Text($" {message}")
                };
                await client.SendGroupMessageAsync(context.GroupId, segments);
            }
            else
            {
                await client.SendPrivateMessageAsync(contextUserId, message);
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            cts.Token.Register(() => tcs.TrySetResult(ApprovalResult.Rejected));

            return await tcs.Task;
        }
        finally
        {
            _pendingApprovals.TryRemove(approvalKey, out _);
        }
    }
}
