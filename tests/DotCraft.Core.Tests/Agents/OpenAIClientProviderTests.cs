using DotCraft.Agents;
using DotCraft.Configuration;

namespace DotCraft.Tests.Agents;

public sealed class OpenAIClientProviderTests
{
    [Fact]
    public void ResolveSubAgentModel_ExplicitSubAgentModelWins()
    {
        var config = new AppConfig
        {
            Model = "main-model",
            SubAgent = new AppConfig.SubAgentConfig
            {
                Model = "sub-model"
            }
        };
        var provider = new OpenAIClientProvider();

        var effective = provider.ResolveSubAgentModel(config, "thread-model");

        Assert.Equal("sub-model", effective);
    }

    [Fact]
    public void ResolveSubAgentModel_EmptySubAgentModelFollowsThreadModel()
    {
        var config = new AppConfig
        {
            Model = "main-model",
            SubAgent = new AppConfig.SubAgentConfig()
        };
        var provider = new OpenAIClientProvider();

        var main = provider.ResolveMainModel(config, "thread-model");
        var subAgent = provider.ResolveSubAgentModel(config, main);

        Assert.Equal("thread-model", subAgent);
    }

    [Fact]
    public void ResolveMainModel_EmptyThreadModelFallsBackToWorkspaceModel()
    {
        var config = new AppConfig { Model = "workspace-model" };
        var provider = new OpenAIClientProvider();

        var main = provider.ResolveMainModel(config, " ");

        Assert.Equal("workspace-model", main);
    }

    [Fact]
    public void ResolveConsolidationModel_EmptyConsolidationModelFallsBackToWorkspaceModel()
    {
        var config = new AppConfig
        {
            Model = "workspace-model",
            ConsolidationModel = ""
        };
        var provider = new OpenAIClientProvider();

        var consolidation = provider.ResolveConsolidationModel(config);

        Assert.Equal("workspace-model", consolidation);
    }
}
