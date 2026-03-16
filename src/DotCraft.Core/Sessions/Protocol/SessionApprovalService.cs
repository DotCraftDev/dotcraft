using System.Collections.Concurrent;
using DotCraft.Security;

namespace DotCraft.Sessions.Protocol;

/// <summary>
/// Per-Turn IApprovalService that routes approval requests through the Session event stream.
/// When a tool requests approval, this service creates an ApprovalRequest Item, emits the
/// approval/requested event, and suspends tool execution until the adapter calls ResolveApproval.
/// </summary>
internal sealed class SessionApprovalService : IApprovalService
{
    private readonly SessionEventChannel _channel;
    private readonly SessionTurn _turn;
    private readonly Func<int> _nextItemSeq;
    private readonly TimeSpan _timeout;

    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();

    public SessionApprovalService(
        SessionEventChannel channel,
        SessionTurn turn,
        Func<int> nextItemSeq,
        TimeSpan timeout)
    {
        _channel = channel;
        _turn = turn;
        _nextItemSeq = nextItemSeq;
        _timeout = timeout;
    }

    /// <summary>
    /// True when there is at least one unresolved approval request.
    /// </summary>
    public bool HasPendingApproval => !_pending.IsEmpty;

    // -------------------------------------------------------------------------
    // IApprovalService
    // -------------------------------------------------------------------------

    public Task<bool> RequestFileApprovalAsync(
        string operation,
        string path,
        ApprovalContext? context = null)
    {
        var requestId = Guid.NewGuid().ToString("N")[..12];
        var payload = new ApprovalRequestPayload
        {
            ApprovalType = "file",
            Operation = operation,
            Target = path,
            RequestId = requestId
        };
        return RequestApprovalAsync(requestId, payload);
    }

    public Task<bool> RequestShellApprovalAsync(
        string command,
        string? workingDir,
        ApprovalContext? context = null)
    {
        var requestId = Guid.NewGuid().ToString("N")[..12];
        var payload = new ApprovalRequestPayload
        {
            ApprovalType = "shell",
            Operation = command,
            Target = workingDir ?? string.Empty,
            RequestId = requestId
        };
        return RequestApprovalAsync(requestId, payload);
    }

    // -------------------------------------------------------------------------
    // Called by SessionService.ResolveApprovalAsync
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves a pending approval request with the user's decision.
    /// Returns false if no matching pending request exists.
    /// </summary>
    public bool TryResolve(string requestId, bool approved)
    {
        if (!_pending.TryRemove(requestId, out var tcs))
            return false;

        // Create and emit ApprovalResponse Item
        var responseItem = CreateItem(ItemType.ApprovalResponse, new ApprovalResponsePayload
        {
            RequestId = requestId,
            Approved = approved
        });
        _turn.Items.Add(responseItem);

        // Restore Running status before completing TCS so the Turn status is correct
        // when agent execution resumes
        _turn.Status = TurnStatus.Running;

        _channel.EmitItemStarted(responseItem);
        _channel.EmitItemCompleted(responseItem);
        _channel.EmitApprovalResolved(responseItem);

        tcs.TrySetResult(approved);
        return true;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task<bool> RequestApprovalAsync(string requestId, ApprovalRequestPayload payload)
    {
        // Create ApprovalRequest Item
        var requestItem = CreateItem(ItemType.ApprovalRequest, payload);
        _turn.Items.Add(requestItem);
        _turn.Status = TurnStatus.WaitingApproval;

        // Register TCS before emitting the event so there's no race
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = tcs;

        _channel.EmitItemStarted(requestItem);
        _channel.EmitItemCompleted(requestItem);
        _channel.EmitApprovalRequested(requestItem);

        // Apply timeout
        using var cts = new CancellationTokenSource(_timeout);
        await using var reg = cts.Token.Register(() =>
        {
            if (_pending.TryRemove(requestId, out var pendingTcs))
            {
                // Timeout: emit an Error Item and auto-reject
                var errorItem = CreateItem(ItemType.Error, new ErrorPayload
                {
                    Message = $"Approval request '{requestId}' timed out after {_timeout.TotalSeconds:0}s.",
                    Code = "approval_timeout",
                    Fatal = false
                });
                _turn.Items.Add(errorItem);
                _turn.Status = TurnStatus.Running;
                _channel.EmitItemStarted(errorItem);
                _channel.EmitItemCompleted(errorItem);

                pendingTcs.TrySetResult(false);
            }
        });

        return await tcs.Task;
    }

    private SessionItem CreateItem(ItemType type, object payload)
    {
        var seq = _nextItemSeq();
        var item = new SessionItem
        {
            Id = SessionIdGenerator.NewItemId(seq),
            TurnId = _turn.Id,
            Type = type,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = payload
        };
        return item;
    }
}
