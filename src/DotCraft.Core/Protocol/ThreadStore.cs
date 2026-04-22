using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    private readonly ConcurrentDictionary<string, SessionThread> _threadSnapshotCache = new(StringComparer.Ordinal);

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
        if (!_threadSnapshotCache.TryGetValue(thread.Id, out var previous))
        {
            previous = await _rolloutStore.LoadThreadAsync(thread.Id, ct);
            if (previous != null)
                _threadSnapshotCache[thread.Id] = CloneThreadSnapshot(previous);
        }

        var rolloutPath = await _rolloutStore.SaveThreadAsync(thread, previous, ct);
        _threadSnapshotCache[thread.Id] = CloneThreadSnapshot(thread);
        _metadataStore.UpsertThread(thread, rolloutPath);
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
        _threadSnapshotCache.TryRemove(threadId, out _);
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
    /// Returns all persisted thread summaries from SQLite metadata, ordered by activity.
    /// </summary>
    public Task<List<ThreadSummary>> LoadIndexAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_metadataStore.LoadIndex());
    }

    private static SessionThread CloneThreadSnapshot(SessionThread thread)
    {
        var json = JsonSerializer.Serialize(thread, SessionJsonOptions.Default);
        return JsonSerializer.Deserialize<SessionThread>(json, SessionJsonOptions.Default)
            ?? throw new InvalidOperationException($"Failed to clone thread snapshot for {thread.Id}.");
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

                if (item.Type == ItemType.UserMessage && item.AsUserMessage is { Text: { } userText } &&
                    !string.IsNullOrWhiteSpace(userText))
                {
                    history.Add(new ChatMessage(ChatRole.User, userText.Trim()));
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

        var element = JsonSerializer.SerializeToElement(
            new PersistedAgentSessionEnvelope
            {
                ChatHistoryProviderState = new PersistedChatHistoryProviderState { Messages = history },
                AiContextProviderState = new PersistedAiContextProviderState { Timestamp = DateTimeOffset.UtcNow }
            },
            SessionPersistenceJsonOptions.Default);
        try
        {
            return await agent.DeserializeSessionAsync(element, SessionPersistenceJsonOptions.Default, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await agent.CreateSessionAsync(ct);
        }
    }
}

internal sealed class PersistedAgentSessionEnvelope
{
    [JsonPropertyName("chatHistoryProviderState")]
    public PersistedChatHistoryProviderState ChatHistoryProviderState { get; init; } = new();

    [JsonPropertyName("aiContextProviderState")]
    public PersistedAiContextProviderState AiContextProviderState { get; init; } = new();
}

internal sealed class PersistedChatHistoryProviderState
{
    [JsonPropertyName("messages")]
    public List<ChatMessage> Messages { get; init; } = [];
}

internal sealed class PersistedAiContextProviderState
{
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }
}
