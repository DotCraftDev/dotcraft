using System.Reflection;
using System.Text.Json;
using DotCraft.Automations;
using DotCraft.Automations.Protocol;
using DotCraft.Automations.Templates;
using DotCraft.Hosting;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotCraft.GitHubTracker.Tests.GitHub;

public sealed class LocalTaskTemplatesLocalizationTests
{
    [Fact]
    public void ForLocale_ZhHans_ReturnsLocalizedBuiltIns()
    {
        var template = LocalTaskTemplates.ForLocale("zh-Hans")
            .Single(t => t.Id == "scan-commits-for-bugs");

        Assert.Equal("扫描近期提交中的潜在缺陷", template.Title);
        Assert.Equal("扫描近期提交中的潜在缺陷", template.DefaultTitle);
        Assert.Contains("## 任务", template.WorkflowMarkdown);
        Assert.Contains("完成后，请调用", template.WorkflowMarkdown);
    }

    [Fact]
    public void ForLocale_UnknownLocale_FallsBackToEnglish()
    {
        var template = LocalTaskTemplates.ForLocale("fr-FR")
            .Single(t => t.Id == "scan-commits-for-bugs");

        Assert.Equal("Scan recent commits for bugs", template.Title);
        Assert.Contains("## Task", template.WorkflowMarkdown);
    }

    [Fact]
    public async Task TemplateList_ZhHans_LocalizesBuiltInsButLeavesUserTemplatesUntouched()
    {
        var root = CreateTestRoot();
        try
        {
            var store = CreateStore(root);
            await store.SaveAsync(
                id: "user-english-template",
                title: "My English template",
                description: "Keep my copy as written",
                icon: "📌",
                category: "custom",
                workflowMarkdown: "---\nmax_rounds: 5\nworkspace: project\n---\n\nUser workflow",
                defaultSchedule: null,
                defaultWorkspaceMode: "project",
                defaultApprovalPolicy: "workspaceScope",
                needsThreadBinding: false,
                defaultTitle: "My default title",
                defaultDescription: "My default description",
                ct: CancellationToken.None);

            var handler = new AutomationsRequestHandler(null!, null!, store);
            var result = (AutomationTemplateListResult)(await handler.HandleTemplateListAsync(
                new AppServerIncomingMessage
                {
                    Params = JsonSerializer.SerializeToElement(
                        new { locale = "zh-Hans" },
                        SessionWireJsonOptions.Default)
                },
                CancellationToken.None))!;

            var builtIn = result.Templates.Single(t => t.Id == "scan-commits-for-bugs");
            Assert.Equal("扫描近期提交中的潜在缺陷", builtIn.Title);

            var user = result.Templates.Single(t => t.Id == "user-english-template");
            Assert.True(user.IsUser);
            Assert.Equal("My English template", user.Title);
            Assert.Equal("Keep my copy as written", user.Description);
            Assert.Equal("My default title", user.DefaultTitle);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void GenerateTaskId_NonAsciiTitleUsesTemplateIdFallback()
    {
        var method = typeof(AutomationsRequestHandler).GetMethod(
            "GenerateTaskId",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var id = (string)method!.Invoke(null, ["扫描近期提交中的潜在缺陷", "scan-commits-for-bugs"])!;

        Assert.StartsWith("scan-commits-for-bugs-", id);
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
            "dotcraft-localized-template-tests",
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
