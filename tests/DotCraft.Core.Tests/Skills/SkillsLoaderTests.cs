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
            .Select(skill => skill.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["browser_use", "create_hooks", "heartbeat", "memory", "skill_authoring"], skills);
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
