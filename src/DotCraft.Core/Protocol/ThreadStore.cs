using System.Text.Json;
using Microsoft.Agents.AI;

namespace DotCraft.Protocol;

/// <summary>
/// Manages all file I/O for Session Protocol data under the .craft directory.
/// Handles Thread files and AgentSession files.
/// </summary>
public sealed class ThreadStore
{
    private readonly string _threadsDir;

    public ThreadStore(string botPath)
    {
        _threadsDir = Path.Combine(botPath, "threads");
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
    /// Deletes a Thread file (does not delete the session file).
    /// </summary>
    public void DeleteThread(string threadId)
    {
        var path = GetThreadPath(threadId);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>
    /// Deletes the agent session file (.session.json) for a Thread, if it exists.
    /// </summary>
    public void DeleteSessionFile(string threadId)
    {
        var path = GetSessionPath(threadId);
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
        CancellationToken ct = default)
    {
        var path = GetSessionPath(threadId);
        var serialized = await agent.SerializeSessionAsync(session, SessionPersistenceJsonOptions.Default, ct);
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
                stream, SessionPersistenceJsonOptions.Default, cancellationToken: ct);
            return await agent.DeserializeSessionAsync(element, SessionPersistenceJsonOptions.Default, ct);
        }

        return await agent.CreateSessionAsync(ct);
    }

    /// <summary>
    /// Returns true if a session file exists for this Thread (i.e. server-managed history).
    /// </summary>
    public bool SessionFileExists(string threadId) => File.Exists(GetSessionPath(threadId));

    // -------------------------------------------------------------------------
    // Thread discovery: scan thread files on demand
    // -------------------------------------------------------------------------

    /// <summary>
    /// Scans the threads directory and returns a <see cref="ThreadSummary"/> for every
    /// thread file found. Corrupt or unreadable files are silently skipped.
    /// This replaces the old <c>thread-index.json</c> approach and is always in sync.
    /// </summary>
    public async Task<List<ThreadSummary>> LoadIndexAsync(CancellationToken ct = default)
    {
        var entries = new List<ThreadSummary>();

        foreach (var file in Directory.GetFiles(_threadsDir, "*.json"))
        {
            if (file.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                var json = await File.ReadAllTextAsync(file, ct);
                var thread = JsonSerializer.Deserialize<SessionThread>(json, SessionJsonOptions.Default);
                if (thread != null)
                    entries.Add(ThreadSummary.FromThread(thread));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Skip corrupt files
            }
        }

        return entries;
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

    private static string MakeSafe(string key) => string.Concat(key.Split(Path.GetInvalidFileNameChars()));
}
