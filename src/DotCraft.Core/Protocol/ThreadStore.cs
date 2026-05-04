using System.Collections.Concurrent;
using System.Text.Json;
using DotCraft.State;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol;

/// <summary>
/// Manages thread persistence under the .craft directory.
/// Canonical thread history is stored as thread JSONL under threads/active|archived while metadata and agent sessions live in SQLite.
/// </summary>
public sealed class ThreadStore
{
    private readonly ThreadMetadataStore _metadataStore;
    private readonly ThreadRolloutStore _rolloutStore;
    private readonly ThreadAttachmentStore _attachmentStore;
    private readonly ConcurrentDictionary<string, SessionThread> _threadSnapshotCache = new(StringComparer.Ordinal);

    public ThreadStore(string botPath)
        : this(botPath, null)
    {
    }

    internal ThreadStore(string botPath, StateRuntime? stateRuntime)
    {
        var runtime = stateRuntime ?? new StateRuntime(botPath);
        _metadataStore = new ThreadMetadataStore(runtime);
        _rolloutStore = new ThreadRolloutStore(botPath);
        _attachmentStore = new ThreadAttachmentStore(runtime, botPath);
        RebuildAttachmentReferences();
    }

    /// <summary>
    /// Persists a thread to canonical thread JSONL storage and upserts queryable metadata in SQLite.
    /// </summary>
    public async Task SaveThreadAsync(SessionThread thread, CancellationToken ct = default)
    {
        if (!_threadSnapshotCache.TryGetValue(thread.Id, out var previous))
        {
            previous = await _rolloutStore.LoadThreadAsync(thread.Id, ct);
            if (previous != null)
                _threadSnapshotCache[thread.Id] = CloneThreadSnapshot(previous);
        }

        var rolloutPath = await _rolloutStore.SaveThreadAsync(thread, previous, ct);
        _threadSnapshotCache[thread.Id] = CloneThreadSnapshot(thread);
        _metadataStore.UpsertThread(thread, rolloutPath);
        _attachmentStore.ReplaceThreadAttachments(thread);
    }

    /// <summary>
    /// Appends a rollback record for an already-pruned thread and updates metadata/cache.
    /// </summary>
    public async Task RollbackThreadAsync(
        SessionThread thread,
        int numTurns,
        CancellationToken ct = default)
    {
        var rolloutPath = await _rolloutStore.AppendRollbackAsync(thread, numTurns, ct);
        _threadSnapshotCache[thread.Id] = CloneThreadSnapshot(thread);
        _metadataStore.UpsertThread(thread, rolloutPath);
        _attachmentStore.ReplaceThreadAttachments(thread);
    }

    /// <summary>
    /// Loads a thread by replaying canonical thread history.
    /// </summary>
    public async Task<SessionThread?> LoadThreadAsync(string threadId, CancellationToken ct = default)
    {
        var thread = await _rolloutStore.LoadThreadAsync(threadId, ct);
        if (thread == null)
        {
            _threadSnapshotCache.TryRemove(threadId, out _);
            return null;
        }

        _threadSnapshotCache[threadId] = CloneThreadSnapshot(thread);
        return thread;
    }

    /// <summary>
    /// Deletes a thread JSONL history and metadata row.
    /// </summary>
    public void DeleteThread(string threadId)
    {
        var candidatePaths = _threadSnapshotCache.TryGetValue(threadId, out var cached)
            ? _attachmentStore.ExtractManagedImagePaths(cached)
            : _rolloutStore.LoadThreadAsync(threadId).GetAwaiter().GetResult() is { } loaded
                ? _attachmentStore.ExtractManagedImagePaths(loaded)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _threadSnapshotCache.TryRemove(threadId, out _);
        _attachmentStore.DeleteThreadReferencesAndCleanup(threadId, candidatePaths);
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
    /// Rebuilds and saves the persisted agent session from canonical thread history.
    /// </summary>
    public async Task RebuildAndSaveSessionFromThreadAsync(
        AIAgent agent,
        string threadId,
        CancellationToken ct = default)
    {
        var rebuilt = await RebuildSessionFromRolloutAsync(agent, threadId, ct);
        await SaveSessionAsync(agent, rebuilt, threadId, ct);
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
            try
            {
                var element = JsonSerializer.Deserialize<JsonElement>(sessionJson, SessionPersistenceJsonOptions.Default);
                return await agent.DeserializeSessionAsync(element, SessionPersistenceJsonOptions.Default, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Fall back to canonical rollout history when the SQLite session is
                // missing, malformed, or cannot be deserialized by the current agent.
            }
        }

        return await RebuildSessionFromRolloutAsync(agent, threadId, ct);
    }

    /// <summary>
    /// Returns true when a thread has a persisted server-side session in SQLite.
    /// </summary>
    public bool SessionFileExists(string threadId)
        => _metadataStore.SessionExists(threadId);

    /// <summary>
    /// Loads the persisted context-window usage token count for a thread.
    /// Returns null when no context usage snapshot has been recorded yet.
    /// </summary>
    public long? LoadContextUsageTokens(string threadId)
        => _metadataStore.LoadContextUsageTokens(threadId);

    /// <summary>
    /// Persists the current context-window usage token count for a thread.
    /// </summary>
    public Task SaveContextUsageTokensAsync(string threadId, long tokens, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _metadataStore.SaveContextUsageTokens(threadId, tokens);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns all persisted thread summaries from SQLite metadata, ordered by activity.
    /// </summary>
    public Task<List<ThreadSummary>> LoadIndexAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_metadataStore.LoadIndex());
    }

    public Task UpsertThreadSpawnEdgeAsync(ThreadSpawnEdge edge, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _metadataStore.UpsertThreadSpawnEdge(edge);
        return Task.CompletedTask;
    }

    public Task SetThreadSpawnEdgeStatusAsync(
        string parentThreadId,
        string childThreadId,
        string status,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _metadataStore.SetThreadSpawnEdgeStatus(parentThreadId, childThreadId, status);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ThreadSpawnEdge>> ListSubAgentChildrenAsync(
        string parentThreadId,
        bool includeClosed = false,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ThreadSpawnEdge>>(_metadataStore.ListSubAgentChildren(parentThreadId, includeClosed));
    }

    private static SessionThread CloneThreadSnapshot(SessionThread thread)
    {
        var json = JsonSerializer.Serialize(thread, SessionJsonOptions.Default);
        return JsonSerializer.Deserialize<SessionThread>(json, SessionJsonOptions.Default)
            ?? throw new InvalidOperationException($"Failed to clone thread snapshot for {thread.Id}.");
    }

    private void RebuildAttachmentReferences()
    {
        try
        {
            _attachmentStore.RebuildFromThreads(_rolloutStore.LoadAllThreads());
        }
        catch
        {
            // Attachment indexing is best-effort; missing thumbnails should not block startup.
        }
    }

    private async Task<AgentSession> RebuildSessionFromRolloutAsync(
        AIAgent agent,
        string threadId,
        CancellationToken ct)
    {
        var thread = await _rolloutStore.LoadThreadAsync(threadId, ct);
        if (thread == null)
            return await agent.CreateSessionAsync(ct);

        var history = new List<ChatMessage>();

        foreach (var turn in thread.Turns.OrderBy(t => t.StartedAt).ThenBy(t => t.Id, StringComparer.Ordinal))
        {
            foreach (var item in turn.Items)
            {
                if (item.Status != ItemStatus.Completed)
                    continue;

                if (item.Type == ItemType.UserMessage && TryBuildUserMessage(item, out var userMessage))
                {
                    history.Add(userMessage);
                }
                else if (item.Type == ItemType.AgentMessage && item.AsAgentMessage is { Text: { } agentText } &&
                         !string.IsNullOrWhiteSpace(agentText))
                {
                    history.Add(new ChatMessage(ChatRole.Assistant, agentText.Trim()));
                }
            }
        }

        if (history.Count == 0)
            return await agent.CreateSessionAsync(ct);

        return await CreateSessionWithHistoryAsync(agent, history, ct);
    }

    private static async Task<AgentSession> CreateSessionWithHistoryAsync(
        AIAgent agent,
        List<ChatMessage> history,
        CancellationToken ct)
    {
        var session = await agent.CreateSessionAsync(ct);
        session.SetInMemoryChatHistory(history, jsonSerializerOptions: SessionPersistenceJsonOptions.Default);
        return session;
    }

    private static bool TryBuildUserMessage(SessionItem item, out ChatMessage message)
    {
        message = new ChatMessage(ChatRole.User, string.Empty);
        if (item.AsUserMessage is not { } user)
            return false;

        var parts =
            user.MaterializedInputParts is { Count: > 0 } materialized ? materialized :
            user.NativeInputParts is { Count: > 0 } native ? native :
            null;

        if (parts is { Count: > 0 })
        {
            var contents = parts
                .Select(p => p.ToAIContent())
                .Where(c => c is not TextContent tc || !string.IsNullOrWhiteSpace(tc.Text))
                .ToList();
            if (contents.Count > 0)
            {
                message = new ChatMessage(ChatRole.User, (IList<AIContent>)contents);
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(user.Text))
            return false;

        message = new ChatMessage(ChatRole.User, user.Text.Trim());
        return true;
    }
}
