using Microsoft.Extensions.AI;

namespace DotCraft.Protocol;

/// <summary>
/// The central Session Core API consumed by all Channel Adapters.
/// Manages Thread/Turn/Item lifecycle, event emission, and persistence.
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Creates a new Thread for the given identity.
    /// </summary>
    /// <param name="identity">Channel and user context for the new Thread.</param>
    /// <param name="config">Optional per-thread agent configuration. Null means workspace defaults.</param>
    /// <param name="historyMode">
    /// <see cref="HistoryMode.Server"/> (default): Session Core manages conversation history.
    /// <see cref="HistoryMode.Client"/>: The adapter provides message history with each SubmitInput call.
    /// </param>
    /// <param name="threadId">
    /// Optional pre-assigned thread ID. When provided, this ID is used instead of generating a new one.
    /// The caller is responsible for ensuring uniqueness (e.g. by using <see cref="SessionIdGenerator.NewThreadId"/>).
    /// </param>
    /// <param name="displayName">
    /// Optional explicit display name for the thread. If null, a display name is automatically set
    /// from the first user message text during <see cref="SubmitInputAsync"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<SessionThread> CreateThreadAsync(
        SessionIdentity identity,
        ThreadConfiguration? config = null,
        HistoryMode historyMode = HistoryMode.Server,
        string? threadId = null,
        string? displayName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Resumes a Paused or previously inactive Thread.
    /// Loads Thread state and reconstructs agent session from persistence.
    /// </summary>
    Task<SessionThread> ResumeThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Transitions an Active Thread to Paused status.
    /// </summary>
    Task PauseThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Transitions a Thread to Archived status. Archived threads are read-only.
    /// </summary>
    Task ArchiveThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Discovers Threads matching the given identity (workspace + user + channel context).
    /// Returns summaries ordered by LastActiveAt descending.
    /// </summary>
    /// <param name="crossChannelOrigins">
    /// When non-null and non-empty, also includes threads that match workspace + userId and have
    /// <see cref="ThreadSummary.OriginChannel"/> in this list (case-insensitive), ignoring channel context.
    /// </param>
    Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(
        SessionIdentity identity,
        bool includeArchived = false,
        IReadOnlyList<string>? crossChannelOrigins = null,
        CancellationToken ct = default);

    /// <summary>
    /// Subscribes to thread-level events independently from turn execution.
    /// Multiple passive subscribers may observe the same thread concurrently.
    /// </summary>
    IAsyncEnumerable<SessionEvent> SubscribeThreadAsync(
        string threadId,
        bool replayRecent = false,
        CancellationToken ct = default);

    /// <summary>
    /// Submits user input to a Thread, starting a new Turn.
    /// Returns an async event stream that delivers Turn/Item lifecycle events.
    /// </summary>
    /// <param name="threadId">Target Thread ID.</param>
    /// <param name="content">
    /// User's input as a list of <see cref="AIContent"/> parts (text, images, etc.).
    /// For text-only input, use the <c>string</c> extension method in <see cref="SessionServiceExtensions"/>.
    /// </param>
    /// <param name="sender">Sender identity and role for group sessions. Null for single-user channels.</param>
    /// <param name="messages">
    /// Client-provided conversation history for client-managed history mode (Section 15).
    /// Null for server-managed mode (Session Core loads history from persistence).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    IAsyncEnumerable<SessionEvent> SubmitInputAsync(
        string threadId,
        IList<AIContent> content,
        SenderContext? sender = null,
        ChatMessage[]? messages = null,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves a pending approval request within a WaitingApproval Turn.
    /// Resumes agent execution with the user's decision.
    /// </summary>
    Task ResolveApprovalAsync(
        string threadId,
        string turnId,
        string requestId,
        SessionApprovalDecision decision,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels a Running or WaitingApproval Turn.
    /// </summary>
    Task CancelTurnAsync(string threadId, string turnId, CancellationToken ct = default);

    /// <summary>
    /// Changes the agent mode for a Thread (e.g., "agent" → "plan").
    /// Rebuilds the agent's tool set. Does not create a Turn.
    /// </summary>
    Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default);

    /// <summary>
    /// Updates the per-thread agent configuration (e.g., MCP servers, extensions).
    /// </summary>
    Task UpdateThreadConfigurationAsync(
        string threadId,
        ThreadConfiguration config,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the full Thread state including all Turns and Items.
    /// </summary>
    Task<SessionThread> GetThreadAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Loads the Thread into the in-memory cache (same as <see cref="GetThreadAsync"/>)
    /// and ensures the per-thread agent is built when <see cref="SessionThread.Configuration"/> is non-null.
    /// Does not change thread status, persist, or emit <c>thread/resumed</c>.
    /// Use before turn execution when the thread may have been loaded from disk only (e.g. after host restart).
    /// </summary>
    Task<SessionThread> EnsureThreadLoadedAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes a Thread and all its associated files from disk.
    /// Unlike <see cref="ArchiveThreadAsync"/>, this operation is irreversible.
    /// </summary>
    Task DeleteThreadPermanentlyAsync(string threadId, CancellationToken ct = default);

    /// <summary>
    /// Updates the display name of a Thread.
    /// </summary>
    Task RenameThreadAsync(string threadId, string displayName, CancellationToken ct = default);

    /// <summary>
    /// Optional hook invoked after a thread is successfully created and persisted (any channel).
    /// Hosts use this to notify all wire clients (e.g. broadcast <c>thread/started</c> on AppServer).
    /// </summary>
    Action<SessionThread>? ThreadCreatedForBroadcast { get; set; }

    /// <summary>
    /// Optional hook invoked after <see cref="DeleteThreadPermanentlyAsync"/> completes successfully
    /// (memory and disk state removed). Hosts use this to notify all wire clients (e.g. broadcast
    /// <c>thread/deleted</c> on AppServer).
    /// </summary>
    Action<string>? ThreadDeletedForBroadcast { get; set; }

    /// <summary>
    /// Optional hook invoked after a thread's display name is updated in Session Core (successful
    /// <see cref="RenameThreadAsync"/> or first user message auto-title). Hosts broadcast
    /// <c>thread/renamed</c> on AppServer so UIs keep thread lists in sync.
    /// </summary>
    Action<SessionThread>? ThreadRenamedForBroadcast { get; set; }
}
