using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DotCraft.Sessions.Protocol;

/// <summary>
/// Manages all file I/O for Session Protocol data under the .craft directory.
/// Handles Thread files, AgentSession files, the Thread index, and legacy session migration.
/// </summary>
public sealed class ThreadStore
{
    private readonly string _threadsDir;
    private readonly string _sessionsDir;
    private readonly string _indexPath;

    // Compact tool results to this length, same as existing SessionStore.
    private const int ToolResultMaxChars = 500;

    public ThreadStore(string botPath)
    {
        _threadsDir = Path.Combine(botPath, "threads");
        _sessionsDir = Path.Combine(botPath, "sessions");
        _indexPath = Path.Combine(botPath, "thread-index.json");
        Directory.CreateDirectory(_threadsDir);
    }

    // -------------------------------------------------------------------------
    // Thread files: .craft/threads/{threadId}.json
    // -------------------------------------------------------------------------

    /// <summary>
    /// Persists a Thread to disk.
    /// </summary>
    public async Task SaveThreadAsync(SessionThread thread, CancellationToken ct = default)
    {
        var path = GetThreadPath(thread.Id);
        var json = JsonSerializer.Serialize(thread, SessionJsonOptions.Default);
        await File.WriteAllTextAsync(path, json, ct);
    }

    /// <summary>
    /// Loads a Thread from disk. Returns null if not found.
    /// </summary>
    public async Task<SessionThread?> LoadThreadAsync(string threadId, CancellationToken ct = default)
    {
        var path = GetThreadPath(threadId);
        if (!File.Exists(path))
            return null;

        var json = await File.ReadAllTextAsync(path, ct);
        return JsonSerializer.Deserialize<SessionThread>(json, SessionJsonOptions.Default);
    }

    /// <summary>
    /// Deletes a Thread file (does not remove from index or delete the session file).
    /// </summary>
    public void DeleteThread(string threadId)
    {
        var path = GetThreadPath(threadId);
        if (File.Exists(path))
            File.Delete(path);
    }

    // -------------------------------------------------------------------------
    // AgentSession files: .craft/threads/{threadId}.session.json
    // -------------------------------------------------------------------------

    /// <summary>
    /// Saves the AgentSession for a Thread, applying tool result compaction.
    /// </summary>
    public async Task SaveSessionAsync(
        AIAgent agent,
        AgentSession session,
        string threadId,
        bool compact = true,
        CancellationToken ct = default)
    {
        var path = GetSessionPath(threadId);
        if (compact)
            CompactSession(session);
        var serialized = await agent.SerializeSessionAsync(session, JsonSerializerOptions.Web, ct);
        await File.WriteAllTextAsync(path, serialized.GetRawText(), ct);
    }

    /// <summary>
    /// Loads an existing AgentSession for the Thread, or creates a new one.
    /// </summary>
    public async Task<AgentSession> LoadOrCreateSessionAsync(
        AIAgent agent,
        string threadId,
        CancellationToken ct = default)
    {
        var path = GetSessionPath(threadId);
        if (File.Exists(path))
        {
            await using var stream = File.OpenRead(path);
            var element = await JsonSerializer.DeserializeAsync<JsonElement>(
                stream, JsonSerializerOptions.Web, cancellationToken: ct);
            return await agent.DeserializeSessionAsync(element, cancellationToken: ct);
        }

        return await agent.CreateSessionAsync(ct);
    }

    /// <summary>
    /// Returns true if a session file exists for this Thread (i.e. server-managed history).
    /// </summary>
    public bool SessionFileExists(string threadId) => File.Exists(GetSessionPath(threadId));

    // -------------------------------------------------------------------------
    // Thread index: .craft/thread-index.json
    // -------------------------------------------------------------------------

    private readonly SemaphoreSlim _indexLock = new(1, 1);

    /// <summary>
    /// Loads all ThreadSummary entries from the index. Returns empty list on error.
    /// </summary>
    public async Task<List<ThreadSummary>> LoadIndexAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_indexPath))
            return [];

        try
        {
            var json = await File.ReadAllTextAsync(_indexPath, ct);
            var data = JsonSerializer.Deserialize<ThreadIndexData>(json, JsonSerializerOptions.Web);
            return data?.Threads ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Adds or updates the index entry for a Thread.
    /// </summary>
    public async Task UpdateIndexEntryAsync(SessionThread thread, CancellationToken ct = default)
    {
        await _indexLock.WaitAsync(ct);
        try
        {
            var entries = await LoadIndexAsync(ct);
            entries.RemoveAll(e => e.Id == thread.Id);
            entries.Add(ThreadSummary.FromThread(thread));
            await SaveIndexAsync(entries, ct);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    /// <summary>
    /// Removes the index entry for a Thread.
    /// </summary>
    public async Task RemoveIndexEntryAsync(string threadId, CancellationToken ct = default)
    {
        await _indexLock.WaitAsync(ct);
        try
        {
            var entries = await LoadIndexAsync(ct);
            entries.RemoveAll(e => e.Id == threadId);
            await SaveIndexAsync(entries, ct);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    /// <summary>
    /// Rebuilds the thread index by scanning all thread files in the threads directory.
    /// Called on startup when the index is missing or corrupt.
    /// </summary>
    public async Task RebuildIndexAsync(ILogger? logger = null, CancellationToken ct = default)
    {
        await _indexLock.WaitAsync(ct);
        try
        {
            logger?.LogWarning("Rebuilding thread index from thread files.");
            var entries = new List<ThreadSummary>();
            foreach (var file in Directory.GetFiles(_threadsDir, "*.json"))
            {
                // Skip .session.json files
                if (file.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    var json = await File.ReadAllTextAsync(file, ct);
                    var thread = JsonSerializer.Deserialize<SessionThread>(json, SessionJsonOptions.Default);
                    if (thread != null)
                        entries.Add(ThreadSummary.FromThread(thread));
                }
                catch
                {
                    // Skip corrupt files
                }
            }
            await SaveIndexAsync(entries, ct);
        }
        finally
        {
            _indexLock.Release();
        }
    }

    // -------------------------------------------------------------------------
    // Legacy session migration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lazily migrates a legacy session file (.craft/sessions/{key}.json) to the Thread format.
    /// Creates the Thread file, copies the session file, and updates the index.
    /// Returns the migrated Thread, or null if no legacy session exists for the given key.
    /// </summary>
    public async Task<SessionThread?> MigrateLegacySessionAsync(
        string legacyKey,
        string channelName,
        string? userId,
        string workspacePath,
        CancellationToken ct = default)
    {
        var legacySessionPath = GetLegacySessionPath(legacyKey);
        if (!File.Exists(legacySessionPath))
            return null;

        var thread = new SessionThread
        {
            Id = SessionIdGenerator.NewThreadId(),
            WorkspacePath = workspacePath,
            UserId = userId,
            OriginChannel = channelName,
            Status = ThreadStatus.Active,
            CreatedAt = File.GetCreationTimeUtc(legacySessionPath),
            LastActiveAt = File.GetLastWriteTimeUtc(legacySessionPath),
            HistoryMode = HistoryMode.Server,
            Metadata = new Dictionary<string, string>
            {
                ["legacySessionKey"] = legacyKey
            }
        };

        // Copy legacy session file to threads directory
        var newSessionPath = GetSessionPath(thread.Id);
        File.Copy(legacySessionPath, newSessionPath, overwrite: true);

        await SaveThreadAsync(thread, ct);
        await UpdateIndexEntryAsync(thread, ct);

        return thread;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private string GetThreadPath(string threadId)
    {
        var safe = MakeSafe(threadId);
        return Path.Combine(_threadsDir, $"{safe}.json");
    }

    private string GetSessionPath(string threadId)
    {
        var safe = MakeSafe(threadId);
        return Path.Combine(_threadsDir, $"{safe}.session.json");
    }

    private string GetLegacySessionPath(string sessionKey)
    {
        var safe = MakeSafe(sessionKey);
        return Path.Combine(_sessionsDir, $"{safe}.json");
    }

    private static string MakeSafe(string key) =>
        string.Concat(key.Split(Path.GetInvalidFileNameChars()));

    private async Task SaveIndexAsync(List<ThreadSummary> entries, CancellationToken ct)
    {
        var data = new ThreadIndexData { Threads = entries };
        var json = JsonSerializer.Serialize(data, JsonSerializerOptions.Web);
        await File.WriteAllTextAsync(_indexPath, json, ct);
    }

    private static void CompactSession(AgentSession session)
    {
        var chatHistory = session.GetService<ChatHistoryProvider>();
        if (chatHistory is not InMemoryChatHistoryProvider memoryProvider)
            return;

        foreach (var msg in memoryProvider)
        {
            if (msg.Role != ChatRole.Tool)
                continue;

            foreach (var content in msg.Contents)
            {
                if (content is TextContent textContent && textContent.Text?.Length > ToolResultMaxChars)
                    textContent.Text = textContent.Text[..ToolResultMaxChars] + "\n... (truncated)";
            }
        }
    }

    // -------------------------------------------------------------------------
    // Index data model
    // -------------------------------------------------------------------------

    private sealed class ThreadIndexData
    {
        public List<ThreadSummary> Threads { get; set; } = [];
    }
}
