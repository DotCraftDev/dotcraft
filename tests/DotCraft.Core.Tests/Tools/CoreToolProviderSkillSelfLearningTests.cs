using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using OpenAI;
using System.ClientModel;

namespace DotCraft.Tests.Tools;

public sealed class CoreToolProviderSkillSelfLearningTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "dotcraft-coretoolprovider-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateTools_SelfLearningDisabled_DoesNotExposeSkillMutationTools()
    {
        var tools = CreateTools(new AppConfig.SelfLearningConfig { Enabled = false });

        Assert.DoesNotContain(tools, tool => string.Equals(tool.Name, "SkillManage", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateTools_SelfLearningEnabled_ExposesSingleSkillManageTool()
    {
        var tools = CreateTools(new AppConfig.SelfLearningConfig { Enabled = true, AllowDelete = false });
        var skillTools = tools
            .Where(tool => tool.Name.StartsWith("Skill", StringComparison.Ordinal))
            .Select(tool => tool.Name)
            .ToArray();

        Assert.Equal(["SkillManage"], skillTools);
    }

    [Fact]
    public void CreateTools_SelfLearningDeleteAllowed_StillExposesSingleSkillManageTool()
    {
        var tools = CreateTools(new AppConfig.SelfLearningConfig { Enabled = true, AllowDelete = true });
        var skillTools = tools
            .Where(tool => tool.Name.StartsWith("Skill", StringComparison.Ordinal))
            .Select(tool => tool.Name)
            .ToArray();

        Assert.Equal(["SkillManage"], skillTools);
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
        var context = new ToolProviderContext
        {
            Config = config,
            ChatClient = new OpenAIClient(
                    new ApiKeyCredential(config.ApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(config.EndPoint) })
                .GetChatClient(config.Model),
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
