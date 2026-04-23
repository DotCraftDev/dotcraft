using DotCraft.Agents;
using DotCraft.Automations;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Local;
using DotCraft.Automations.Orchestrator;
using DotCraft.Automations.Workspace;
using DotCraft.Cron;
using DotCraft.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.GitHubTracker.Tests.GitHub;

public sealed class LocalAutomationReviewLifecycleTests
{
    [Fact]
    public async Task ApproveTaskAsync_ScheduledLocalTask_RearmsAndReturnsPending_WhenCacheCold()
    {
        var root = CreateTestRoot();
        try
        {
            var harness = CreateHarness(root);
            using var source = harness.Source;

            var task = await CreateTaskAsync(
                harness.FileStore,
                "scheduled-approve",
                AutomationTaskStatus.AwaitingReview,
                new CronSchedule { Kind = "every", EveryMs = 60_000 });

            var events = new List<AutomationTaskStatus>();
            harness.Orchestrator.OnTaskStatusChanged += (_, status) =>
            {
                events.Add(status);
                return Task.CompletedTask;
            };

            var before = DateTimeOffset.UtcNow;
            await harness.Orchestrator.ApproveTaskAsync("local", task.Id, CancellationToken.None);
            var after = DateTimeOffset.UtcNow;

            var persisted = await harness.FileStore.LoadAsync(task.TaskDirectory, CancellationToken.None);
            Assert.Equal(AutomationTaskStatus.Pending, persisted.Status);
            Assert.NotNull(persisted.NextRunAt);
            Assert.InRange(persisted.NextRunAt!.Value, before.AddSeconds(50), after.AddSeconds(70));

            var listed = (await harness.Orchestrator.GetAllTasksAsync(CancellationToken.None)).Single(t => t.Id == task.Id);
            Assert.Equal(AutomationTaskStatus.Pending, listed.Status);
            Assert.Equal(persisted.NextRunAt, listed.NextRunAt);

            Assert.Equal([AutomationTaskStatus.Pending], events);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RejectTaskAsync_ScheduledLocalTask_RearmsAndReturnsPending_WhenCacheCold()
    {
        var root = CreateTestRoot();
        try
        {
            var harness = CreateHarness(root);
            using var source = harness.Source;

            var task = await CreateTaskAsync(
                harness.FileStore,
                "scheduled-reject",
                AutomationTaskStatus.AwaitingReview,
                new CronSchedule { Kind = "every", EveryMs = 60_000 });

            var events = new List<AutomationTaskStatus>();
            harness.Orchestrator.OnTaskStatusChanged += (_, status) =>
            {
                events.Add(status);
                return Task.CompletedTask;
            };

            var before = DateTimeOffset.UtcNow;
            await harness.Orchestrator.RejectTaskAsync("local", task.Id, "not this run", CancellationToken.None);
            var after = DateTimeOffset.UtcNow;

            var persisted = await harness.FileStore.LoadAsync(task.TaskDirectory, CancellationToken.None);
            Assert.Equal(AutomationTaskStatus.Pending, persisted.Status);
            Assert.NotNull(persisted.NextRunAt);
            Assert.InRange(persisted.NextRunAt!.Value, before.AddSeconds(50), after.AddSeconds(70));

            var listed = (await harness.Orchestrator.GetAllTasksAsync(CancellationToken.None)).Single(t => t.Id == task.Id);
            Assert.Equal(AutomationTaskStatus.Pending, listed.Status);
            Assert.Equal(persisted.NextRunAt, listed.NextRunAt);

            Assert.Equal([AutomationTaskStatus.Pending], events);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ApproveTaskAsync_OneShotLocalTask_RemainsApproved()
    {
        var root = CreateTestRoot();
        try
        {
            var harness = CreateHarness(root);
            using var source = harness.Source;

            var task = await CreateTaskAsync(
                harness.FileStore,
                "oneshot-approve",
                AutomationTaskStatus.AwaitingReview,
                schedule: null);

            var events = new List<AutomationTaskStatus>();
            harness.Orchestrator.OnTaskStatusChanged += (_, status) =>
            {
                events.Add(status);
                return Task.CompletedTask;
            };

            await harness.Orchestrator.ApproveTaskAsync("local", task.Id, CancellationToken.None);

            var persisted = await harness.FileStore.LoadAsync(task.TaskDirectory, CancellationToken.None);
            Assert.Equal(AutomationTaskStatus.Approved, persisted.Status);
            Assert.Null(persisted.NextRunAt);

            var listed = (await harness.Orchestrator.GetAllTasksAsync(CancellationToken.None)).Single(t => t.Id == task.Id);
            Assert.Equal(AutomationTaskStatus.Approved, listed.Status);
            Assert.Null(listed.NextRunAt);

            Assert.Equal([AutomationTaskStatus.Approved], events);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RejectTaskAsync_OneShotLocalTask_RemainsRejected()
    {
        var root = CreateTestRoot();
        try
        {
            var harness = CreateHarness(root);
            using var source = harness.Source;

            var task = await CreateTaskAsync(
                harness.FileStore,
                "oneshot-reject",
                AutomationTaskStatus.AwaitingReview,
                schedule: null);

            var events = new List<AutomationTaskStatus>();
            harness.Orchestrator.OnTaskStatusChanged += (_, status) =>
            {
                events.Add(status);
                return Task.CompletedTask;
            };

            await harness.Orchestrator.RejectTaskAsync("local", task.Id, "bad result", CancellationToken.None);

            var persisted = await harness.FileStore.LoadAsync(task.TaskDirectory, CancellationToken.None);
            Assert.Equal(AutomationTaskStatus.Rejected, persisted.Status);
            Assert.Null(persisted.NextRunAt);

            var listed = (await harness.Orchestrator.GetAllTasksAsync(CancellationToken.None)).Single(t => t.Id == task.Id);
            Assert.Equal(AutomationTaskStatus.Rejected, listed.Status);
            Assert.Null(listed.NextRunAt);

            Assert.Equal([AutomationTaskStatus.Rejected], events);
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
            Description = "Review lifecycle regression test",
            Schedule = schedule,
            RequireApproval = true,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        await fileStore.SaveAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(task.WorkflowFilePath, "Review lifecycle workflow", CancellationToken.None);
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
        var orchestrator = new AutomationOrchestrator(
            config,
            new AutomationWorkspaceManager(config, NullLogger<AutomationWorkspaceManager>.Instance),
            workflowLoader,
            new ToolProfileRegistry(),
            NullLogger<AutomationOrchestrator>.Instance,
            [source]);

        return new TestHarness(fileStore, source, orchestrator);
    }

    private static string CreateTestRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "dotcraft-local-review-tests",
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
        LocalAutomationSource Source,
        AutomationOrchestrator Orchestrator);
}
