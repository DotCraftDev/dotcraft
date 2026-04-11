using DotCraft.Automations.Abstractions;
using DotCraft.GitHubTracker.GitHub;
using DotCraft.GitHubTracker.Tests.Fakes;
using DotCraft.GitHubTracker.Tests.Helpers;
using DotCraft.GitHubTracker.Tracker;
using DotCraft.GitHubTracker.Workflow;
using DotCraft.GitHubTracker.Workspace;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.GitHubTracker.Tests.GitHub;

public sealed class GitHubAutomationWorkspaceCleanupTests
{
    [Fact]
    public async Task ReconcileExpiredResourcesAsync_RemovesClosedIssueWorkspaceAndCache()
    {
        var testRoot = CreateTestRoot();
        try
        {
            var tracker = new FakeWorkItemTracker
            {
                StateSnapshots = { ["101"] = "Closed" }
            };

            var source = CreateSource(testRoot, tracker, out _);
            var workItem = OrchestratorTestHelpers.MakeIssue("101");
            var task = GitHubAutomationTask.FromWorkItem(workItem, "owner/repo");

            var workspacePath = await source.ProvisionWorkspaceAsync(task, CancellationToken.None);
            Assert.NotNull(workspacePath);
            Assert.True(Directory.Exists(workspacePath));

            await source.OnStatusChangedAsync(task, AutomationTaskStatus.AwaitingReview, CancellationToken.None);

            await source.ReconcileExpiredResourcesAsync(CancellationToken.None);
            await source.ReconcileExpiredResourcesAsync(CancellationToken.None);

            Assert.False(Directory.Exists(workspacePath));
            var allTasks = await source.GetAllTasksAsync(CancellationToken.None);
            Assert.DoesNotContain(allTasks, t => t.Id == task.Id);
        }
        finally
        {
            DeleteDirectory(testRoot);
        }
    }

    [Fact]
    public async Task ReconcileExpiredResourcesAsync_RemovesMergedPullRequestReviewState()
    {
        var testRoot = CreateTestRoot();
        try
        {
            var tracker = new FakeWorkItemTracker
            {
                StateSnapshots = { ["202"] = "Merged" }
            };

            var source = CreateSource(testRoot, tracker, out var craftPath);
            var workItem = OrchestratorTestHelpers.MakePr("202", headSha: "abc123");
            var task = GitHubAutomationTask.FromWorkItem(workItem, "owner/repo", "github-pr");

            var workspacePath = await source.ProvisionWorkspaceAsync(task, CancellationToken.None);
            Assert.NotNull(workspacePath);

            await source.OnStatusChangedAsync(task, AutomationTaskStatus.AwaitingReview, CancellationToken.None);
            await source.OnAgentCompletedAsync(task, "review complete", CancellationToken.None);

            var reviewStatePath = Path.Combine(craftPath, "review-state", $"{task.Id}.json");
            Assert.True(File.Exists(reviewStatePath));

            await source.ReconcileExpiredResourcesAsync(CancellationToken.None);

            Assert.False(Directory.Exists(workspacePath));
            Assert.False(File.Exists(reviewStatePath));
        }
        finally
        {
            DeleteDirectory(testRoot);
        }
    }

    [Fact]
    public async Task ReconcileExpiredResourcesAsync_KeepsWorkspaceForNonTerminalIssue()
    {
        var testRoot = CreateTestRoot();
        try
        {
            var tracker = new FakeWorkItemTracker
            {
                StateSnapshots = { ["303"] = "Todo" }
            };

            var source = CreateSource(testRoot, tracker, out _);
            var workItem = OrchestratorTestHelpers.MakeIssue("303");
            var task = GitHubAutomationTask.FromWorkItem(workItem, "owner/repo");

            var workspacePath = await source.ProvisionWorkspaceAsync(task, CancellationToken.None);
            Assert.NotNull(workspacePath);

            await source.OnStatusChangedAsync(task, AutomationTaskStatus.AwaitingReview, CancellationToken.None);

            await source.ReconcileExpiredResourcesAsync(CancellationToken.None);

            Assert.True(Directory.Exists(workspacePath));
            var allTasks = await source.GetAllTasksAsync(CancellationToken.None);
            Assert.Contains(allTasks, t => t.Id == task.Id);
        }
        finally
        {
            DeleteDirectory(testRoot);
        }
    }

    [Fact]
    public async Task DeleteTaskAsync_RemovesWorkspaceAndCache()
    {
        var testRoot = CreateTestRoot();
        try
        {
            var tracker = new FakeWorkItemTracker();
            var source = CreateSource(testRoot, tracker, out _);
            var workItem = OrchestratorTestHelpers.MakeIssue("404");
            var task = GitHubAutomationTask.FromWorkItem(workItem, "owner/repo");

            var workspacePath = await source.ProvisionWorkspaceAsync(task, CancellationToken.None);
            Assert.NotNull(workspacePath);

            await source.OnStatusChangedAsync(task, AutomationTaskStatus.AwaitingReview, CancellationToken.None);
            await source.DeleteTaskAsync(task.Id, CancellationToken.None);

            Assert.False(Directory.Exists(workspacePath));
            var allTasks = await source.GetAllTasksAsync(CancellationToken.None);
            Assert.DoesNotContain(allTasks, t => t.Id == task.Id);
        }
        finally
        {
            DeleteDirectory(testRoot);
        }
    }

    private static GitHubAutomationSource CreateSource(
        string testRoot,
        FakeWorkItemTracker tracker,
        out string craftPath)
    {
        craftPath = Path.Combine(testRoot, ".craft");
        Directory.CreateDirectory(craftPath);

        var config = OrchestratorTestHelpers.MakeConfig();
        config.Tracker.Repository = null;
        config.Workspace.Root = Path.Combine(testRoot, "gh-workspaces");

        var issueWorkflowLoader = new WorkflowLoader(config, NullLogger<WorkflowLoader>.Instance);
        var prWorkflowLoader = new WorkflowLoader(config, NullLogger<WorkflowLoader>.Instance);
        var workspaceManager = new WorkItemWorkspaceManager(config, NullLogger<WorkItemWorkspaceManager>.Instance);

        return new GitHubAutomationSource(
            tracker,
            workspaceManager,
            issueWorkflowLoader,
            prWorkflowLoader,
            config,
            testRoot,
            craftPath,
            NullLoggerFactory.Instance);
    }

    private static string CreateTestRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "dotcraft-gh-cleanup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
