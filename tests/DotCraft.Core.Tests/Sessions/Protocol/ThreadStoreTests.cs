using DotCraft.Sessions.Protocol;

namespace DotCraft.Core.Tests.Sessions.Protocol;

/// <summary>
/// Unit tests for ThreadStore persistence, index CRUD, and legacy migration.
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
        thread.Metadata["legacySessionKey"] = "test-key";
        await _store.SaveThreadAsync(thread);

        var loaded = await _store.LoadThreadAsync(thread.Id);
        Assert.NotNull(loaded);
        Assert.True(loaded.Metadata.TryGetValue("legacySessionKey", out var v));
        Assert.Equal("test-key", v);
    }

    // -------------------------------------------------------------------------
    // Thread index CRUD
    // -------------------------------------------------------------------------

    [Fact]
    public async Task LoadIndex_EmptyDirectory_ReturnsEmptyList()
    {
        var index = await _store.LoadIndexAsync();
        Assert.Empty(index);
    }

    [Fact]
    public async Task UpdateIndexEntry_ThenLoadIndex_ContainsEntry()
    {
        var thread = CreateThread();
        await _store.UpdateIndexEntryAsync(thread);

        var index = await _store.LoadIndexAsync();
        Assert.Single(index);
        Assert.Equal(thread.Id, index[0].Id);
    }

    [Fact]
    public async Task UpdateIndexEntry_CalledTwice_DoesNotDuplicate()
    {
        var thread = CreateThread();
        await _store.UpdateIndexEntryAsync(thread);

        thread.Status = ThreadStatus.Paused;
        await _store.UpdateIndexEntryAsync(thread);

        var index = await _store.LoadIndexAsync();
        Assert.Single(index);
    }

    [Fact]
    public async Task RemoveIndexEntry_RemovesThreadFromIndex()
    {
        var thread = CreateThread();
        await _store.UpdateIndexEntryAsync(thread);
        await _store.RemoveIndexEntryAsync(thread.Id);

        var index = await _store.LoadIndexAsync();
        Assert.Empty(index);
    }

    [Fact]
    public async Task UpdateIndexEntry_MultipleThreads_AllPreserved()
    {
        var t1 = CreateThread();
        var t2 = CreateThread();
        await _store.UpdateIndexEntryAsync(t1);
        await _store.UpdateIndexEntryAsync(t2);

        var index = await _store.LoadIndexAsync();
        Assert.Equal(2, index.Count);
        Assert.Contains(index, s => s.Id == t1.Id);
        Assert.Contains(index, s => s.Id == t2.Id);
    }

    // -------------------------------------------------------------------------
    // Index rebuild
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RebuildIndex_ScansThreadFiles()
    {
        var t1 = CreateThread();
        var t2 = CreateThread();

        // Save thread files without updating the index
        await _store.SaveThreadAsync(t1);
        await _store.SaveThreadAsync(t2);

        // Ensure index file does not exist
        var indexPath = Path.Combine(_root, "thread-index.json");
        if (File.Exists(indexPath)) File.Delete(indexPath);

        await _store.RebuildIndexAsync();

        var index = await _store.LoadIndexAsync();
        Assert.Equal(2, index.Count);
        Assert.Contains(index, s => s.Id == t1.Id);
        Assert.Contains(index, s => s.Id == t2.Id);
    }

    [Fact]
    public async Task RebuildIndex_IgnoresSessionFiles()
    {
        var thread = CreateThread();
        await _store.SaveThreadAsync(thread);

        // Create a fake session file that should be ignored
        var sessionFile = Path.Combine(_root, "threads", $"{thread.Id}.session.json");
        await File.WriteAllTextAsync(sessionFile, "{}");

        await _store.RebuildIndexAsync();

        var index = await _store.LoadIndexAsync();
        Assert.Single(index);
    }

    // -------------------------------------------------------------------------
    // Legacy migration
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MigrateLegacy_WhenLegacySessionExists_CreatesThread()
    {
        // Create a fake legacy session file in the sessions directory
        const string legacyKey = "abc123";
        var sessionsDir = Path.Combine(_root, "sessions");
        Directory.CreateDirectory(sessionsDir);
        var legacyFile = Path.Combine(sessionsDir, $"{legacyKey}.json");
        await File.WriteAllTextAsync(legacyFile, "{}");

        var thread = await _store.MigrateLegacySessionAsync(
            legacyKey, "legacy", userId: "u1", workspacePath: "/workspace");

        Assert.NotNull(thread);
        Assert.Equal("legacy", thread.OriginChannel);
        Assert.Equal("u1", thread.UserId);
        Assert.Equal(ThreadStatus.Active, thread.Status);
        Assert.True(thread.Metadata.TryGetValue("legacySessionKey", out var lk));
        Assert.Equal(legacyKey, lk);
    }

    [Fact]
    public async Task MigrateLegacy_WhenNoLegacySession_ReturnsNull()
    {
        var result = await _store.MigrateLegacySessionAsync(
            "does_not_exist", "legacy", userId: "u1", workspacePath: "/workspace");
        Assert.Null(result);
    }

    [Fact]
    public async Task MigrateLegacy_CopiesSessionFile()
    {
        const string legacyKey = "migratetest";
        var sessionsDir = Path.Combine(_root, "sessions");
        Directory.CreateDirectory(sessionsDir);
        var legacyFile = Path.Combine(sessionsDir, $"{legacyKey}.json");
        var sessionContent = @"{""history"":[]}";
        await File.WriteAllTextAsync(legacyFile, sessionContent);

        var thread = await _store.MigrateLegacySessionAsync(
            legacyKey, "legacy", userId: null, workspacePath: "/ws");

        Assert.NotNull(thread);
        Assert.True(_store.SessionFileExists(thread!.Id));
    }

    [Fact]
    public async Task MigrateLegacy_UpdatesIndex()
    {
        const string legacyKey = "indextest";
        var sessionsDir = Path.Combine(_root, "sessions");
        Directory.CreateDirectory(sessionsDir);
        await File.WriteAllTextAsync(Path.Combine(sessionsDir, $"{legacyKey}.json"), "{}");

        await _store.MigrateLegacySessionAsync(legacyKey, "ch", userId: "u", workspacePath: "/");

        var index = await _store.LoadIndexAsync();
        Assert.Single(index);
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
