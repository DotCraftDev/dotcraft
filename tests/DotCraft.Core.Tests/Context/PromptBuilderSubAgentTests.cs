using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Skills;

namespace DotCraft.Tests.Context;

public sealed class PromptBuilderSubAgentTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _craftDir;

    public PromptBuilderSubAgentTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"subagent_prompt_{Guid.NewGuid():N}");
        _craftDir = Path.Combine(_tempDir, ".craft");
        Directory.CreateDirectory(_craftDir);
        File.WriteAllText(Path.Combine(_craftDir, "AGENTS.md"), "AGENTS instructions");
        File.WriteAllText(Path.Combine(_craftDir, "USER.md"), "USER instructions");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void SubAgentLightPrompt_KeepsEssentialContextAndRoleInstructions()
    {
        var prompt = CreateBuilder(
                toolNames: ["ReadFile", "GrepFiles", "WebSearch"],
                roleInstructions: "Role-specific guidance.")
            .BuildSystemPrompt();

        Assert.Contains("DotCraft", prompt, StringComparison.Ordinal);
        Assert.Contains(_tempDir, prompt, StringComparison.Ordinal);
        Assert.Contains("AGENTS instructions", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("USER instructions", prompt, StringComparison.Ordinal);
        Assert.Contains("## SubAgent Context", prompt, StringComparison.Ordinal);
        Assert.Contains("ReadFile", prompt, StringComparison.Ordinal);
        Assert.Contains("GrepFiles", prompt, StringComparison.Ordinal);
        Assert.Contains("WebSearch", prompt, StringComparison.Ordinal);
        Assert.Contains("Role-specific guidance.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void SubAgentLightPrompt_OmitsHeavySections()
    {
        var prompt = CreateBuilder(
                toolNames: ["SkillManage", "SkillView"],
                roleInstructions: "Role-specific guidance.")
            .BuildSystemPrompt();

        Assert.DoesNotContain("## Skill Self-Learning", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("# Memory", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("## Available Tool Sources", prompt, StringComparison.Ordinal);
    }

    private PromptBuilder CreateBuilder(IReadOnlyList<string> toolNames, string roleInstructions) =>
        new(
            new MemoryStore(_craftDir),
            new SkillsLoader(_craftDir),
            _craftDir,
            _tempDir,
            sandboxEnabled: false,
            deferredMcpServerNames: ["example"],
            toolNamesProvider: () => toolNames,
            promptProfile: SubAgentPromptProfiles.Light,
            roleInstructions: roleInstructions);
}
