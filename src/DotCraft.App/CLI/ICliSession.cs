using DotCraft.Protocol;

namespace DotCraft.CLI;

/// <summary>
/// Abstraction over the AppServer session backend consumed by <see cref="ReplHost"/>.
/// Implemented by <see cref="WireCliSession"/>, which communicates with a
/// <c>dotcraft app-server</c> subprocess (or remote WebSocket) over JSON-RPC 2.0.
/// </summary>
public interface ICliSession : IAsyncDisposable
{
    /// <summary>
    /// Creates a new thread and returns its ID.
    /// </summary>
    Task<string> CreateThreadAsync(SessionIdentity identity, CancellationToken ct = default);

    /// <summary>
    /// Resumes a thread and returns the full wire thread object (including turn history) for history display.
    /// </summary>
    Task<SessionWireThread> ResumeThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Finds threads matching the given identity, ordered by last-active time descending.
    /// </summary>
    Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(SessionIdentity identity, CancellationToken ct = default);

    /// <summary>
    /// Archives (soft-deletes) a thread.
    /// </summary>
    Task ArchiveThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes a thread and its associated files.
    /// </summary>
    Task DeleteThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Changes the agent mode for a thread (e.g., "agent" → "plan").
    /// </summary>
    Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default);

    /// <summary>
    /// Submits user input to the given thread and renders the turn to the console.
    /// Manages the full turn lifecycle: renderer creation, streaming, approval prompts, and cleanup.
    /// </summary>
    Task RunTurnAsync(string threadId, string input, CancellationToken ct = default);
}
