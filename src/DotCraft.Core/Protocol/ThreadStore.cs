using System.Text.Json;
using DotCraft.State;
using Microsoft.Agents.AI;

namespace DotCraft.Protocol;

/// <summary>
/// Manages thread persistence under the .craft directory.
/// Canonical thread history is stored as thread JSONL under threads/active|archived while metadata and agent sessions live in SQLite.
/// </summary>
public sealed class ThreadStore
{
    private readonly ThreadMetadataStore _metadataStore;
    private readonly ThreadRolloutStore _rolloutStore;

    public ThreadStore(string botPath)
        : this(botPath, null)
    {
    }

    internal ThreadStore(string botPath, StateRuntime? stateRuntime)
    {
        _metadataStore = new ThreadMetadataStore(stateRuntime ?? new StateRuntime(botPath));
        _rolloutStore = new ThreadRolloutStore(botPath);
    }

    /// <summary>
    /// Persists a thread to canonical thread JSONL storage and upserts queryable metadata in SQLite.
    /// </summary>
    public async Task SaveThreadAsync(SessionThread thread, CancellationToken ct = default)
    {
        var previous = await _rolloutStore.LoadThreadAsync(thread.Id, ct);
        var rolloutPath = await _rolloutStore.SaveThreadAsync(thread, previous, ct);
        _metadataStore.UpsertThread(thread, rolloutPath);
    }

    /// <summary>
    /// Loads a thread by replaying canonical thread history.
    /// </summary>
    public Task<SessionThread?> LoadThreadAsync(string threadId, CancellationToken ct = default)
        => _rolloutStore.LoadThreadAsync(threadId, ct);

    /// <summary>
    /// Deletes a thread JSONL history and metadata row.
    /// </summary>
    public void DeleteThread(string threadId)
    {
        _rolloutStore.DeleteThread(threadId);
        _metadataStore.DeleteThread(threadId);
    }

    /// <summary>
    /// Deletes the persisted agent session for a thread from SQLite.
    /// </summary>
    public void DeleteSessionFile(string threadId) => _metadataStore.DeleteSession(threadId);

    /// <summary>
    /// Saves the agent session JSON into SQLite.
    /// </summary>
    public async Task SaveSessionAsync(
        AIAgent agent,
        AgentSession session,
        string threadId,
        CancellationToken ct = default)
    {
        var serialized = await agent.SerializeSessionAsync(session, SessionPersistenceJsonOptions.Default, ct);
        _metadataStore.SaveSessionJson(threadId, serialized.GetRawText());
    }

    /// <summary>
    /// Loads an existing agent session from SQLite, or creates a new session when none exists.
    /// </summary>
    public async Task<AgentSession> LoadOrCreateSessionAsync(
        AIAgent agent,
        string threadId,
        CancellationToken ct = default)
    {
        var sessionJson = _metadataStore.LoadSessionJson(threadId);
        if (!string.IsNullOrWhiteSpace(sessionJson))
        {
            var element = JsonSerializer.Deserialize<JsonElement>(sessionJson, SessionPersistenceJsonOptions.Default);
            return await agent.DeserializeSessionAsync(element, SessionPersistenceJsonOptions.Default, ct);
        }

        return await agent.CreateSessionAsync(ct);
    }

    /// <summary>
    /// Returns true when a thread has a persisted server-side session in SQLite.
    /// </summary>
    public bool SessionFileExists(string threadId)
        => _metadataStore.SessionExists(threadId);

    /// <summary>
    /// Returns all persisted thread summaries from SQLite metadata, ordered by activity.
    /// </summary>
    public Task<List<ThreadSummary>> LoadIndexAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_metadataStore.LoadIndex());
    }
}
