using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;

namespace DotCraft.Tests.Tools;

public sealed class CoreToolProviderSkillSelfLearningTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-coretoolprovider-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateTools_SelfLearningDisabled_DoesNotExposeSkillMutationTools()
    {
        var tools = CreateTools(new AppConfig.SelfLearningConfig { Enabled = false });

        Assert.DoesNotContain(tools, tool => string.Equals(tool.Name, "SkillManage", StringComparison.Ordinal));
        Assert.Contains(tools, tool => string.Equals(tool.Name, "SkillView", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateTools_SelfLearningEnabled_ExposesSkillViewAndSkillManageTools()
    {
        var tools = CreateTools(new AppConfig.SelfLearningConfig { Enabled = true });
        var skillTools = tools
            .Where(tool => tool.Name.StartsWith("Skill", StringComparison.Ordinal))
            .Select(tool => tool.Name)
            .ToArray();

        Assert.Equal(["SkillView", "SkillManage"], skillTools);
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

    private List<Microsoft.Extensions.AI.AITool> CreateTools(AppConfig.SelfLearningConfig selfLearning)
    {
        Directory.CreateDirectory(_tempRoot);
        var config = new AppConfig
        {
            ApiKey = "sk-test-not-used-for-network",
            EndPoint = "https://127.0.0.1:9/v1",
            Skills = new AppConfig.SkillsConfig
            {
                SelfLearning = selfLearning
            }
        };
        var skillsLoader = new SkillsLoader(_tempRoot);
        var openAIClientProvider = new OpenAIClientProvider();
        var mainModel = openAIClientProvider.ResolveMainModel(config);
        var context = new ToolProviderContext
        {
            Config = config,
            ChatClient = openAIClientProvider.GetChatClient(config, mainModel),
            OpenAIClientProvider = openAIClientProvider,
            EffectiveMainModel = mainModel,
            WorkspacePath = _tempRoot,
            BotPath = _tempRoot,
            MemoryStore = new MemoryStore(_tempRoot),
            SkillsLoader = skillsLoader,
            SkillMutationApplier = new WorkspaceFileSkillMutationApplier(skillsLoader),
            ApprovalService = new AutoApproveApprovalService()
        };

        return new CoreToolProvider().CreateTools(context).ToList();
    }
}
