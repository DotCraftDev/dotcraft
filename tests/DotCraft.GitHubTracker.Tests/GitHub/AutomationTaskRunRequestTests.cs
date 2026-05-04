using System.Text.Json;
using DotCraft.Agents;
using DotCraft.Automations;
using DotCraft.Automations.Abstractions;
using DotCraft.Automations.Local;
using DotCraft.Automations.Orchestrator;
using DotCraft.Automations.Protocol;
using DotCraft.Automations.Templates;
using DotCraft.Automations.Workspace;
using DotCraft.Cron;
using DotCraft.Hosting;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.GitHubTracker.Tests.GitHub;

public sealed class AutomationTaskRunRequestTests
{
    [Fact]
    public async Task HandleTaskRunAsync_RequeuesScheduledLocalTaskAsDueNow()
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);
            var task = await CreateTaskAsync(
                harness.FileStore,
                "manual-run",
                AutomationTaskStatus.Completed,
                new CronSchedule { Kind = "daily", DailyHour = 9, DailyMinute = 0 });
            task.NextRunAt = DateTimeOffset.UtcNow.AddDays(1);
            await harness.FileStore.SaveAsync(task, CancellationToken.None);

            var before = DateTimeOffset.UtcNow;
            var result = await harness.Handler.HandleTaskRunAsync(
                Request(new { taskId = task.Id, sourceName = "local" }),
                CancellationToken.None);
            var after = DateTimeOffset.UtcNow;

            var wire = Assert.IsType<AutomationTaskRunResult>(result).Task;
            Assert.Equal("manual-run", wire.Id);
            Assert.Equal("pending", wire.Status);
            Assert.NotNull(wire.NextRunAt);
            Assert.InRange(wire.NextRunAt!.Value, before.AddSeconds(-1), after);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task HandleTaskRunAsync_RequeuesUnscheduledLocalTaskAndClearsNextRunAt()
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);
            var task = await CreateTaskAsync(
                harness.FileStore,
                "manual-run-once",
                AutomationTaskStatus.Completed,
                schedule: null);
            task.NextRunAt = DateTimeOffset.UtcNow.AddDays(1);
            await harness.FileStore.SaveAsync(task, CancellationToken.None);

            var result = await harness.Handler.HandleTaskRunAsync(
                Request(new { taskId = task.Id, sourceName = "local" }),
                CancellationToken.None);

            var wire = Assert.IsType<AutomationTaskRunResult>(result).Task;
            Assert.Equal("manual-run-once", wire.Id);
            Assert.Equal("pending", wire.Status);
            Assert.Null(wire.NextRunAt);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task HandleTaskRunAsync_RejectsRunningTask()
    {
        var root = CreateTestRoot();
        try
        {
            using var harness = CreateHarness(root);
            var task = await CreateTaskAsync(
                harness.FileStore,
                "already-running",
                AutomationTaskStatus.Running,
                schedule: null);

            await Assert.ThrowsAsync<AppServerException>(() =>
                harness.Handler.HandleTaskRunAsync(
                    Request(new { taskId = task.Id, sourceName = "local" }),
                    CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task HandleTaskRunAsync_RejectsNonLocalTask()
    {
        using var harness = CreateHarness(CreateTestRoot());

        await Assert.ThrowsAsync<AppServerException>(() =>
            harness.Handler.HandleTaskRunAsync(
                Request(new { taskId = "remote", sourceName = "github" }),
                CancellationToken.None));
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
            Description = "Manual run request test",
            Schedule = schedule,
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
        };

        await fileStore.SaveAsync(task, CancellationToken.None);
        await File.WriteAllTextAsync(task.WorkflowFilePath, "Manual run workflow", CancellationToken.None);
        return task;
    }

    private static AppServerIncomingMessage Request(object parameters)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(parameters));
        return new AppServerIncomingMessage
        {
            JsonRpc = "2.0",
            Method = AppServerMethods.AutomationTaskRun,
            Params = doc.RootElement.Clone()
        };
    }

    private static TestHarness CreateHarness(string root)
    {
        var config = new AutomationsConfig
        {
            WorkspaceRoot = Path.Combine(root, "automations"),
            PollingInterval = TimeSpan.FromSeconds(30),
            MaxConcurrentTasks = 1
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
        var handler = new AutomationsRequestHandler(
            orchestrator,
            fileStore,
            null!);

        return new TestHarness(source, fileStore, handler);
    }

    private static string CreateTestRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "dotcraft-automation-run-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            if (!Directory.Exists(path))
                return;

            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
            }
            catch (UnauthorizedAccessException) when (attempt < 5)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(50 * attempt));
            }
        }
    }

    private sealed record TestHarness(
        LocalAutomationSource Source,
        LocalTaskFileStore FileStore,
        AutomationsRequestHandler Handler) : IDisposable
    {
        public void Dispose() => Source.Dispose();
    }
}
