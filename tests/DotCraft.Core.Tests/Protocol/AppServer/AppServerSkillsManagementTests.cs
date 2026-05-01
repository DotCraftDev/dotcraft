using DotCraft.Protocol.AppServer;
using DotCraft.Skills;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerSkillsManagementTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-appserver-skills-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SkillsList_ReturnsOptionalInterfaceMetadataAndIconDataUrl()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var skillDir = Path.Combine(craftPath, "skills", "demo-skill");
        Directory.CreateDirectory(Path.Combine(skillDir, "agents"));
        Directory.CreateDirectory(Path.Combine(skillDir, "assets"));
        File.WriteAllText(Path.Combine(skillDir, "SKILL.md"), "---\nname: demo-skill\ndescription: Long skill description\n---\n# Demo");
        File.WriteAllText(Path.Combine(skillDir, "agents", "openai.yaml"), """
            interface:
              display_name: "Demo Skill"
              short_description: "Short demo"
              icon_small: "./assets/demo.svg"
              default_prompt: "Use $demo-skill."
            """);
        File.WriteAllText(Path.Combine(skillDir, "assets", "demo.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 16\" />");

        var loader = new SkillsLoader(craftPath);
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsList, new { includeUnavailable = true }));
        using var response = harness.Transport.TryReadSent()!;
        var skill = response.RootElement.GetProperty("result").GetProperty("skills")[0];

        Assert.Equal("demo-skill", skill.GetProperty("name").GetString());
        Assert.Equal("Demo Skill", skill.GetProperty("displayName").GetString());
        Assert.Equal("Short demo", skill.GetProperty("shortDescription").GetString());
        Assert.StartsWith("data:image/svg+xml;base64,", skill.GetProperty("iconSmallDataUrl").GetString());
        Assert.Equal("Use $demo-skill.", skill.GetProperty("defaultPrompt").GetString());
    }

    [Fact]
    public async Task SkillsList_IgnoresMissingOrEscapingIconPaths()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var safeSkillDir = Path.Combine(craftPath, "skills", "safe-skill");
        var plainSkillDir = Path.Combine(craftPath, "skills", "plain-skill");
        Directory.CreateDirectory(Path.Combine(safeSkillDir, "agents"));
        Directory.CreateDirectory(plainSkillDir);
        File.WriteAllText(Path.Combine(craftPath, "skills", "secret.svg"), "<svg />");
        File.WriteAllText(Path.Combine(safeSkillDir, "SKILL.md"), "---\nname: safe-skill\ndescription: Safe\n---\n# Safe");
        File.WriteAllText(Path.Combine(safeSkillDir, "agents", "openai.yaml"), """
            interface:
              display_name: "Safe Skill"
              icon_small: "../secret.svg"
            """);
        File.WriteAllText(Path.Combine(plainSkillDir, "SKILL.md"), "---\nname: plain-skill\ndescription: Plain\n---\n# Plain");

        var loader = new SkillsLoader(craftPath);
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsList, new { includeUnavailable = true }));
        using var response = harness.Transport.TryReadSent()!;
        var skills = response.RootElement.GetProperty("result").GetProperty("skills");
        var safeSkill = skills.EnumerateArray().Single(skill => skill.GetProperty("name").GetString() == "safe-skill");
        var plainSkill = skills.EnumerateArray().Single(skill => skill.GetProperty("name").GetString() == "plain-skill");

        Assert.False(safeSkill.TryGetProperty("iconSmallDataUrl", out _));
        Assert.False(plainSkill.TryGetProperty("displayName", out _));
    }

    [Fact]
    public async Task SkillsView_ReturnsEffectiveVariantBody_WhileSkillsReadReturnsSourceRawContent()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var loader = new SkillsLoader(craftPath);
        WriteSkill(loader, "demo-skill", "Source body.");
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        var target = SkillVariantStore.CreateTarget(
            harness.Monitor.Current.Model,
            harness.Identity.WorkspacePath,
            sandboxEnabled: false,
            harness.Monitor.Current.Permissions.DefaultApprovalPolicy.ToString(),
            toolNames: null);
        var applier = new VariantSkillMutationApplier(new WorkspaceFileSkillMutationApplier(loader), loader, target);
        await applier.PatchAsync(new SkillPatchRequest("demo-skill", "Source body.", "Variant body.", null, false));
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsView, new { name = "demo-skill" }));
        using var viewResponse = harness.Transport.TryReadSent()!;
        var viewContent = viewResponse.RootElement.GetProperty("result").GetProperty("content").GetString();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsRead, new { name = "demo-skill" }));
        using var readResponse = harness.Transport.TryReadSent()!;
        var readContent = readResponse.RootElement.GetProperty("result").GetProperty("content").GetString();

        Assert.Contains("Variant body.", viewContent);
        Assert.DoesNotContain("name: demo-skill", viewContent);
        Assert.Contains("Source body.", readContent);
        Assert.Contains("name: demo-skill", readContent);
    }

    [Fact]
    public async Task SkillsList_ReportsHasVariantForCurrentVariantOnly()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var loader = new SkillsLoader(craftPath);
        WriteSkill(loader, "demo-skill", "Source body.");
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        var target = SkillVariantStore.CreateTarget(
            harness.Monitor.Current.Model,
            harness.Identity.WorkspacePath,
            sandboxEnabled: false,
            harness.Monitor.Current.Permissions.DefaultApprovalPolicy.ToString(),
            toolNames: null);
        var applier = new VariantSkillMutationApplier(new WorkspaceFileSkillMutationApplier(loader), loader, target);
        await applier.PatchAsync(new SkillPatchRequest("demo-skill", "Source body.", "Variant body.", null, false));
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsList, new { includeUnavailable = true }));
        using var listResponse = harness.Transport.TryReadSent()!;
        var skill = listResponse.RootElement.GetProperty("result").GetProperty("skills")[0];
        Assert.True(skill.GetProperty("hasVariant").GetBoolean());

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsRestoreOriginal, new { name = "demo-skill" }));
        using var restoreResponse = harness.Transport.TryReadSent()!;
        Assert.True(restoreResponse.RootElement.GetProperty("result").GetProperty("restored").GetBoolean());

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsList, new { includeUnavailable = true }));
        using var restoredListResponse = harness.Transport.TryReadSent()!;
        var restoredSkill = restoredListResponse.RootElement.GetProperty("result").GetProperty("skills")[0];
        Assert.False(restoredSkill.TryGetProperty("hasVariant", out var hasVariant)
                     && hasVariant.GetBoolean());
    }

    [Fact]
    public async Task SkillsRestoreOriginal_MakesSkillsViewFallBackToSource()
    {
        var craftPath = Path.Combine(_tempRoot, ".craft");
        var loader = new SkillsLoader(craftPath);
        WriteSkill(loader, "demo-skill", "Source body.");
        using var harness = new AppServerTestHarness(workspaceCraftPath: craftPath, skillsLoader: loader);
        var target = SkillVariantStore.CreateTarget(
            harness.Monitor.Current.Model,
            harness.Identity.WorkspacePath,
            sandboxEnabled: false,
            harness.Monitor.Current.Permissions.DefaultApprovalPolicy.ToString(),
            toolNames: null);
        var applier = new VariantSkillMutationApplier(new WorkspaceFileSkillMutationApplier(loader), loader, target);
        await applier.PatchAsync(new SkillPatchRequest("demo-skill", "Source body.", "Variant body.", null, false));
        await harness.InitializeAsync();

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsRestoreOriginal, new { name = "demo-skill" }));
        using var restoreResponse = harness.Transport.TryReadSent()!;
        Assert.True(restoreResponse.RootElement.GetProperty("result").GetProperty("restored").GetBoolean());

        await harness.ExecuteRequestAsync(harness.BuildRequest(AppServerMethods.SkillsView, new { name = "demo-skill" }));
        using var viewResponse = harness.Transport.TryReadSent()!;
        var viewContent = viewResponse.RootElement.GetProperty("result").GetProperty("content").GetString();

        Assert.Contains("Source body.", viewContent);
        Assert.DoesNotContain("Variant body.", viewContent);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for temp test directories.
        }
    }

    private static void WriteSkill(SkillsLoader loader, string name, string body)
    {
        var skillDir = Path.Combine(loader.WorkspaceSkillsPath, name);
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(
            Path.Combine(skillDir, "SKILL.md"),
            $"""
            ---
            name: {name}
            description: Test skill
            ---

            # {name}

            {body}
            """);
    }
}
