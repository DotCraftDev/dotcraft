using DotCraft.Automations;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Local;
using DotCraft.Automations.Orchestrator;
using DotCraft.Cron;
using DotCraft.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.GitHubTracker.Tests.GitHub;

/// <summary>
/// Regression tests for the "every schedule never fires" bug: <see cref="AutomationTask.NextRunAt"/> must be
/// persisted in <c>task.md</c> so that orchestrator poll cycles do not drift the cadence forward, and the first
/// tick for a newly-observed <c>every</c> task must be "due now" rather than "now + everyMs".
/// </summary>
public sealed class LocalAutomationScheduleTests
{
    [Fact]
    public async Task LocalTaskFileStore_RoundTripsNextRunAt()
    {
        var root = CreateTestRoot();
        try
        {
            var store = CreateStore(root);
            var taskDir = Path.Combine(store.TasksRoot, "task-rt");
            Directory.CreateDirectory(taskDir);

            var expected = new DateTimeOffset(2027, 1, 2, 3, 4, 5, TimeSpan.Zero);
            var task = new LocalAutomationTask
            {
                TaskDirectory = taskDir,
                Id = "task-rt",
                Title = "rt",
                Status = AutomationTaskStatus.Pending,
                SourceName = "local",
                Description = "body",
                Schedule = new CronSchedule { Kind = "every", EveryMs = 60_000 },
                NextRunAt = expected
            };

            await store.SaveAsync(task, CancellationToken.None);

            var reloaded = (await store.LoadAllAsync(CancellationToken.None)).Single();
            Assert.Equal(expected, reloaded.NextRunAt);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void EnsureNextRunAtInitialized_EveryKind_FirstTick_IsDueImmediately()
    {
        var created = DateTimeOffset.UtcNow.AddMinutes(-5);
        var task = new LocalAutomationTask
        {
            TaskDirectory = "/nowhere",
            Id = "id",
            Title = "t",
            Status = AutomationTaskStatus.Pending,
            SourceName = "local",
            CreatedAt = created,
            Schedule = new CronSchedule { Kind = "every", EveryMs = 60_000 }
        };

        AutomationOrchestrator.EnsureNextRunAtInitialized([task]);

        Assert.NotNull(task.NextRunAt);
        Assert.True(
            task.NextRunAt!.Value <= DateTimeOffset.UtcNow,
            $"NextRunAt ({task.NextRunAt}) should be due now or earlier, not in the future.");
    }

    [Fact]
    public void EnsureNextRunAtInitialized_EveryKind_WithInitialDelay_DefersByDelay()
    {
        var task = new LocalAutomationTask
        {
            TaskDirectory = "/nowhere",
            Id = "id",
            Title = "t",
            Status = AutomationTaskStatus.Pending,
            SourceName = "local",
            Schedule = new CronSchedule { Kind = "every", EveryMs = 60_000, InitialDelayMs = 5_000 }
        };

        var before = DateTimeOffset.UtcNow;
        AutomationOrchestrator.EnsureNextRunAtInitialized([task]);
        var after = DateTimeOffset.UtcNow;

        Assert.NotNull(task.NextRunAt);
        Assert.InRange(
            task.NextRunAt!.Value,
            before.AddMilliseconds(5_000),
            after.AddMilliseconds(5_000));
    }

    [Fact]
    public void EnsureNextRunAtInitialized_PreservesPersistedNextRunAt()
    {
        var persisted = DateTimeOffset.UtcNow.AddMinutes(2);
        var task = new LocalAutomationTask
        {
            TaskDirectory = "/nowhere",
            Id = "id",
            Title = "t",
            Status = AutomationTaskStatus.Pending,
            SourceName = "local",
            Schedule = new CronSchedule { Kind = "every", EveryMs = 60_000 },
            NextRunAt = persisted
        };

        AutomationOrchestrator.EnsureNextRunAtInitialized([task]);

        Assert.Equal(persisted, task.NextRunAt);
    }

    [Fact]
    public void EnsureNextRunAtInitialized_NoSchedule_ClearsNextRunAt()
    {
        var task = new LocalAutomationTask
        {
            TaskDirectory = "/nowhere",
            Id = "id",
            Title = "t",
            Status = AutomationTaskStatus.Pending,
            SourceName = "local",
            NextRunAt = DateTimeOffset.UtcNow.AddMinutes(5)
        };

        AutomationOrchestrator.EnsureNextRunAtInitialized([task]);

        Assert.Null(task.NextRunAt);
    }

    private static LocalTaskFileStore CreateStore(string root)
    {
        var config = new AutomationsConfig();
        var paths = new DotCraftPaths
        {
            WorkspacePath = root,
            CraftPath = Path.Combine(root, ".craft")
        };
        Directory.CreateDirectory(Path.Combine(paths.CraftPath, "tasks"));
        return new LocalTaskFileStore(config, paths, NullLogger<LocalTaskFileStore>.Instance);
    }

    private static string CreateTestRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "dotcraft-local-schedule-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
