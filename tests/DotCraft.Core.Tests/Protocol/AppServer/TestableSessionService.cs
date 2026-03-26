using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// A full <see cref="ISessionService"/> implementation for AppServer unit tests.
/// Thread lifecycle methods are backed by a real <see cref="ThreadStore"/> so that
/// <c>thread/start</c>, <c>thread/list</c>, etc. produce realistic wire responses.
/// <c>SubmitInputAsync</c> yields canned <see cref="SessionEvent"/> sequences queued
/// per thread via <see cref="EnqueueSubmitEvents"/>.
/// </summary>
internal sealed class TestableSessionService : ISessionService
{
    private readonly ThreadStore _store;
    private readonly Dictionary<string, SessionThread> _cache = new();
    private readonly Dictionary<string, Queue<SessionEvent[]>> _submitQueue = new();
    private readonly List<(string threadId, string turnId)> _cancelledTurns = new();
    private readonly List<(string threadId, string turnId, string requestId, SessionApprovalDecision decision)> _resolvedApprovals = new();

    public IReadOnlyList<(string threadId, string turnId)> CancelledTurns => _cancelledTurns;
    public IReadOnlyList<(string threadId, string turnId, string requestId, SessionApprovalDecision decision)> ResolvedApprovals => _resolvedApprovals;

    /// <inheritdoc />
    public Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }

    public TestableSessionService(ThreadStore store) => _store = store;

    // -------------------------------------------------------------------------
    // Canned event configuration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Queues a batch of events to be returned by the next <c>SubmitInputAsync</c>
    /// call for the given thread. Multiple calls enqueue multiple batches.
    /// </summary>
    public void EnqueueSubmitEvents(string threadId, params SessionEvent[] events)
    {
        if (!_submitQueue.TryGetValue(threadId, out var queue))
            _submitQueue[threadId] = queue = new Queue<SessionEvent[]>();
        queue.Enqueue(events);
    }

    // -------------------------------------------------------------------------
    // ISessionService — thread lifecycle (backed by ThreadStore)
    // -------------------------------------------------------------------------

    public async Task<SessionThread> CreateThreadAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? threadId = null,
        string? displayName = null,
        CancellationToken ct = default)
    {
        var thread = new SessionThread
        {
            Id = threadId ?? SessionIdGenerator.NewThreadId(),
            WorkspacePath = identity.WorkspacePath,
            UserId = identity.UserId,
            OriginChannel = identity.ChannelName,
            Status = ThreadStatus.Active,
            HistoryMode = historyMode,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            Configuration = config,
            DisplayName = displayName
        };
        if (identity.ChannelContext != null)
        {
            thread.ChannelContext = identity.ChannelContext;
            thread.Metadata["channelContext"] = identity.ChannelContext;
        }
        _cache[thread.Id] = thread;
        await _store.SaveThreadAsync(thread, ct);
        ThreadCreatedForBroadcast?.Invoke(thread);
        return thread;
    }

    public async Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default)
    {
        var t = await GetOrLoadAsync(threadId, ct);
        if (t.Status != ThreadStatus.Active)
        {
            t.Status = ThreadStatus.Active;
            t.LastActiveAt = DateTimeOffset.UtcNow;
            await _store.SaveThreadAsync(t, ct);
        }
        return t;
    }

    public async Task PauseThreadAsync(string threadId, CancellationToken ct = default)
    {
        var t = await GetOrLoadAsync(threadId, ct);
        if (t.Status == ThreadStatus.Paused) return;
        t.Status = ThreadStatus.Paused;
        await _store.SaveThreadAsync(t, ct);
    }

    public async Task ArchiveThreadAsync(string threadId, CancellationToken ct = default)
    {
        var t = await GetOrLoadAsync(threadId, ct);
        if (t.Status == ThreadStatus.Archived) return;
        t.Status = ThreadStatus.Archived;
        await _store.SaveThreadAsync(t, ct);
    }

    public async Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default)
    {
        var t = await GetOrLoadAsync(threadId, ct);
        t.DisplayName = displayName;
        await _store.SaveThreadAsync(t, ct);
    }

    public async Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        bool includeArchived = false,
        IReadOnlyList<string>? crossChannelOrigins = null,
        CancellationToken ct = default)
    {
        var index = await _store.LoadIndexAsync(ct);
        var hasCross = crossChannelOrigins is { Count: > 0 };
        return index
            .Where(s =>
            {
                if (!string.Equals(s.WorkspacePath, identity.WorkspacePath, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!(includeArchived || s.Status != ThreadStatus.Archived))
                    return false;

                var identityMatch =
                    (identity.UserId == null || s.UserId == identity.UserId)
                    && (identity.ChannelContext == null
                        ? s.ChannelContext == null
                        : s.ChannelContext == identity.ChannelContext);

                if (identityMatch)
                    return true;

                if (!hasCross)
                    return false;

                foreach (var o in crossChannelOrigins!)
                {
                    if (string.Equals(o, s.OriginChannel, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            })
            .OrderByDescending(s => s.LastActiveAt)
            .ToList();
    }

    public async Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default) =>
        await GetOrLoadAsync(threadId, ct);

    public Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default) =>
        GetThreadAsync(threadId, ct);

    public Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default) =>
        Task.CompletedTask;

    public async Task UpdateThreadConfigurationAsync(
        string threadId, ThreadConfiguration config, CancellationToken ct = default)
    {
        var t = await GetOrLoadAsync(threadId, ct);
        t.Configuration = config;
        await _store.SaveThreadAsync(t, ct);
    }

    public Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default)
    {
        _cache.Remove(threadId);
        _store.DeleteThread(threadId);
        _store.DeleteSessionFile(threadId);
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // ISessionService — turn operations (backed by canned events)
    // -------------------------------------------------------------------------

    public IAsyncEnumerable<SessionEvent> SubmitInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        ChatMessage[]? messages = null,
        CancellationToken ct = default)
    {
        if (_submitQueue.TryGetValue(threadId, out var queue) && queue.TryDequeue(out var events))
            return YieldEvents(events, ct);

        return EmptyEvents();
    }

    public IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(
        string threadId,
        bool replayRecent = false,
        CancellationToken ct = default)
    {
        // Keep the subscription alive until the token is cancelled so that
        // AppServerConnection.HasSubscription(threadId) stays true for the lifetime of the subscription.
        // In the real SessionService, this stream is driven by the ThreadEventBroker.
        return BlockUntilCancelledAsync(ct);
    }

    private static async IAsyncEnumerable<SessionEvent> BlockUntilCancelledAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }
        yield break;
    }

    public Task ResolveApprovalAsync(
        string threadId, string turnId, string requestId,
        SessionApprovalDecision decision, CancellationToken ct = default)
    {
        _resolvedApprovals.Add((threadId, turnId, requestId, decision));
        return Task.CompletedTask;
    }

    public Task CancelTurnAsync(string threadId, string turnId, CancellationToken ct = default)
    {
        _cancelledTurns.Add((threadId, turnId));
        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<SessionThread> GetOrLoadAsync(string threadId, CancellationToken ct)
    {
        if (_cache.TryGetValue(threadId, out var cached)) return cached;
        var loaded = await _store.LoadThreadAsync(threadId, ct)
            ?? throw new KeyNotFoundException($"Thread '{threadId}' not found.");
        _cache[threadId] = loaded;
        return loaded;
    }

    private static async IAsyncEnumerable<SessionEvent> YieldEvents(
        SessionEvent[] events,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var e in events)
        {
            ct.ThrowIfCancellationRequested();
            yield return e;
        }
    }

    private static async IAsyncEnumerable<SessionEvent> EmptyEvents()
    {
        yield break;
    }
}
