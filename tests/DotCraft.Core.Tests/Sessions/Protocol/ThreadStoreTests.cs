using DotCraft.Sessions.Protocol;

namespace DotCraft.Core.Tests.Sessions.Protocol;

/// <summary>
/// Unit tests for ThreadStore persistence and thread discovery.
/// Uses a temp directory so tests are hermetic.
/// </summary>
public sealed class ThreadStoreTests : IDisposable
{
    private readonly string _root;
    private readonly ThreadStore _store;

    public ThreadStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ThreadStoreTests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        _store = new ThreadStore(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }

    // -------------------------------------------------------------------------
    // Thread save / load
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveThread_ThenLoadThread_RoundTrip()
    {
        var thread = CreateThread();
        await _store.SaveThreadAsync(thread);

        var loaded = await _store.LoadThreadAsync(thread.Id);

        Assert.NotNull(loaded);
        Assert.Equal(thread.Id, loaded.Id);
        Assert.Equal(thread.WorkspacePath, loaded.WorkspacePath);
        Assert.Equal(thread.UserId, loaded.UserId);
        Assert.Equal(thread.OriginChannel, loaded.OriginChannel);
        Assert.Equal(thread.Status, loaded.Status);
        Assert.Equal(thread.HistoryMode, loaded.HistoryMode);
    }

    [Fact]
    public async Task LoadThread_NonExistent_ReturnsNull()
    {
        var loaded = await _store.LoadThreadAsync("nonexistent_id");
        Assert.Null(loaded);
    }

    [Fact]
    public async Task SaveThread_Overwrites_PreviousVersion()
    {
        var thread = CreateThread();
        await _store.SaveThreadAsync(thread);

        thread.Status = ThreadStatus.Paused;
        await _store.SaveThreadAsync(thread);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.Equal(ThreadStatus.Paused, loaded.Status);
    }

    [Fact]
    public async Task SaveThread_PreservesMetadata()
    {
        var thread = CreateThread();
        thread.Metadata["customKey"] = "test-value";
        await _store.SaveThreadAsync(thread);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.True(loaded.Metadata.TryGetValue("customKey", out var v));
        Assert.Equal("test-value", v);
    }

    // -------------------------------------------------------------------------
    // Thread discovery (LoadIndexAsync scans thread files)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LoadIndex_EmptyDirectory_ReturnsEmptyList()
    {
        var index = await _store.LoadIndexAsync();
        Assert.Empty(index);
    }

    [Fact]
    public async Task LoadIndex_AfterSavingThreads_ReturnsSummaries()
    {
        var t1 = CreateThread();
        var t2 = CreateThread();
        await _store.SaveThreadAsync(t1);
        await _store.SaveThreadAsync(t2);

        var index = await _store.LoadIndexAsync();
        Assert.Equal(2, index.Count);
        Assert.Contains(index, s => s.Id == t1.Id);
        Assert.Contains(index, s => s.Id == t2.Id);
    }

    [Fact]
    public async Task LoadIndex_AfterDeletingThread_ExcludesDeleted()
    {
        var t1 = CreateThread();
        var t2 = CreateThread();
        await _store.SaveThreadAsync(t1);
        await _store.SaveThreadAsync(t2);

        _store.DeleteThread(t1.Id);

        var index = await _store.LoadIndexAsync();
        Assert.Single(index);
        Assert.Equal(t2.Id, index[0].Id);
    }

    [Fact]
    public async Task LoadIndex_IgnoresSessionFiles()
    {
        var thread = CreateThread();
        await _store.SaveThreadAsync(thread);

        // Create a fake session file that should be ignored
        var sessionFile = Path.Combine(_root, "threads", $"{thread.Id}.session.json");
        await File.WriteAllTextAsync(sessionFile, "{}");

        var index = await _store.LoadIndexAsync();
        Assert.Single(index);
        Assert.Equal(thread.Id, index[0].Id);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static SessionThread CreateThread() => new()
    {
        Id = SessionIdGenerator.NewThreadId(),
        WorkspacePath = "/workspace",
        UserId = "user1",
        OriginChannel = "console",
        Status = ThreadStatus.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        LastActiveAt = DateTimeOffset.UtcNow,
        HistoryMode = HistoryMode.Server
    };
}
