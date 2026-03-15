using DotCraft.Sessions;
using DotCraft.Sessions.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Core.Tests.Sessions.Protocol;

/// <summary>
/// Unit tests for ISessionService lifecycle: create, pause, resume, archive, find.
/// Uses a FakeSessionService backed by a real ThreadStore to verify the contract.
/// </summary>
public sealed class SessionServiceLifecycleTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ThreadStore _store;
    private readonly FakeSessionService _svc;

    public SessionServiceLifecycleTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SSLifecycle_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _store = new ThreadStore(_tempDir);
        _svc = new FakeSessionService(_store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    // -------------------------------------------------------------------------
    // CreateThread
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateThread_ReturnsActiveThread()
    {
        var identity = MakeIdentity();
        var thread = await _svc.CreateThreadAsync(identity);

        Assert.NotNull(thread);
        Assert.False(string.IsNullOrEmpty(thread.Id));
        Assert.Equal(ThreadStatus.Active, thread.Status);
        Assert.Equal(identity.WorkspacePath, thread.WorkspacePath);
        Assert.Equal(identity.UserId, thread.UserId);
        Assert.Equal(identity.ChannelName, thread.OriginChannel);
    }

    [Fact]
    public async Task CreateThread_IsPersisted()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(thread.Id, loaded.Id);
    }

    [Fact]
    public async Task CreateThread_AppearsInIndex()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        var index = await _store.LoadIndexAsync();
        Assert.Contains(index, s => s.Id == thread.Id);
    }

    [Fact]
    public async Task CreateThread_IdFormat_IsValid()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        // Format: thread_{timestamp}_{random}
        Assert.StartsWith("thread_", thread.Id);
    }

    // -------------------------------------------------------------------------
    // PauseThread
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PauseThread_SetsStatusPaused()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.PauseThreadAsync(thread.Id);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ThreadStatus.Paused, loaded.Status);
    }

    [Fact]
    public async Task PauseThread_IsIdempotent()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.PauseThreadAsync(thread.Id);
        await _svc.PauseThreadAsync(thread.Id); // second call should not throw
        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ThreadStatus.Paused, loaded.Status);
    }

    // -------------------------------------------------------------------------
    // ResumeThread
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ResumeThread_SetsStatusActive()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.PauseThreadAsync(thread.Id);
        await _svc.ResumeThreadAsync(thread.Id);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ThreadStatus.Active, loaded.Status);
    }

    [Fact]
    public async Task ResumeThread_AlreadyActive_DoesNotThrow()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        // Resuming an already-active thread should be a no-op
        var resumed = await _svc.ResumeThreadAsync(thread.Id);
        Assert.Equal(ThreadStatus.Active, resumed.Status);
    }

    [Fact]
    public async Task ResumeThread_Archived_Throws()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.ArchiveThreadAsync(thread.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _svc.ResumeThreadAsync(thread.Id));
    }

    // -------------------------------------------------------------------------
    // ArchiveThread
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ArchiveThread_SetsStatusArchived()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.ArchiveThreadAsync(thread.Id);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ThreadStatus.Archived, loaded.Status);
    }

    [Fact]
    public async Task ArchiveThread_IsIdempotent()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.ArchiveThreadAsync(thread.Id);
        await _svc.ArchiveThreadAsync(thread.Id);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ThreadStatus.Archived, loaded.Status);
    }

    // -------------------------------------------------------------------------
    // FindThreads
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FindThreads_ReturnsMatchingThreadsByWorkspaceAndUser()
    {
        var identity = MakeIdentity("user1", "/ws/alpha");
        await _svc.CreateThreadAsync(identity);
        await _svc.CreateThreadAsync(identity);
        await _svc.CreateThreadAsync(MakeIdentity("user2", "/ws/alpha")); // different user

        var found = await _svc.FindThreadsAsync(identity);
        Assert.Equal(2, found.Count);
        Assert.All(found, s => Assert.Equal("user1", s.UserId));
    }

    [Fact]
    public async Task FindThreads_NoMatch_ReturnsEmpty()
    {
        await _svc.CreateThreadAsync(MakeIdentity("user1", "/ws1"));

        var found = await _svc.FindThreadsAsync(MakeIdentity("user1", "/ws2"));
        Assert.Empty(found);
    }

    // -------------------------------------------------------------------------
    // GetThread
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetThread_ReturnsThread()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        var retrieved = await _svc.GetThreadAsync(thread.Id);
        Assert.Equal(thread.Id, retrieved.Id);
    }

    [Fact]
    public async Task GetThread_NonExistent_Throws()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _svc.GetThreadAsync("nonexistent_id"));
    }

    // -------------------------------------------------------------------------
    // SetThreadMode
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetThreadMode_UpdatesConfig()
    {
        var thread = await _svc.CreateThreadAsync(MakeIdentity());
        await _svc.SetThreadModeAsync(thread.Id, "plan");

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal("plan", loaded.Configuration?.Mode);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static SessionIdentity MakeIdentity(string? userId = "user1", string? workspace = "/workspace") =>
        new() { ChannelName = "test", UserId = userId, WorkspacePath = workspace ?? "/workspace" };
}

/// <summary>
/// A lightweight ISessionService implementation backed by ThreadStore, used for lifecycle testing.
/// Does not implement SubmitInputAsync (throws NotSupportedException).
/// </summary>
internal sealed class FakeSessionService : ISessionService
{
    private readonly ThreadStore _store;
    private readonly Dictionary<string, SessionThread> _threads = new();

    public FakeSessionService(ThreadStore store) => _store = store;

    public async Task<SessionThread> CreateThreadAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        CancellationToken ct = default)
    {
        var thread = new SessionThread
        {
            Id = SessionIdGenerator.NewThreadId(),
            WorkspacePath = identity.WorkspacePath,
            UserId = identity.UserId,
            OriginChannel = identity.ChannelName,
            Status = ThreadStatus.Active,
            HistoryMode = historyMode,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            Configuration = config
        };

        if (identity.ChannelContext != null)
            thread.Metadata["channelContext"] = identity.ChannelContext;

        _threads[thread.Id] = thread;
        await _store.SaveThreadAsync(thread, ct);
        await _store.UpdateIndexEntryAsync(thread, ct);
        return thread;
    }

    public async Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        if (thread.Status == ThreadStatus.Archived)
            throw new InvalidOperationException($"Thread '{threadId}' is archived.");
        if (thread.Status != ThreadStatus.Active)
        {
            thread.Status = ThreadStatus.Active;
            thread.LastActiveAt = DateTimeOffset.UtcNow;
            await _store.SaveThreadAsync(thread, ct);
            await _store.UpdateIndexEntryAsync(thread, ct);
        }
        return thread;
    }

    public async Task PauseThreadAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        if (thread.Status == ThreadStatus.Paused) return;
        thread.Status = ThreadStatus.Paused;
        await _store.SaveThreadAsync(thread, ct);
        await _store.UpdateIndexEntryAsync(thread, ct);
    }

    public async Task ArchiveThreadAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        if (thread.Status == ThreadStatus.Archived) return;
        thread.Status = ThreadStatus.Archived;
        await _store.SaveThreadAsync(thread, ct);
        await _store.UpdateIndexEntryAsync(thread, ct);
    }

    public async Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        CancellationToken ct = default)
    {
        var index = await _store.LoadIndexAsync(ct);
        return index
            .Where(s =>
                string.Equals(s.WorkspacePath, identity.WorkspacePath, StringComparison.OrdinalIgnoreCase)
                && (identity.UserId == null || s.UserId == identity.UserId))
            .OrderByDescending(s => s.LastActiveAt)
            .ToList();
    }

    public async Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default) =>
        await GetOrLoadAsync(threadId, ct);

    public IAsyncEnumerable<SessionEvent> SubmitInputAsync(
        string threadId, string text, SenderContext? sender = null,
        ChatMessage[]? messages = null, CancellationToken ct = default) =>
        throw new NotSupportedException("Use FakeSessionService for lifecycle tests only.");

    public Task ResolveApprovalAsync(string turnId, string requestId, bool approved, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task CancelTurnAsync(string turnId, CancellationToken ct = default) =>
        Task.CompletedTask;

    public async Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        thread.Configuration ??= new ThreadConfiguration();
        thread.Configuration.Mode = mode;
        await _store.SaveThreadAsync(thread, ct);
        await _store.UpdateIndexEntryAsync(thread, ct);
    }

    public async Task UpdateThreadConfigurationAsync(string threadId, ThreadConfiguration config, CancellationToken ct = default)
    {
        var thread = await GetOrLoadAsync(threadId, ct);
        thread.Configuration = config;
        await _store.SaveThreadAsync(thread, ct);
        await _store.UpdateIndexEntryAsync(thread, ct);
    }

    private async Task<SessionThread> GetOrLoadAsync(string threadId, CancellationToken ct)
    {
        if (_threads.TryGetValue(threadId, out var t)) return t;
        var loaded = await _store.LoadThreadAsync(threadId, ct)
            ?? throw new KeyNotFoundException($"Thread '{threadId}' not found.");
        _threads[threadId] = loaded;
        return loaded;
    }
}
