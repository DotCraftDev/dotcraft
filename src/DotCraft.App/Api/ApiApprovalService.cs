using System.Collections.Concurrent;
using DotCraft.Security;

namespace DotCraft.Api;

public sealed class PendingApproval
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];

    public string Type { get; init; } = "";

    public string Operation { get; init; } = "";

    public string Detail { get; init; } = "";

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    internal TaskCompletionSource<bool> Completion { get; } = new();
}

public enum ApiApprovalMode
{
    Auto,
    Reject,
    Interactive
}

public sealed class ApiApprovalService(ApiApprovalMode mode = ApiApprovalMode.Auto, int timeoutSeconds = 120)
    : IApprovalService
{
    private readonly TimeSpan _timeout = TimeSpan.FromSeconds(timeoutSeconds);
    
    private readonly ConcurrentDictionary<string, PendingApproval> _pending = new();

    public IReadOnlyCollection<PendingApproval> PendingApprovals => _pending.Values.ToList();

    public Task<bool> RequestFileApprovalAsync(string operation, string path, ApprovalContext? context = null)
    {
        return mode switch
        {
            ApiApprovalMode.Auto => Task.FromResult(true),
            ApiApprovalMode.Reject => Task.FromResult(false),
            ApiApprovalMode.Interactive => RequestInteractiveApprovalAsync("file", operation, path),
            _ => Task.FromResult(false)
        };
    }

    public Task<bool> RequestShellApprovalAsync(string command, string? workingDir, ApprovalContext? context = null)
    {
        var detail = string.IsNullOrEmpty(workingDir) ? command : $"{command} (cwd: {workingDir})";
        return mode switch
        {
            ApiApprovalMode.Auto => Task.FromResult(true),
            ApiApprovalMode.Reject => Task.FromResult(false),
            ApiApprovalMode.Interactive => RequestInteractiveApprovalAsync("shell", "exec", detail),
            _ => Task.FromResult(false)
        };
    }

    public bool Resolve(string approvalId, bool approved)
    {
        if (!_pending.TryRemove(approvalId, out var pending))
            return false;

        pending.Completion.TrySetResult(approved);
        return true;
    }

    private async Task<bool> RequestInteractiveApprovalAsync(string type, string operation, string detail)
    {
        var approval = new PendingApproval
        {
            Type = type,
            Operation = operation,
            Detail = detail
        };

        _pending[approval.Id] = approval;

        try
        {
            using var cts = new CancellationTokenSource(_timeout);
            await using var reg = cts.Token.Register(() => approval.Completion.TrySetResult(false));
            return await approval.Completion.Task;
        }
        finally
        {
            _pending.TryRemove(approval.Id, out _);
        }
    }

    public static ApiApprovalMode ParseMode(string mode, bool autoApproveFallback)
    {
        if (!string.IsNullOrEmpty(mode))
        {
            return mode.ToLowerInvariant() switch
            {
                "auto" => ApiApprovalMode.Auto,
                "reject" => ApiApprovalMode.Reject,
                "interactive" => ApiApprovalMode.Interactive,
                _ => autoApproveFallback ? ApiApprovalMode.Auto : ApiApprovalMode.Reject
            };
        }

        return autoApproveFallback ? ApiApprovalMode.Auto : ApiApprovalMode.Reject;
    }
}
