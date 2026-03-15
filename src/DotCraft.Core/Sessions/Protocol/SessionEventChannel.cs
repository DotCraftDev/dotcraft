using System.Threading.Channels;

namespace DotCraft.Sessions.Protocol;

/// <summary>
/// Wraps a System.Threading.Channels channel to provide typed event emission
/// during Turn execution. One instance is created per Turn.
/// </summary>
internal sealed class SessionEventChannel
{
    private readonly Channel<SessionEvent> _channel;
    private readonly string _threadId;
    private readonly string _turnId;
    private int _eventSequence;

    public SessionEventChannel(string threadId, string turnId)
    {
        _threadId = threadId;
        _turnId = turnId;
        _channel = Channel.CreateUnbounded<SessionEvent>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = false
        });
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    private string NextEventId() =>
        $"evt_{Interlocked.Increment(ref _eventSequence):D4}";

    private void Write(SessionEventType type, string? itemId, object? payload)
    {
        var evt = new SessionEvent
        {
            EventId = NextEventId(),
            EventType = type,
            ThreadId = _threadId,
            TurnId = _turnId,
            ItemId = itemId,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = payload
        };
        _channel.Writer.TryWrite(evt);
    }

    private void WriteThreadLevel(SessionEventType type, string? threadId, object? payload)
    {
        var evt = new SessionEvent
        {
            EventId = NextEventId(),
            EventType = type,
            ThreadId = threadId ?? _threadId,
            TurnId = null,
            ItemId = null,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = payload
        };
        _channel.Writer.TryWrite(evt);
    }

    // -------------------------------------------------------------------------
    // Turn events
    // -------------------------------------------------------------------------

    public void EmitTurnStarted(SessionTurn turn) =>
        Write(SessionEventType.TurnStarted, null, turn);

    public void EmitTurnCompleted(SessionTurn turn) =>
        Write(SessionEventType.TurnCompleted, null, turn);

    public void EmitTurnFailed(SessionTurn turn, string error) =>
        Write(SessionEventType.TurnFailed, null, turn);

    public void EmitTurnCancelled(SessionTurn turn, string reason) =>
        Write(SessionEventType.TurnCancelled, null, turn);

    // -------------------------------------------------------------------------
    // Item events
    // -------------------------------------------------------------------------

    public void EmitItemStarted(SessionItem item) =>
        Write(SessionEventType.ItemStarted, item.Id, item);

    public void EmitItemDelta(SessionItem item, object deltaPayload) =>
        Write(SessionEventType.ItemDelta, item.Id, deltaPayload);

    public void EmitItemCompleted(SessionItem item) =>
        Write(SessionEventType.ItemCompleted, item.Id, item);

    // -------------------------------------------------------------------------
    // Approval events
    // -------------------------------------------------------------------------

    public void EmitApprovalRequested(SessionItem item) =>
        Write(SessionEventType.ApprovalRequested, item.Id, item);

    public void EmitApprovalResolved(SessionItem item) =>
        Write(SessionEventType.ApprovalResolved, item.Id, item);

    // -------------------------------------------------------------------------
    // Thread events (emitted when the Turn starts on a new or resumed thread)
    // -------------------------------------------------------------------------

    public void EmitThreadCreated(SessionThread thread) =>
        WriteThreadLevel(SessionEventType.ThreadCreated, thread.Id, thread);

    public void EmitThreadResumed(SessionThread thread, string resumedBy) =>
        WriteThreadLevel(SessionEventType.ThreadResumed, thread.Id, thread);

    public void EmitThreadStatusChanged(string threadId, ThreadStatus prev, ThreadStatus next) =>
        WriteThreadLevel(SessionEventType.ThreadStatusChanged, threadId,
            new ThreadStatusChangedPayload { PreviousStatus = prev, NewStatus = next });

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    /// <summary>
    /// Signals that no more events will be written. Must be called even on error paths.
    /// </summary>
    public void Complete(Exception? error = null) =>
        _channel.Writer.TryComplete(error);

    /// <summary>
    /// Returns all events from the channel as an async sequence.
    /// Completes when the channel is completed.
    /// </summary>
    public IAsyncEnumerable<SessionEvent> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
