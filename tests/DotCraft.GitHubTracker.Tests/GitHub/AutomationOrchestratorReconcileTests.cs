using DotCraft.Agents;
using DotCraft.Automations;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Local;
using DotCraft.Automations.Orchestrator;
using DotCraft.Automations.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.GitHubTracker.Tests.GitHub;

public sealed class AutomationOrchestratorReconcileTests
{
    [Fact]
    public async Task TriggerImmediatePollAsync_CallsSourceReconcileBeforePollingTasks()
    {
        var workspaceRoot = CreateTestRoot();
        try
        {
            var healthySource = new RecordingAutomationSource();
            var failingSource = new RecordingAutomationSource { ThrowOnReconcile = true };
            var orchestrator = CreateOrchestrator(workspaceRoot, [failingSource, healthySource]);

            await orchestrator.TriggerImmediatePollAsync(CancellationToken.None);

            Assert.Equal(1, healthySource.ReconcileCalls);
            Assert.Equal(1, healthySource.PendingCalls);
            Assert.Equal(1, failingSource.ReconcileCalls);
            Assert.Equal(1, failingSource.PendingCalls);
        }
        finally
        {
            DeleteDirectory(workspaceRoot);
        }
    }

    private static AutomationOrchestrator CreateOrchestrator(
        string workspaceRoot,
        IEnumerable<IAutomationSource> sources)
    {
        var config = new AutomationsConfig
        {
            WorkspaceRoot = Path.Combine(workspaceRoot, "automations"),
            PollingInterval = TimeSpan.FromSeconds(30),
            MaxConcurrentTasks = 1,
        };

        return new AutomationOrchestrator(
            config,
            new AutomationWorkspaceManager(config, NullLogger<AutomationWorkspaceManager>.Instance),
            new LocalWorkflowLoader(NullLogger<LocalWorkflowLoader>.Instance),
            new ToolProfileRegistry(),
            NullLogger<AutomationOrchestrator>.Instance,
            sources);
    }

    private static string CreateTestRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "dotcraft-orchestrator-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed class RecordingAutomationSource : IAutomationSource
    {
        private readonly string _name = Guid.NewGuid().ToString("N");

        public string Name => _name;

        public string ToolProfileName => "test";

        public int ReconcileCalls { get; private set; }

        public int PendingCalls { get; private set; }

        public bool ThrowOnReconcile { get; init; }

        public void RegisterToolProfile(IToolProfileRegistry registry)
        {
        }

        public Task<IReadOnlyList<AutomationTask>> GetPendingTasksAsync(CancellationToken ct)
        {
            PendingCalls++;
            return Task.FromResult<IReadOnlyList<AutomationTask>>([]);
        }

        public Task<AutomationWorkflowDefinition> GetWorkflowAsync(AutomationTask task, CancellationToken ct) =>
            Task.FromException<AutomationWorkflowDefinition>(new NotSupportedException());

        public Task OnStatusChangedAsync(AutomationTask task, AutomationTaskStatus newStatus, CancellationToken ct) =>
            Task.CompletedTask;

        public Task OnAgentCompletedAsync(AutomationTask task, string agentSummary, CancellationToken ct) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AutomationTask>> GetAllTasksAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AutomationTask>>([]);

        public Task ReconcileExpiredResourcesAsync(CancellationToken ct)
        {
            ReconcileCalls++;
            if (ThrowOnReconcile)
                throw new InvalidOperationException("boom");

            return Task.CompletedTask;
        }
    }
}
