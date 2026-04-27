using DotCraft.Skills;

namespace DotCraft.Tests.Skills;

public sealed class SkillsLoaderTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-skillsloader-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void DeployBuiltInSkills_DeploysExpectedBuiltIns()
    {
        Directory.CreateDirectory(_tempRoot);
        var loader = new SkillsLoader(_tempRoot);

        loader.DeployBuiltInSkills();

        var skills = loader.ListSkills(filterUnavailable: false)
            .Where(skill => skill.Source == "builtin")
            .Select(skill => skill.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["browser-use", "create-hooks", "heartbeat", "memory", "skill-authoring"], skills);
    }

    [Fact]
    public void DeployBuiltInSkills_RemovesLegacyUnderscoreBuiltIns()
    {
        Directory.CreateDirectory(_tempRoot);
        var loader = new SkillsLoader(_tempRoot);
        var legacyDir = Path.Combine(loader.WorkspaceSkillsPath, "skill_authoring");
        Directory.CreateDirectory(legacyDir);
        File.WriteAllText(Path.Combine(legacyDir, "SKILL.md"), "legacy");
        File.WriteAllText(Path.Combine(legacyDir, ".builtin"), "0.0.0.0");

        loader.DeployBuiltInSkills();

        Assert.False(Directory.Exists(legacyDir));
        Assert.True(Directory.Exists(Path.Combine(loader.WorkspaceSkillsPath, "skill-authoring")));
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
