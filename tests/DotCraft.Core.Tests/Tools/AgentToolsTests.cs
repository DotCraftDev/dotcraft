using System.ComponentModel;
using System.Reflection;
using DotCraft.Tools;

namespace DotCraft.Tests.Tools;

public sealed class AgentToolsTests
{
    [Fact]
    public void SpawnSubagentDescription_RequiresMainAgentSynthesis()
    {
        var method = typeof(AgentTools).GetMethod(nameof(AgentTools.SpawnSubagent))!;
        var description = method.GetCustomAttribute<DescriptionAttribute>()!.Description;

        Assert.Contains("trusted for broad findings", description);
        Assert.Contains("main agent owns synthesis", description);
        Assert.Contains("inspect critical files when needed", description);
        Assert.Contains("before finalizing a plan", description);

        Assert.DoesNotContain("use it directly to inform your next actions", description);
    }
}
