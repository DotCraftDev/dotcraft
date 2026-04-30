using DotCraft.Hub;

namespace DotCraft.Tests.Hub;

public sealed class HubPathsTests
{
    [Fact]
    public void Resolve_UsesGlobalCraftHubDirectory()
    {
        var userProfile = Path.Combine(Path.GetTempPath(), "DotCraftHubPaths_" + Guid.NewGuid().ToString("N"));

        var paths = HubPaths.Resolve(userProfile);

        Assert.Equal(Path.Combine(userProfile, ".craft"), paths.CraftHomePath);
        Assert.Equal(Path.Combine(userProfile, ".craft", "hub"), paths.HubStatePath);
        Assert.Equal(Path.Combine(userProfile, ".craft", "hub", "hub.lock"), paths.LockFilePath);
        Assert.Equal(Path.Combine(userProfile, ".craft", "config.json"), paths.GlobalConfigPath);
    }
}
