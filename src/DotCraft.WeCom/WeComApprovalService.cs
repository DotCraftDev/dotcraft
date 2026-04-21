using System.Collections.Concurrent;
using DotCraft.Security;

namespace DotCraft.WeCom;

public enum OperationTier
{
    Chat,
    ReadOnly,
    WriteWorkspace,
    WriteOutsideWorkspace
}

public sealed class WeComApprovalService(
    WeComPermissionService permissionService,
    int timeoutSeconds = 60) : IApprovalService
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

        var role = permissionService.GetUserRole(context.UserId);
        var tier = WeComPermissionService.ClassifyFileOperation(operation, isWithinWorkspace: false);

        if (!permissionService.IsOperationAllowed(role, tier))
            return false;

        if (!WeComPermissionService.RequiresApproval(role, tier))
            return true;

        var operationKey = $"file:{operation.ToLowerInvariant()}";
        if (IsSessionApproved(context, operationKey))
            return true;

        var description = $"文件操作: {operation}\n路径: {path}";
        var result = await RequestApprovalViaWeComAsync(context, description, operationKey);
        if (result == ApprovalResult.ApprovedForSession)
            AddSessionApproval(context, operationKey);
        return result != ApprovalResult.Rejected;
    }

    public async Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null)
    {
        if (context == null)
            return false;

        var role = permissionService.GetUserRole(context.UserId);
        var tier = WeComPermissionService.ClassifyShellOperation(isOutsideWorkspace: true);

        if (!permissionService.IsOperationAllowed(role, tier))
            return false;

        if (!WeComPermissionService.RequiresApproval(role, tier))
            return true;

        const string operationKey = "shell:*";
        if (IsSessionApproved(context, operationKey))
            return true;

        var description = $"Shell命令: {command}";
        if (!string.IsNullOrWhiteSpace(workingDir))
            description += $"\n工作目录: {workingDir}";
        var result = await RequestApprovalViaWeComAsync(context, description, operationKey);
        if (result == ApprovalResult.ApprovedForSession)
            AddSessionApproval(context, operationKey);
        return result != ApprovalResult.Rejected;
    }

    public async Task<bool> RequestResourceApprovalAsync(string kind, string operation, string target, ApprovalContext? context = null)
    {
        if (context == null)
            return false;

        var role = permissionService.GetUserRole(context.UserId);
        var tier = WeComPermissionService.ClassifyFileOperation("write", isWithinWorkspace: false);

        if (!permissionService.IsOperationAllowed(role, tier))
            return false;

        if (!WeComPermissionService.RequiresApproval(role, tier))
            return true;

        var operationKey = $"{kind}:{operation}".ToLowerInvariant();
        if (IsSessionApproved(context, operationKey))
            return true;

        var description = $"远端资源操作\n类型: {kind}\n操作: {operation}\n目标: {target}";
        var result = await RequestApprovalViaWeComAsync(context, description, operationKey);
        if (result == ApprovalResult.ApprovedForSession)
            AddSessionApproval(context, operationKey);
        return result != ApprovalResult.Rejected;
    }

    public bool HasPendingApprovals => !_pendingApprovals.IsEmpty;

    public bool TryHandleApprovalReply(string plainText, string userId)
    {
        if (_pendingApprovals.IsEmpty)
            return false;

        var keyPrefix = $"user_{userId}";

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
        return $"user_{context.UserId}";
    }

    private async Task<ApprovalResult> RequestApprovalViaWeComAsync(ApprovalContext context, string description, string operationKey)
    {
        var pusher = WeComPusherScope.Current;
        if (pusher == null)
        {
            // No pusher available, reject the operation
            return ApprovalResult.Rejected;
        }

        var approvalId = Guid.NewGuid().ToString("N")[..8];
        var approvalKey = $"user_{context.UserId}_{approvalId}";

        var tcs = new TaskCompletionSource<ApprovalResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingApprovals[approvalKey] = new PendingApproval(tcs, operationKey);

        try
        {
            var message = $"⚠️ 操作审批请求\n{description}\n\n请回复: 同意 / 同意全部 / 拒绝 (超时{timeoutSeconds}秒自动拒绝)\n(同意全部: 本会话中不再询问同类操作)";

            await pusher.PushMarkdownAsync(message);

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
