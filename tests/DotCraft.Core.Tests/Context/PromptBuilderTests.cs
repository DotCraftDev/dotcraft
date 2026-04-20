using DotCraft.Agents;
using DotCraft.Context;
using DotCraft.Memory;
using DotCraft.Skills;

namespace DotCraft.Tests.Context;

public sealed class PromptBuilderTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-promptbuilder-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void BuildSystemPrompt_DefaultAgentPrompt_IncludesWorkingStyleGuidance()
    {
        Directory.CreateDirectory(_tempRoot);
        var builder = CreatePromptBuilder();

        var prompt = builder.BuildSystemPrompt();

        Assert.Contains("## Working Style", prompt);
        Assert.Contains("Before the first tool call in a task", prompt);
        Assert.Contains("Before making file edits", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithModePrompt_IncludesWorkingStyleGuidanceAndModeSection()
    {
        Directory.CreateDirectory(_tempRoot);
        var modeManager = new AgentModeManager();
        modeManager.SwitchMode(AgentMode.Plan);
        var builder = CreatePromptBuilder(modeManager);

        var prompt = builder.BuildSystemPrompt();

        Assert.Contains("## Working Style", prompt);
        Assert.Contains("Before the first tool call in a task", prompt);
        Assert.Contains("# Plan Mode - System Reminder", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WithSubAgentProfilesSection_InsertsItAfterIdentityAndBeforeBootstrap()
    {
        Directory.CreateDirectory(_tempRoot);
        File.WriteAllText(Path.Combine(_tempRoot, "AGENTS.md"), "bootstrap marker");

        var builder = CreatePromptBuilder(
            subAgentProfilesSection:
            """
            ## Available SubAgent Profiles

            - `dotcraft-native`: Native DotCraft subagent profile.
            """
        );

        var prompt = builder.BuildSystemPrompt();

        Assert.Contains("## Available SubAgent Profiles", prompt);

        var profilesIndex = prompt.IndexOf("## Available SubAgent Profiles", StringComparison.Ordinal);
        var bootstrapIndex = prompt.IndexOf("## AGENTS.md", StringComparison.Ordinal);
        var identityIndex = prompt.IndexOf("# DotCraft", StringComparison.Ordinal);

        Assert.True(identityIndex >= 0);
        Assert.True(profilesIndex > identityIndex);
        Assert.True(bootstrapIndex > profilesIndex);
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

    private PromptBuilder CreatePromptBuilder(
        AgentModeManager? modeManager = null,
        string? subAgentProfilesSection = null)
    {
        return new PromptBuilder(
            new MemoryStore(_tempRoot),
            new SkillsLoader(_tempRoot),
            _tempRoot,
            _tempRoot,
            modeManager: modeManager,
            subAgentProfilesSection: subAgentProfilesSection);
    }
}
