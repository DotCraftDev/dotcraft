using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Sessions;
using DotCraft.Skills;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionServiceContextUsageSnapshotTests : IDisposable
{
    private readonly string _tempDir;

    public SessionServiceContextUsageSnapshotTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ContextUsage_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public async Task TryGetContextUsageSnapshot_ReturnsNull_ForBlankThreadId()
    {
        await using var agentFactory = CreateAgentFactory();
        var service = CreateSessionService(agentFactory);

        Assert.Null(service.TryGetContextUsageSnapshot(""));
        Assert.Null(service.TryGetContextUsageSnapshot("   "));
    }

    [Fact]
    public async Task TryGetContextUsageSnapshot_ReturnsNull_WhenTrackerDoesNotExist()
    {
        const string threadId = "thread-missing";

        await using var agentFactory = CreateAgentFactory();
        var service = CreateSessionService(agentFactory);

        var snapshot = service.TryGetContextUsageSnapshot(threadId);

        Assert.Null(snapshot);
        Assert.Null(agentFactory.TryGetTokenTracker(threadId));
    }

    [Fact]
    public async Task TryGetContextUsageSnapshot_DoesNotCreateTracker_WhenReadingMissingSnapshot()
    {
        const string threadId = "thread-readonly";

        await using var agentFactory = CreateAgentFactory();
        var service = CreateSessionService(agentFactory);

        _ = service.TryGetContextUsageSnapshot(threadId);

        Assert.Null(agentFactory.TryGetTokenTracker(threadId));
    }

    [Fact]
    public async Task TryGetContextUsageSnapshot_ReturnsThresholdSnapshot_WhenTrackerExists()
    {
        const string threadId = "thread-active";

        await using var agentFactory = CreateAgentFactory();
        var service = CreateSessionService(agentFactory);
        var tracker = agentFactory.GetOrCreateTokenTracker(threadId);
        tracker.Update(12_345, 67);

        var snapshot = service.TryGetContextUsageSnapshot(threadId);

        Assert.NotNull(snapshot);

        var threshold = agentFactory.CompactionPipeline.EvaluateThreshold(tracker.LastInputTokens);
        Assert.Equal(threshold.Tokens, snapshot!.Tokens);
        Assert.Equal(agentFactory.CompactionPipeline.EffectiveContextWindow, snapshot.ContextWindow);
        Assert.Equal(threshold.AutoThreshold, snapshot.AutoCompactThreshold);
        Assert.Equal(threshold.WarningThreshold, snapshot.WarningThreshold);
        Assert.Equal(threshold.ErrorThreshold, snapshot.ErrorThreshold);
        Assert.Equal(threshold.PercentLeft, snapshot.PercentLeft);
    }

    private SessionService CreateSessionService(AgentFactory agentFactory)
    {
        var store = new ThreadStore(_tempDir);
        var persistence = new SessionPersistenceService(store);
        var defaultAgent = agentFactory.CreateAgentForMode(AgentMode.Agent);
        return new SessionService(agentFactory, defaultAgent, persistence, new SessionGate());
    }

    private AgentFactory CreateAgentFactory()
    {
        var config = new AppConfig
        {
            ApiKey = "sk-test-not-used-for-network",
            EndPoint = "https://127.0.0.1:9/v1"
        };
        var memory = new MemoryStore(_tempDir);
        var skills = new SkillsLoader(_tempDir);
        return new AgentFactory(
            dotcraftPath: _tempDir,
            workspacePath: _tempDir,
            config: config,
            memoryStore: memory,
            skillsLoader: skills,
            approvalService: new AutoApproveApprovalService(),
            blacklist: null,
            toolProviders: Array.Empty<IAgentToolProvider>());
    }
}
