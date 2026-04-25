using DotCraft.Tracing;
using Microsoft.Agents.AI;

namespace DotCraft.Protocol;

public sealed record TraceSessionDeletionDescriptor(
    string SessionKey,
    string? RootThreadId,
    string BindingKind,
    string DeletionScope);

public static class SessionPersistenceDeletionScopes
{
    public const string ThreadCascade = "threadCascade";
    public const string TraceOnly = "traceOnly";
}

/// <summary>
/// Unified persistence facade for Session Core thread and tracing state.
/// ThreadStore and TraceStore remain the low-level implementations behind this facade.
/// </summary>
public sealed class SessionPersistenceService(
    ThreadStore threadStore,
    TraceStore? traceStore = null)
{
    private readonly TraceStore _traceStore = traceStore ?? new TraceStore();

    public Task SaveThreadAsync(SessionThread thread, CancellationToken ct = default)
        => threadStore.SaveThreadAsync(thread, ct);

    public Task<SessionThread?> LoadThreadAsync(string threadId, CancellationToken ct = default)
        => threadStore.LoadThreadAsync(threadId, ct);

    public Task<List<ThreadSummary>> LoadIndexAsync(CancellationToken ct = default)
        => threadStore.LoadIndexAsync(ct);

    public Task SaveSessionAsync(
        AIAgent agent,
        AgentSession session,
        string threadId,
        CancellationToken ct = default)
        => threadStore.SaveSessionAsync(agent, session, threadId, ct);

    public Task RebuildAndSaveSessionFromThreadAsync(
        AIAgent agent,
        string threadId,
        CancellationToken ct = default)
        => threadStore.RebuildAndSaveSessionFromThreadAsync(agent, threadId, ct);

    public Task<AgentSession> LoadOrCreateSessionAsync(
        AIAgent agent,
        string threadId,
        CancellationToken ct = default)
        => threadStore.LoadOrCreateSessionAsync(agent, threadId, ct);

    public Task RollbackThreadAsync(SessionThread thread, int numTurns, CancellationToken ct = default)
        => threadStore.RollbackThreadAsync(thread, numTurns, ct);

    public void DeleteSessionFile(string threadId)
        => threadStore.DeleteSessionFile(threadId);

    public bool SessionFileExists(string threadId)
        => threadStore.SessionFileExists(threadId);

    public long? LoadContextUsageTokens(string threadId)
        => threadStore.LoadContextUsageTokens(threadId);

    public Task SaveContextUsageTokensAsync(string threadId, long tokens, CancellationToken ct = default)
        => threadStore.SaveContextUsageTokensAsync(threadId, tokens, ct);

    public TraceSessionDeletionDescriptor DescribeSessionDeletion(string sessionKey)
        => _traceStore.DescribeSessionDeletion(sessionKey);

    public Dictionary<string, TraceSessionDeletionDescriptor> DescribeSessionDeletions(IEnumerable<string> sessionKeys)
        => _traceStore.DescribeSessionDeletions(sessionKeys);

    public Task DeleteThreadCascadeAsync(string threadId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        foreach (var sessionKey in _traceStore.GetBoundSessionKeys(threadId))
            _traceStore.ClearSession(sessionKey);

        threadStore.DeleteThread(threadId);
        threadStore.DeleteSessionFile(threadId);
        return Task.CompletedTask;
    }

    public async Task<bool> DeleteTraceSessionAsync(
        string sessionKey,
        Func<string, CancellationToken, Task>? deleteThreadAsync = null,
        CancellationToken ct = default)
    {
        var descriptor = DescribeSessionDeletion(sessionKey);
        if (descriptor.DeletionScope == SessionPersistenceDeletionScopes.ThreadCascade
            && !string.IsNullOrWhiteSpace(descriptor.RootThreadId))
        {
            if (deleteThreadAsync != null)
            {
                try
                {
                    await deleteThreadAsync(descriptor.RootThreadId, ct);
                    return true;
                }
                catch (KeyNotFoundException)
                {
                    await DeleteThreadCascadeAsync(descriptor.RootThreadId, ct);
                    return true;
                }
            }

            await DeleteThreadCascadeAsync(descriptor.RootThreadId, ct);
            return true;
        }

        return _traceStore.DeleteStandaloneSession(sessionKey);
    }

    public async Task DeleteTraceSessionsAsync(
        IEnumerable<string> sessionKeys,
        Func<string, CancellationToken, Task>? deleteThreadAsync = null,
        CancellationToken ct = default)
    {
        var descriptors = DescribeSessionDeletions(sessionKeys).Values.ToList();
        var boundThreadIds = descriptors
            .Where(d => d.DeletionScope == SessionPersistenceDeletionScopes.ThreadCascade
                && !string.IsNullOrWhiteSpace(d.RootThreadId))
            .Select(d => d.RootThreadId!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var standaloneSessions = descriptors
            .Where(d => d.DeletionScope == SessionPersistenceDeletionScopes.TraceOnly)
            .Select(d => d.SessionKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        foreach (var threadId in boundThreadIds)
        {
            if (deleteThreadAsync != null)
            {
                try
                {
                    await deleteThreadAsync(threadId, ct);
                    continue;
                }
                catch (KeyNotFoundException)
                {
                    // Fall through to persistence-only cleanup when the runtime thread is already gone.
                }
            }

            await DeleteThreadCascadeAsync(threadId, ct);
        }

        foreach (var sessionKey in standaloneSessions)
            _traceStore.DeleteStandaloneSession(sessionKey);
    }
}
