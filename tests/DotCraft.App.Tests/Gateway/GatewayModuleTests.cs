using DotCraft.Configuration;
using DotCraft.Gateway;

namespace DotCraft.Tests.Gateway;

public sealed class GatewayModuleTests
{
    [Fact]
    public void IsEnabled_DefaultConfig_IsTrueBecauseAutomationsDefaultToEnabled()
    {
        var config = new AppConfig();
        var module = new GatewayModule();

        Assert.True(module.IsEnabled(config));
    }

    [Fact]
    public void IsEnabled_DisabledAutomationsAndNoOtherChannels_IsFalse()
    {
        var config = new AppConfig();
        config.SetSection("Automations", new DotCraft.Automations.AutomationsConfig { Enabled = false });

        var module = new GatewayModule();

        Assert.False(module.IsEnabled(config));
    }
}
