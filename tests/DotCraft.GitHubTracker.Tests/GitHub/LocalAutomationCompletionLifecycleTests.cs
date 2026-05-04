using DotCraft.Automations;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Local;
using DotCraft.Automations.Orchestrator;
using DotCraft.Cron;
using DotCraft.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.GitHubTracker.Tests.GitHub;

public sealed class LocalAutomationCompletionLifecycleTests
{
    [Fact]
    public async Task CompletedOneShotLocalTask_StopsWorkflowAndPersistsCompleted()
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);
            var task = await CreateTaskAsync(
                harness.FileStore,
                "oneshot-complete",
                AutomationTaskStatus.Running,
                schedule: null);

            task.Status = AutomationTaskStatus.Completed;
            await harness.Source.OnAgentCompletedAsync(task, "done", CancellationToken.None);
            await harness.FileStore.SaveAsync(task, CancellationToken.None);

            Assert.True(await harness.Source.ShouldStopWorkflowAfterTurnAsync(task, CancellationToken.None));

            var persisted = await harness.FileStore.LoadAsync(task.TaskDirectory, CancellationToken.None);
            Assert.Equal(AutomationTaskStatus.Completed, persisted.Status);
            Assert.Equal("done", persisted.AgentSummary);
            Assert.Null(persisted.NextRunAt);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CompletedScheduledLocalTask_RearmsToPendingWithRefreshedNextRun()
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);
            var task = await CreateTaskAsync(
                harness.FileStore,
                "scheduled-complete",
                AutomationTaskStatus.Running,
                new CronSchedule { Kind = "every", EveryMs = 60_000 });

            task.NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await harness.FileStore.SaveAsync(task, CancellationToken.None);

            var before = DateTimeOffset.UtcNow;
            task.Status = AutomationTaskStatus.Completed;
            await harness.Source.OnAgentCompletedAsync(task, "scheduled done", CancellationToken.None);
            AutomationOrchestrator.RearmSchedule(task);
            task.Status = AutomationTaskStatus.Pending;
            await harness.Source.OnStatusChangedAsync(task, AutomationTaskStatus.Pending, CancellationToken.None);
            var after = DateTimeOffset.UtcNow;

            var persisted = await harness.FileStore.LoadAsync(task.TaskDirectory, CancellationToken.None);
            Assert.Equal(AutomationTaskStatus.Pending, persisted.Status);
            Assert.Equal("scheduled done", persisted.AgentSummary);
            Assert.NotNull(persisted.NextRunAt);
            Assert.InRange(persisted.NextRunAt!.Value, before.AddSeconds(50), after.AddSeconds(70));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task LoadAllAsync_SkipsTaskWithLegacyReviewStatus()
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);
            var taskDirectory = Path.Combine(harness.FileStore.TasksRoot, "legacy-review");
            Directory.CreateDirectory(taskDirectory);
            await File.WriteAllTextAsync(
                Path.Combine(taskDirectory, "task.md"),
                """
                ---
                id: legacy-review
                title: Legacy review task
                status: awaiting_review
                ---
                old body
                """,
                CancellationToken.None);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => harness.FileStore.LoadAsync(taskDirectory, CancellationToken.None));

            var loaded = await harness.FileStore.LoadAllAsync(CancellationToken.None);
            Assert.Empty(loaded);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task<LocalAutomationTask> CreateTaskAsync(
        LocalTaskFileStore fileStore,
        string id,
        AutomationTaskStatus status,
        CronSchedule? schedule)
    {
        var taskDirectory = Path.Combine(fileStore.TasksRoot, id);
        Directory.CreateDirectory(taskDirectory);

        var task = new LocalAutomationTask
        {
            TaskDirectory = taskDirectory,
            Id = id,
            Title = id,
            Status = status,
            SourceName = "local",
            Description = "Completion lifecycle regression test",
            Schedule = schedule,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        await fileStore.SaveAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(task.WorkflowFilePath, "Completion lifecycle workflow", CancellationToken.None);
        return task;
    }

    private static TestHarness CreateHarness(string root)
    {
        var config = new AutomationsConfig
        {
            WorkspaceRoot = Path.Combine(root, "automations"),
            PollingInterval = TimeSpan.FromSeconds(30),
            MaxConcurrentTasks = 1,
        };
        var paths = new DotCraftPaths
        {
            WorkspacePath = root,
            CraftPath = Path.Combine(root, ".craft")
        };

        Directory.CreateDirectory(Path.Combine(paths.CraftPath, "tasks"));

        var fileStore = new LocalTaskFileStore(config, paths, NullLogger<LocalTaskFileStore>.Instance);
        var workflowLoader = new LocalWorkflowLoader(NullLogger<LocalWorkflowLoader>.Instance);
        var source = new LocalAutomationSource(
            fileStore,
            workflowLoader,
            NullLoggerFactory.Instance,
            NullLogger<LocalAutomationSource>.Instance);

        return new TestHarness(fileStore, source);
    }

    private static string CreateTestRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "dotcraft-local-completion-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed record TestHarness(
        LocalTaskFileStore FileStore,
        LocalAutomationSource Source) : IDisposable
    {
        public void Dispose() => Source.Dispose();
    }
}
