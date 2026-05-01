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
        Assert.Contains("## File Editing Workflow", prompt);
        Assert.Contains("Prefer `EditFile` when changing an existing file.", prompt);
        Assert.Contains("## File References", prompt);
        Assert.Contains("[label](target)", prompt);

        var workingStyleIndex = prompt.IndexOf("## Working Style", StringComparison.Ordinal);
        var editingWorkflowIndex = prompt.IndexOf("## File Editing Workflow", StringComparison.Ordinal);
        var fileReferencesIndex = prompt.IndexOf("## File References", StringComparison.Ordinal);
        Assert.True(editingWorkflowIndex > workingStyleIndex);
        Assert.True(fileReferencesIndex > editingWorkflowIndex);
        Assert.True(fileReferencesIndex > workingStyleIndex);
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
        Assert.Contains("decision-complete but compact", prompt);
        Assert.Contains("3-5 short sections", prompt);
        Assert.Contains("Mention files only when needed", prompt);
        Assert.Contains("Do not duplicate todos", prompt);
        Assert.Contains("Subagent Result Synthesis", prompt);
        Assert.Contains("do not repeat broad searches", prompt);
        Assert.Contains("Inspect critical files when needed", prompt);
        Assert.DoesNotContain("comprehensive yet concise", prompt);
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

            - `native`: Native DotCraft subagent profile.
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

    [Fact]
    public void BuildSystemPrompt_AlwaysSkillWithMissingTool_IsNotLoaded()
    {
        Directory.CreateDirectory(_tempRoot);
        WriteAlwaysSkillRequiringSkillManage();
        var builder = CreatePromptBuilder(
            toolNamesProvider: () => []);

        var prompt = builder.BuildSystemPrompt();

        Assert.DoesNotContain("### Skill: skill-authoring", prompt);
        Assert.Contains("Missing tools: SkillManage", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_AlwaysSkillWithAvailableTool_IsLoaded()
    {
        Directory.CreateDirectory(_tempRoot);
        WriteAlwaysSkillRequiringSkillManage();
        var builder = CreatePromptBuilder(
            toolNamesProvider: () => ["SkillManage"]);

        var prompt = builder.BuildSystemPrompt();

        Assert.Contains("### Skill: skill-authoring", prompt);
        Assert.Contains("Workflow marker", prompt);
        Assert.DoesNotContain("Missing tools: SkillManage", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_SkillManageAvailable_IncludesSelfLearningGuidance()
    {
        Directory.CreateDirectory(_tempRoot);
        var builder = CreatePromptBuilder(
            toolNamesProvider: () => ["SkillManage"]);

        var prompt = builder.BuildSystemPrompt();

        Assert.Contains("## Skill Self-Learning", prompt);
        Assert.Contains("Skills are procedural memory", prompt);
        Assert.Contains("about 5+ tool calls", prompt);
        Assert.Contains("patch it before finishing", prompt);
        Assert.Contains("Prefer updating or generalizing an existing skill", prompt);
        Assert.Contains("reusable task-class level", prompt);
        Assert.Contains("SkillManage(action: \"patch\")", prompt);
        Assert.DoesNotContain("Confirm with the user", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_SkillManageUnavailable_OmitsSelfLearningGuidance()
    {
        Directory.CreateDirectory(_tempRoot);
        var builder = CreatePromptBuilder(
            toolNamesProvider: () => []);

        var prompt = builder.BuildSystemPrompt();

        Assert.DoesNotContain("## Skill Self-Learning", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_OnDemandSkillWithAvailableTool_IsSummarizedButNotAlwaysLoaded()
    {
        Directory.CreateDirectory(_tempRoot);
        WriteOnDemandSkillRequiringSkillManage();
        var builder = CreatePromptBuilder(
            toolNamesProvider: () => ["SkillManage"]);

        var prompt = builder.BuildSystemPrompt();

        Assert.DoesNotContain("### Skill: skill-authoring", prompt);
        Assert.Contains("# Skills (mandatory)", prompt);
        Assert.Contains("relevant or even partially relevant", prompt);
        Assert.Contains("MUST read its SKILL.md file using the ReadFile tool", prompt);
        Assert.Contains("Only proceed without loading a skill if genuinely none", prompt);
        Assert.Contains("<name>skill-authoring</name>", prompt);
        Assert.Contains("<skill available=\"true\" always=\"false\">", prompt);
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
        string? subAgentProfilesSection = null,
        Func<IReadOnlyList<string>>? toolNamesProvider = null)
    {
        return new PromptBuilder(
            new MemoryStore(_tempRoot),
            new SkillsLoader(_tempRoot),
            _tempRoot,
            _tempRoot,
            modeManager: modeManager,
            subAgentProfilesSection: subAgentProfilesSection,
            toolNamesProvider: toolNamesProvider);
    }

    private void WriteAlwaysSkillRequiringSkillManage()
    {
        var skillDir = Path.Combine(_tempRoot, "skills", "skill-authoring");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(
            Path.Combine(skillDir, "SKILL.md"),
            """
            ---
            name: skill-authoring
            description: Skill authoring workflow
            tools: SkillManage
            always: true
            ---

            # Skill Authoring

            Workflow marker
            """);
    }

    private void WriteOnDemandSkillRequiringSkillManage()
    {
        var skillDir = Path.Combine(_tempRoot, "skills", "skill-authoring");
        Directory.CreateDirectory(skillDir);
        File.WriteAllText(
            Path.Combine(skillDir, "SKILL.md"),
            """
            ---
            name: skill-authoring
            description: Skill authoring workflow
            tools: SkillManage
            ---

            # Skill Authoring

            Workflow marker
            """);
    }
}
