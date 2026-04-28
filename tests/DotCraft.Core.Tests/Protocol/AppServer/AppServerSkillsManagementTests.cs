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
}
