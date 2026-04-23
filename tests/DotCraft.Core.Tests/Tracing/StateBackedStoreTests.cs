using DotCraft.State;
using DotCraft.Protocol;
using DotCraft.Tracing;

namespace DotCraft.Tests.Tracing;

public sealed class StateBackedStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _craftPath;
    private readonly string _tracingPath;
    private readonly StateRuntime _stateRuntime;

    public StateBackedStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "state-backed-store-tests", Guid.NewGuid().ToString("N"));
        _craftPath = Path.Combine(_root, ".craft");
        _tracingPath = Path.Combine(_craftPath, "tracing");
        Directory.CreateDirectory(_tracingPath);
        _stateRuntime = new StateRuntime(_craftPath);
    }

    [Fact]
    public void TraceStore_RoundTrips_Events_And_Summary_Via_StateDb()
    {
        var writer = new TraceStore(_tracingPath, 5000, false, _stateRuntime);
        writer.Record(new TraceEvent
        {
            SessionKey = "thread-1",
            Type = TraceEventType.SessionMetadata,
            FinalSystemPrompt = "system",
            ToolNames = ["ReadFile", "EditFile"]
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "thread-1",
            Type = TraceEventType.Request,
            Content = "hello"
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "thread-1",
            Type = TraceEventType.TokenUsage,
            InputTokens = 11,
            OutputTokens = 7,
            TotalTokens = 18
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "thread-1",
            Type = TraceEventType.ToolCallCompleted,
            ToolName = "ReadFile",
            DurationMs = 42
        });
        writer.WaitForPendingPersistence();

        var reader = new TraceStore(_tracingPath, 5000, false, _stateRuntime);
        reader.LoadFromDisk();

        var session = reader.GetSession("thread-1");
        Assert.NotNull(session);
        Assert.Equal(1, session.RequestCount);
        Assert.Equal(11, session.TotalInputTokens);
        Assert.Equal(7, session.TotalOutputTokens);
        Assert.Equal(1, session.ToolCallCount);
        Assert.Equal(42, session.MaxToolDurationMs);
        Assert.Equal("system", session.FinalSystemPrompt);
        Assert.Contains("ReadFile", session.ToolNames);
        Assert.Equal(4, reader.GetEvents("thread-1").Count);

        var summary = reader.GetSummary();
        Assert.Equal(1, summary.SessionCount);
        Assert.Equal(1, summary.TotalRequests);
        Assert.Equal(18, summary.TotalTokens);
        Assert.Equal(1, summary.TotalToolCalls);
    }

    [Fact]
    public void TraceStore_RefreshFromDisk_Rebuilds_From_Shared_StateDb()
    {
        var reader = new TraceStore(_tracingPath, 5000, false, _stateRuntime);
        reader.LoadFromDisk();

        var writer = new TraceStore(_tracingPath, 5000, false, _stateRuntime);
        writer.Record(new TraceEvent
        {
            SessionKey = "shared-session",
            Type = TraceEventType.Request,
            Content = "ping"
        });
        writer.WaitForPendingPersistence();

        reader.RefreshFromDisk();

        Assert.Single(reader.GetSessions());
        Assert.Single(reader.GetEvents("shared-session"));
    }

    [Fact]
    public void TraceStore_Preserves_MaxToolDuration_When_Earlier_Event_Replaces_StartedAt()
    {
        var store = new TraceStore(_tracingPath, 5000, false, _stateRuntime);
        var sessionKey = "session-with-earlier-event";
        var later = new DateTimeOffset(2026, 4, 21, 10, 0, 0, TimeSpan.Zero);
        var earlier = later.AddMinutes(-5);

        store.Record(new TraceEvent
        {
            SessionKey = sessionKey,
            Type = TraceEventType.TokenUsage,
            Timestamp = later,
            InputTokens = 11,
            OutputTokens = 7
        });
        store.Record(new TraceEvent
        {
            SessionKey = sessionKey,
            Type = TraceEventType.ToolCallCompleted,
            Timestamp = later.AddSeconds(1),
            ToolName = "ToolA",
            DurationMs = 100
        });
        store.Record(new TraceEvent
        {
            SessionKey = sessionKey,
            Type = TraceEventType.ToolCallCompleted,
            Timestamp = later.AddSeconds(2),
            ToolName = "ToolB",
            DurationMs = 200
        });
        store.Record(new TraceEvent
        {
            SessionKey = sessionKey,
            Type = TraceEventType.ToolCallCompleted,
            Timestamp = later.AddSeconds(3),
            ToolName = "ToolC",
            DurationMs = 300
        });
        store.Record(new TraceEvent
        {
            SessionKey = sessionKey,
            Type = TraceEventType.Request,
            Timestamp = earlier,
            Content = "trigger clone with earlier event"
        });

        var session = store.GetSession(sessionKey);
        Assert.NotNull(session);
        Assert.Equal(earlier, session.StartedAt);
        Assert.Equal(11, session.TotalInputTokens);
        Assert.Equal(7, session.TotalOutputTokens);
        Assert.Equal(3, session.ToolCallCount);
        Assert.Equal(600, session.TotalToolDurationMs);
        Assert.Equal(300, session.MaxToolDurationMs);
    }

    [Fact]
    public void TokenUsageStore_RoundTrips_Records_Via_StateDb()
    {
        var writer = new TokenUsageStore(_tracingPath, stateRuntime: _stateRuntime);
        writer.Record(new TokenUsageRecord
        {
            SourceId = "qq",
            SourceMode = TokenUsageSourceModes.ServerManaged,
            SubjectKind = TokenUsageSubjectKinds.User,
            SubjectId = "u1",
            SubjectLabel = "Alice",
            SessionKey = "thread-1",
            ThreadId = "thread-1",
            InputTokens = 3,
            OutputTokens = 5
        });
        writer.Record(new TokenUsageRecord
        {
            SourceId = "qq",
            SourceMode = TokenUsageSourceModes.ServerManaged,
            SubjectKind = TokenUsageSubjectKinds.User,
            SubjectId = "u2",
            SubjectLabel = "Bob",
            ContextKind = TokenUsageContextKinds.Group,
            ContextId = "42",
            ContextLabel = "Team",
            SessionKey = "thread-2",
            ThreadId = "thread-2",
            InputTokens = 7,
            OutputTokens = 11
        });

        var reader = new TokenUsageStore(_tracingPath, stateRuntime: _stateRuntime);
        reader.LoadFromDisk();

        var summary = Assert.Single(reader.GetSourceSummaries());
        Assert.Equal("qq", summary.SourceId);
        Assert.Equal(26, summary.TotalTokens);
        Assert.Equal(2, summary.RequestCount);
        Assert.Equal(2, summary.SubjectCount);
        Assert.Equal(1, summary.ContextCount);
        Assert.Equal(TokenUsageSubjectKinds.User, summary.SubjectKind);
        Assert.Equal(TokenUsageContextKinds.Group, summary.ContextKind);

        Assert.Equal(2, reader.GetSubjectBreakdown("qq").Count);
        var context = Assert.Single(reader.GetContextBreakdown("qq"));
        Assert.Equal("42", context.Id);
        Assert.Equal("Team", context.Label);
        Assert.Equal(1, context.RelatedSubjectCount);
    }

    [Fact]
    public async Task TraceSessionBindingStore_InferBindings_For_Main_Child_And_Unbound_Sessions()
    {
        var threadStore = new ThreadStore(_craftPath, _stateRuntime);
        var thread = CreateThread();
        await threadStore.SaveThreadAsync(thread);

        var writer = new TraceStore(_tracingPath, 5000, false, _stateRuntime);
        writer.Record(new TraceEvent
        {
            SessionKey = thread.Id,
            Type = TraceEventType.Request,
            Content = "root"
        });
        writer.Record(new TraceEvent
        {
            SessionKey = $"{thread.Id}:sub:child1",
            Type = TraceEventType.Request,
            Content = "child"
        });
        writer.Record(new TraceEvent
        {
            SessionKey = "ag-ui:standalone",
            Type = TraceEventType.Request,
            Content = "standalone"
        });
        writer.WaitForPendingPersistence();

        var rootBinding = writer.DescribeSessionDeletion(thread.Id);
        Assert.Equal("threadMain", rootBinding.BindingKind);
        Assert.Equal(thread.Id, rootBinding.RootThreadId);

        var childBinding = writer.DescribeSessionDeletion($"{thread.Id}:sub:child1");
        Assert.Equal("threadChild", childBinding.BindingKind);
        Assert.Equal(thread.Id, childBinding.RootThreadId);

        var standaloneBinding = writer.DescribeSessionDeletion("ag-ui:standalone");
        Assert.Equal("unbound", standaloneBinding.BindingKind);
        Assert.Null(standaloneBinding.RootThreadId);
    }

    [Fact]
    public async Task ThreadTraceDeletionService_DeleteThreadCascade_Removes_Thread_And_Bound_Traces()
    {
        var threadStore = new ThreadStore(_craftPath, _stateRuntime);
        var traceStore = new TraceStore(_tracingPath, 5000, false, _stateRuntime);
        var persistence = new SessionPersistenceService(threadStore, traceStore);
        var thread = CreateThread();
        await threadStore.SaveThreadAsync(thread);

        InsertThreadSession(thread.Id);

        traceStore.Record(new TraceEvent
        {
            SessionKey = thread.Id,
            Type = TraceEventType.Request,
            Content = "root"
        });
        traceStore.Record(new TraceEvent
        {
            SessionKey = $"{thread.Id}:sub:child1",
            Type = TraceEventType.Request,
            Content = "child"
        });
        traceStore.WaitForPendingPersistence();

        await persistence.DeleteThreadCascadeAsync(thread.Id);

        Assert.Null(await threadStore.LoadThreadAsync(thread.Id));
        Assert.False(persistence.SessionFileExists(thread.Id));
        Assert.Null(traceStore.GetSession(thread.Id));
        Assert.Null(traceStore.GetSession($"{thread.Id}:sub:child1"));
        Assert.Equal("unbound", traceStore.DescribeSessionDeletion(thread.Id).BindingKind);
        Assert.Equal("unbound", traceStore.DescribeSessionDeletion($"{thread.Id}:sub:child1").BindingKind);

        using var connection = _stateRuntime.OpenConnection();
        using var threadCommand = connection.CreateCommand();
        threadCommand.CommandText = "SELECT COUNT(*) FROM threads WHERE thread_id = $thread_id";
        threadCommand.Parameters.AddWithValue("$thread_id", thread.Id);
        Assert.Equal(0L, (long)(threadCommand.ExecuteScalar() ?? 0L));

        using var eventCommand = connection.CreateCommand();
        eventCommand.CommandText = "SELECT COUNT(*) FROM trace_events WHERE session_key LIKE $session_key";
        eventCommand.Parameters.AddWithValue("$session_key", $"{thread.Id}%");
        Assert.Equal(0L, (long)(eventCommand.ExecuteScalar() ?? 0L));
    }

    [Fact]
    public async Task ThreadTraceDeletionService_DeleteTraceSessionAsync_Removes_Unbound_Trace_Only()
    {
        var threadStore = new ThreadStore(_craftPath, _stateRuntime);
        var traceStore = new TraceStore(_tracingPath, 5000, false, _stateRuntime);
        var persistence = new SessionPersistenceService(threadStore, traceStore);
        var thread = CreateThread();
        await threadStore.SaveThreadAsync(thread);

        traceStore.Record(new TraceEvent
        {
            SessionKey = thread.Id,
            Type = TraceEventType.Request,
            Content = "bound"
        });
        traceStore.Record(new TraceEvent
        {
            SessionKey = "ag-ui:standalone",
            Type = TraceEventType.Request,
            Content = "unbound"
        });
        traceStore.WaitForPendingPersistence();

        await persistence.DeleteTraceSessionAsync(
            "ag-ui:standalone",
            (_, _) => throw new InvalidOperationException("Bound thread deletion should not run for unbound traces."));

        Assert.NotNull(await threadStore.LoadThreadAsync(thread.Id));
        Assert.NotNull(traceStore.GetSession(thread.Id));
        Assert.Null(traceStore.GetSession("ag-ui:standalone"));
        Assert.Equal("unbound", traceStore.DescribeSessionDeletion("ag-ui:standalone").BindingKind);
    }

    [Fact]
    public async Task ThreadTraceDeletionService_DeleteTraceSessionAsync_For_ChildTrace_Deletes_Root_Thread()
    {
        var threadStore = new ThreadStore(_craftPath, _stateRuntime);
        var traceStore = new TraceStore(_tracingPath, 5000, false, _stateRuntime);
        var persistence = new SessionPersistenceService(threadStore, traceStore);
        var thread = CreateThread();
        await threadStore.SaveThreadAsync(thread);

        traceStore.Record(new TraceEvent
        {
            SessionKey = thread.Id,
            Type = TraceEventType.Request,
            Content = "root"
        });
        traceStore.Record(new TraceEvent
        {
            SessionKey = $"{thread.Id}:sub:child1",
            Type = TraceEventType.Request,
            Content = "child"
        });
        traceStore.WaitForPendingPersistence();

        string? deletedThreadId = null;
        await persistence.DeleteTraceSessionAsync(
            $"{thread.Id}:sub:child1",
            async (threadId, ct) =>
            {
                deletedThreadId = threadId;
                await persistence.DeleteThreadCascadeAsync(threadId, ct);
            });

        Assert.Equal(thread.Id, deletedThreadId);
        Assert.Null(await threadStore.LoadThreadAsync(thread.Id));
        Assert.Null(traceStore.GetSession(thread.Id));
        Assert.Null(traceStore.GetSession($"{thread.Id}:sub:child1"));
    }

    private void InsertThreadSession(string threadId)
    {
        using var connection = _stateRuntime.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO thread_sessions(thread_id, session_json, updated_at)
            VALUES ($thread_id, $session_json, $updated_at)
            """;
        command.Parameters.AddWithValue("$thread_id", threadId);
        command.Parameters.AddWithValue("$session_json", "{}");
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.UtcDateTime.ToString("O"));
        command.ExecuteNonQuery();
    }

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

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }
}
