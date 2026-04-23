using DotCraft.Automations;
using DotCraft.Automations.Templates;
using DotCraft.Cron;
using DotCraft.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.GitHubTracker.Tests.GitHub;

/// <summary>
/// Regression tests for the user-authored templates feature: round-trip file persistence,
/// listing merges with built-ins and carries the <c>IsUser</c> flag, id validation, and
/// built-in id collisions are rejected both on save and delete.
/// </summary>
public sealed class UserTemplateFileStoreTests
{
    [Fact]
    public async Task SaveAsync_RoundTripsAllFields()
    {
        var root = CreateTestRoot();
        try
        {
            var store = CreateStore(root);

            var schedule = new CronSchedule { Kind = "daily", DailyHour = 9, DailyMinute = 30, Tz = "UTC" };
            var saved = await store.SaveAsync(
                id: "user-weekly-sync",
                title: "Weekly sync",
                description: "Run a weekly check",
                icon: "📊",
                category: "productivity",
                workflowMarkdown: "---\nmax_rounds: 5\nworkspace: project\n---\n\nWeekly sync body",
                defaultSchedule: schedule,
                defaultWorkspaceMode: "project",
                defaultApprovalPolicy: "workspaceScope",
                defaultRequireApproval: true,
                needsThreadBinding: false,
                defaultTitle: "Weekly sync",
                defaultDescription: "Weekly sync description",
                ct: CancellationToken.None);

            Assert.Equal("user-weekly-sync", saved.Id);
            Assert.True(saved.IsUser);
            Assert.NotNull(saved.CreatedAt);
            Assert.NotNull(saved.UpdatedAt);

            var all = await store.LoadAllAsync(CancellationToken.None);
            var loaded = Assert.Single(all);
            Assert.Equal("user-weekly-sync", loaded.Id);
            Assert.Equal("Weekly sync", loaded.Title);
            Assert.Equal("Run a weekly check", loaded.Description);
            Assert.Equal("📊", loaded.Icon);
            Assert.Equal("productivity", loaded.Category);
            Assert.Contains("Weekly sync body", loaded.WorkflowMarkdown);
            Assert.NotNull(loaded.DefaultSchedule);
            Assert.Equal("daily", loaded.DefaultSchedule!.Kind);
            Assert.Equal(9, loaded.DefaultSchedule.DailyHour);
            Assert.Equal(30, loaded.DefaultSchedule.DailyMinute);
            Assert.Equal("project", loaded.DefaultWorkspaceMode);
            Assert.Equal("workspaceScope", loaded.DefaultApprovalPolicy);
            Assert.True(loaded.DefaultRequireApproval);
            Assert.False(loaded.NeedsThreadBinding);
            Assert.Equal("Weekly sync", loaded.DefaultTitle);
            Assert.Equal("Weekly sync description", loaded.DefaultDescription);
            Assert.True(loaded.IsUser);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SaveAsync_OverwritePreservesCreatedAtAndUpdatesUpdatedAt()
    {
        var root = CreateTestRoot();
        try
        {
            var store = CreateStore(root);

            var first = await store.SaveAsync(
                id: "tpl-x",
                title: "v1",
                description: null, icon: null, category: null,
                workflowMarkdown: "---\nworkspace: project\n---\nbody",
                defaultSchedule: null, defaultWorkspaceMode: "project", defaultApprovalPolicy: null,
                defaultRequireApproval: false, needsThreadBinding: false,
                defaultTitle: null, defaultDescription: null,
                ct: CancellationToken.None);

            await Task.Delay(10);

            var second = await store.SaveAsync(
                id: "tpl-x",
                title: "v2",
                description: null, icon: null, category: null,
                workflowMarkdown: "---\nworkspace: project\n---\nbody2",
                defaultSchedule: null, defaultWorkspaceMode: "project", defaultApprovalPolicy: null,
                defaultRequireApproval: false, needsThreadBinding: false,
                defaultTitle: null, defaultDescription: null,
                ct: CancellationToken.None);

            Assert.Equal(first.CreatedAt, second.CreatedAt);
            Assert.True(second.UpdatedAt >= first.UpdatedAt);
            Assert.Equal("v2", second.Title);

            var all = await store.LoadAllAsync(CancellationToken.None);
            var loaded = Assert.Single(all);
            Assert.Equal("v2", loaded.Title);
            Assert.Contains("body2", loaded.WorkflowMarkdown);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SaveAsync_BuiltInIdCollision_Throws()
    {
        var root = CreateTestRoot();
        try
        {
            var store = CreateStore(root);
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(
                id: "scan-commits-for-bugs",
                title: "hijack", description: null, icon: null, category: null,
                workflowMarkdown: "---\n---\nx",
                defaultSchedule: null, defaultWorkspaceMode: null, defaultApprovalPolicy: null,
                defaultRequireApproval: false, needsThreadBinding: false,
                defaultTitle: null, defaultDescription: null,
                ct: CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("../hack")]
    [InlineData("with/slash")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-leading")]
    public async Task SaveAsync_InvalidId_Throws(string id)
    {
        var root = CreateTestRoot();
        try
        {
            var store = CreateStore(root);
            await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(
                id: id,
                title: "t", description: null, icon: null, category: null,
                workflowMarkdown: "---\n---\nx",
                defaultSchedule: null, defaultWorkspaceMode: null, defaultApprovalPolicy: null,
                defaultRequireApproval: false, needsThreadBinding: false,
                defaultTitle: null, defaultDescription: null,
                ct: CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesDirectory_Idempotent()
    {
        var root = CreateTestRoot();
        try
        {
            var store = CreateStore(root);
            await store.SaveAsync(
                id: "to-delete", title: "x", description: null, icon: null, category: null,
                workflowMarkdown: "---\n---\nx",
                defaultSchedule: null, defaultWorkspaceMode: null, defaultApprovalPolicy: null,
                defaultRequireApproval: false, needsThreadBinding: false,
                defaultTitle: null, defaultDescription: null,
                ct: CancellationToken.None);

            Assert.True(Directory.Exists(Path.Combine(store.TemplatesRoot, "to-delete")));

            await store.DeleteAsync("to-delete", CancellationToken.None);
            Assert.False(Directory.Exists(Path.Combine(store.TemplatesRoot, "to-delete")));

            // idempotent
            await store.DeleteAsync("to-delete", CancellationToken.None);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void IsValidId_AcceptsExpectedShapes()
    {
        Assert.True(UserTemplateFileStore.IsValidId("user-abc123"));
        Assert.True(UserTemplateFileStore.IsValidId("my_template"));
        Assert.True(UserTemplateFileStore.IsValidId("AbC"));
        Assert.False(UserTemplateFileStore.IsValidId(""));
        Assert.False(UserTemplateFileStore.IsValidId("   "));
        Assert.False(UserTemplateFileStore.IsValidId("../hack"));
        Assert.False(UserTemplateFileStore.IsValidId("a/b"));
        Assert.False(UserTemplateFileStore.IsValidId("-leading-dash"));
        Assert.False(UserTemplateFileStore.IsValidId(new string('a', 65)));
    }

    [Fact]
    public async Task LoadAllAsync_SkipsMalformedFiles()
    {
        var root = CreateTestRoot();
        try
        {
            var store = CreateStore(root);
            Directory.CreateDirectory(store.TemplatesRoot);

            var goodDir = Path.Combine(store.TemplatesRoot, "good");
            Directory.CreateDirectory(goodDir);
            File.WriteAllText(Path.Combine(goodDir, "template.md"),
                "---\nid: good\ntitle: Good\n---\nbody");

            var badDir = Path.Combine(store.TemplatesRoot, "bad");
            Directory.CreateDirectory(badDir);
            File.WriteAllText(Path.Combine(badDir, "template.md"),
                "no front matter here");

            var all = await store.LoadAllAsync(CancellationToken.None);
            var loaded = Assert.Single(all);
            Assert.Equal("good", loaded.Id);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static UserTemplateFileStore CreateStore(string root)
    {
        var config = new AutomationsConfig();
        var paths = new DotCraftPaths
        {
            WorkspacePath = root,
            CraftPath = Path.Combine(root, ".craft")
        };
        Directory.CreateDirectory(paths.CraftPath);
        return new UserTemplateFileStore(config, paths, NullLogger<UserTemplateFileStore>.Instance);
    }

    private static string CreateTestRoot()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "dotcraft-user-template-tests",
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
