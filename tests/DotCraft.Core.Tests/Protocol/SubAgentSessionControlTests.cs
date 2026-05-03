using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Protocol;
using DotCraft.Tests.Sessions.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SubAgentSessionControlTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ThreadStore _store;
    private readonly TestableSessionService _sessionService;

    public SubAgentSessionControlTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"subagent_session_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new ThreadStore(_tempDir);
        _sessionService = new TestableSessionService(_store);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task SpawnAgent_WithExternalProfile_CreatesChildThreadEdgeAndSyntheticTurn()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok", tokens: new SubAgentTokenUsage(3, 5));
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();

        var result = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions
            {
                Prompt = "inspect code",
                AgentNickname = "Inspect",
                ProfileName = "cli-run"
            },
            waitForCompletion: false,
            coordinator,
            CancellationToken.None);
        var waited = await SubAgentSessionControl.WaitAgentAsync(
            _sessionService,
            result.ChildThreadId,
            timeoutSeconds: 5,
            CancellationToken.None);

        var child = await _sessionService.GetThreadAsync(result.ChildThreadId);
        var edge = Assert.Single(await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));
        var turn = Assert.Single(child.Turns);

        Assert.Equal("running", result.Status);
        Assert.Equal("cli-run", result.ProfileName);
        Assert.Equal(CliOneshotRuntime.RuntimeTypeName, result.RuntimeType);
        Assert.False(result.SupportsSendInput);
        Assert.Equal("completed", waited.Status);
        Assert.Equal("cli ok", waited.Message);
        Assert.Equal("cli-run", child.Source.SubAgent?.ProfileName);
        Assert.Equal(CliOneshotRuntime.RuntimeTypeName, child.Source.SubAgent?.RuntimeType);
        Assert.Equal("cli-run", edge.ProfileName);
        Assert.Equal(CliOneshotRuntime.RuntimeTypeName, edge.RuntimeType);
        Assert.False(edge.SupportsSendInput);
        Assert.Equal(TurnStatus.Completed, turn.Status);
        Assert.Equal("inspect code", turn.Input?.AsUserMessage?.Text);
        Assert.Equal("cli ok", turn.Items.Last().AsAgentMessage?.Text);
        Assert.Equal(3, turn.TokenUsage?.InputTokens);
        Assert.Equal(5, turn.TokenUsage?.OutputTokens);
    }

    [Fact]
    public async Task SendInput_WithExternalProfileWithoutResume_ReturnsUnsupported()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok");
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { Prompt = "inspect code", AgentNickname = "Inspect", ProfileName = "cli-run" },
            waitForCompletion: true,
            coordinator,
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SubAgentSessionControl.SendInputAsync(
                _sessionService,
                spawned.ChildThreadId,
                "continue",
                coordinator,
                CancellationToken.None));

        Assert.Contains("does not support SendInput", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendInput_WithResumableExternalProfile_AppendsSyntheticTurnAndUsesStoredSession()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "cli ok", resultSessionId: "sess-1");
        var store = new FakeExternalCliSessionStore();
        var coordinator = CreateCoordinator(runtime, supportsResume: true, resumeEnabled: true, store);
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { Prompt = "inspect code", AgentNickname = "Inspect", ProfileName = "cli-run" },
            waitForCompletion: true,
            coordinator,
            CancellationToken.None);

        runtime.ResultText = "continued";
        runtime.ResultSessionId = "sess-2";
        var sent = await SubAgentSessionControl.SendInputAsync(
            _sessionService,
            spawned.ChildThreadId,
            "continue",
            coordinator,
            CancellationToken.None);
        var waited = await SubAgentSessionControl.WaitAgentAsync(
            _sessionService,
            sent.ChildThreadId,
            timeoutSeconds: 5,
            CancellationToken.None);
        var child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);

        Assert.True(sent.SupportsSendInput);
        Assert.Equal("continued", waited.Message);
        Assert.Equal("sess-1", runtime.LastLaunchContext?.ResumeSessionId);
        Assert.Equal(["sess-1", "sess-2"], store.RecordedSessionIds);
        Assert.Equal(2, child.Turns.Count);
        Assert.Equal("continue", child.Turns[1].Input?.AsUserMessage?.Text);
    }

    [Fact]
    public async Task CloseAgent_WithRunningExternalProfile_CancelsSyntheticTurnAndClosesEdge()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "unused")
        {
            WaitForCancellation = true
        };
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { Prompt = "inspect code", AgentNickname = "Inspect", ProfileName = "cli-run" },
            waitForCompletion: false,
            coordinator,
            CancellationToken.None);

        var closed = await SubAgentSessionControl.CloseAgentAsync(
            _sessionService,
            spawned.ChildThreadId,
            CancellationToken.None);
        await Task.Delay(100);
        var child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);
        var edge = Assert.Single(await _sessionService.ListSubAgentChildrenAsync(context.ParentThread.Id, includeClosed: true));

        Assert.Equal(ThreadSpawnEdgeStatus.Closed, closed.Status);
        Assert.Equal(ThreadSpawnEdgeStatus.Closed, edge.Status);
        Assert.Equal(TurnStatus.Cancelled, child.Turns.Single().Status);
    }

    [Fact]
    public async Task WaitAgent_WhenTimeoutExpires_ReturnsTimeoutWithoutFailingChild()
    {
        var runtime = new FakeRuntime(CliOneshotRuntime.RuntimeTypeName, "unused")
        {
            WaitForCancellation = true
        };
        var coordinator = CreateCoordinator(runtime, supportsResume: false, resumeEnabled: false);
        var context = await CreateContextAsync();
        var spawned = await SubAgentSessionControl.SpawnAgentAsync(
            context,
            new SubAgentSpawnOptions { Prompt = "inspect code", AgentNickname = "Inspect", ProfileName = "cli-run" },
            waitForCompletion: false,
            coordinator,
            CancellationToken.None);

        var waited = await SubAgentSessionControl.WaitAgentAsync(
            _sessionService,
            spawned.ChildThreadId,
            timeoutSeconds: 1,
            CancellationToken.None);
        var child = await _sessionService.GetThreadAsync(spawned.ChildThreadId);

        Assert.Equal("timeout", waited.Status);
        Assert.Contains("Timed out waiting", waited.Message, StringComparison.Ordinal);
        Assert.Equal("Inspect", waited.AgentNickname);
        Assert.Equal(TurnStatus.Running, child.Turns.Single().Status);

        await SubAgentSessionControl.CloseAgentAsync(_sessionService, spawned.ChildThreadId, CancellationToken.None);
    }


    private async Task<SubAgentSessionContext> CreateContextAsync()
    {
        var parent = await _sessionService.CreateThreadAsync(new SessionIdentity
        {
            WorkspacePath = _tempDir,
            UserId = "user",
            ChannelName = "desktop"
        });

        return new SubAgentSessionContext
        {
            SessionService = _sessionService,
            ParentThread = parent,
            ParentTurnId = "turn_001",
            RootThreadId = parent.Id,
            Depth = 0
        };
    }

    private SubAgentCoordinator CreateCoordinator(
        FakeRuntime runtime,
        bool supportsResume,
        bool resumeEnabled,
        IExternalCliSessionStore? store = null)
    {
        return new SubAgentCoordinator(
            _tempDir,
            [runtime],
            [
                new SubAgentProfile
                {
                    Name = "cli-run",
                    Runtime = CliOneshotRuntime.RuntimeTypeName,
                    WorkingDirectoryMode = "workspace",
                    Bin = "test-cli",
                    InputMode = "arg",
                    OutputFormat = "text",
                    SupportsResume = supportsResume,
                    ResumeArgTemplate = supportsResume ? "--resume {sessionId}" : null,
                    ResumeSessionIdJsonPath = supportsResume ? "session_id" : null
                }
            ],
            externalCliSessionStore: store,
            enableExternalCliSessionResume: resumeEnabled);
    }

    private sealed class FakeRuntime(
        string runtimeType,
        string resultText,
        SubAgentTokenUsage? tokens = null,
        string? resultSessionId = null) : ISubAgentRuntime
    {
        public string RuntimeType { get; } = runtimeType;

        public string ResultText { get; set; } = resultText;

        public string? ResultSessionId { get; set; } = resultSessionId;

        public bool WaitForCancellation { get; init; }

        public SubAgentLaunchContext? LastLaunchContext { get; private set; }

        public Task<SubAgentSessionHandle> CreateSessionAsync(
            SubAgentProfile profile,
            SubAgentLaunchContext context,
            CancellationToken cancellationToken)
        {
            LastLaunchContext = context;
            return Task.FromResult(new SubAgentSessionHandle(RuntimeType, profile.Name));
        }

        public Task<DotCraft.Agents.SubAgentRunResult> RunAsync(
            SubAgentSessionHandle session,
            SubAgentTaskRequest request,
            ISubAgentEventSink sink,
            CancellationToken cancellationToken)
        {
            if (WaitForCancellation)
                return WaitForCancellationAsync(cancellationToken);

            return Task.FromResult(new DotCraft.Agents.SubAgentRunResult
            {
                Text = ResultText,
                TokensUsed = tokens,
                SessionId = ResultSessionId
            });
        }

        private async Task<DotCraft.Agents.SubAgentRunResult> WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Expected cancellation.");
        }

        public Task CancelAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DisposeSessionAsync(SubAgentSessionHandle session, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeExternalCliSessionStore : IExternalCliSessionStore
    {
        private readonly Dictionary<string, ExternalCliStoredSession> _sessions = new(StringComparer.Ordinal);

        public List<string> RecordedSessionIds { get; } = [];

        public bool TryGetResumeSession(
            string profileName,
            string? label,
            string workingDirectory,
            out ExternalCliStoredSession session)
        {
            var key = BuildKey(profileName, label, workingDirectory);
            return _sessions.TryGetValue(key, out session!);
        }

        public void RecordSuccessfulRun(
            string profileName,
            string? label,
            string workingDirectory,
            string sessionId)
        {
            RecordedSessionIds.Add(sessionId);
            _sessions[BuildKey(profileName, label, workingDirectory)] =
                new ExternalCliStoredSession(profileName, label, workingDirectory, sessionId);
        }

        private static string BuildKey(string profileName, string? label, string workingDirectory) =>
            $"{profileName}\n{label}\n{workingDirectory}";
    }
}
