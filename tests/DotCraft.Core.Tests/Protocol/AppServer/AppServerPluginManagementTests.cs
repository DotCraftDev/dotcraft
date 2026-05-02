using DotCraft.Configuration;
using DotCraft.Plugins;
using DotCraft.Protocol.AppServer;
using DotCraft.Skills;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

public sealed class AppServerPluginManagementTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"plugin_management_{Guid.NewGuid():N}");
    private readonly string _workspaceCraftPath;

    public AppServerPluginManagementTests()
    {
        _workspaceCraftPath = Path.Combine(_tempRoot, ".craft");
        Directory.CreateDirectory(_workspaceCraftPath);
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
            // Best-effort cleanup.
        }
    }

    [Fact]
    public async Task Initialize_ReportsPluginManagementCapability()
    {
        using var harness = CreateHarness();
        using var init = await harness.InitializeAsync();

        Assert.True(init.RootElement
            .GetProperty("result")
            .GetProperty("capabilities")
            .GetProperty("pluginManagement")
            .GetBoolean());
    }

    [Fact]
    public async Task PluginList_ReturnsBrowserUseContents()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = Assert.Single(response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray());
        Assert.Equal("browser-use", plugin.GetProperty("id").GetString());
        Assert.False(plugin.GetProperty("enabled").GetBoolean());
        Assert.False(plugin.GetProperty("installed").GetBoolean());
        Assert.True(plugin.GetProperty("installable").GetBoolean());
        Assert.Equal("Browser Use", plugin.GetProperty("displayName").GetString());
        Assert.Contains(
            plugin.GetProperty("functions").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "NodeReplJs");
        Assert.Contains(
            plugin.GetProperty("skills").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "browser-use");
    }

    [Fact]
    public async Task PluginInstall_DeploysBrowserUseAndEnablesContents()
    {
        var loader = CreateSkillsLoader(new AppConfig());
        using var harness = CreateHarness(loader: loader);
        await harness.InitializeAsync(configChange: true);

        var msg = harness.BuildRequest(AppServerMethods.PluginInstall, new { id = "browser-use" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = response.RootElement.GetProperty("result").GetProperty("plugin");
        Assert.True(plugin.GetProperty("installed").GetBoolean());
        Assert.True(plugin.GetProperty("enabled").GetBoolean());
        Assert.True(plugin.GetProperty("removable").GetBoolean());
        Assert.True(File.Exists(Path.Combine(_workspaceCraftPath, "plugins", "browser-use", ".builtin")));
        Assert.Contains(loader.ListSkills(filterUnavailable: false), skill => skill.Name == "browser-use");
    }

    [Fact]
    public async Task PluginSetEnabled_DisablesBrowserUseAndWritesCanonicalId()
    {
        var loader = CreateSkillsLoader(new AppConfig());
        using var harness = CreateHarness(loader: loader);
        await harness.InitializeAsync(configChange: true);
        await InstallBrowserUseAsync(harness);

        var msg = harness.BuildRequest(AppServerMethods.PluginSetEnabled, new { id = "browser-use", enabled = false });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.False(response.RootElement.GetProperty("result").GetProperty("plugin").GetProperty("enabled").GetBoolean());
        var configJson = await File.ReadAllTextAsync(Path.Combine(_workspaceCraftPath, "config.json"));
        Assert.Contains("browser-use", configJson, StringComparison.Ordinal);
        Assert.DoesNotContain("node-repl", configJson, StringComparison.Ordinal);
        Assert.DoesNotContain(loader.ListSkills(filterUnavailable: false), skill => skill.Name == "browser-use");
    }

    [Fact]
    public async Task PluginSetEnabled_WhenNotInstalled_ReturnsError()
    {
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginSetEnabled, new { id = "browser-use", enabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
    }

    [Fact]
    public async Task PluginRemove_RemovesManagedBuiltInDirectory()
    {
        var loader = CreateSkillsLoader(new AppConfig());
        using var harness = CreateHarness(loader: loader);
        await harness.InitializeAsync(configChange: true);
        await InstallBrowserUseAsync(harness);

        var msg = harness.BuildRequest(AppServerMethods.PluginRemove, new { id = "browser-use" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = response.RootElement.GetProperty("result").GetProperty("plugin");
        Assert.False(plugin.GetProperty("installed").GetBoolean());
        Assert.False(plugin.GetProperty("enabled").GetBoolean());
        Assert.False(Directory.Exists(Path.Combine(_workspaceCraftPath, "plugins", "browser-use")));
        Assert.DoesNotContain(loader.ListSkills(filterUnavailable: false), skill => skill.Name == "browser-use");
    }

    [Fact]
    public async Task PluginRemove_UserOwnedDirectoryWithoutMarkerIsRejected()
    {
        var pluginRoot = Path.Combine(_workspaceCraftPath, "plugins", "browser-use");
        new BuiltInPluginDeployer(Path.Combine(_workspaceCraftPath, "plugins")).DeployPlugin("browser-use");
        File.Delete(Path.Combine(pluginRoot, ".builtin"));
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginRemove, new { id = "browser-use" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        Assert.True(Directory.Exists(pluginRoot));
    }

    [Fact]
    public async Task PluginList_TreatsLegacyNodeReplDisabledAsBrowserUseDisabled()
    {
        var config = new AppConfig();
        config.Plugins.DisabledPlugins.Add("node-repl");
        using var harness = CreateHarness(config);
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugin = Assert.Single(response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray());
        Assert.Equal("browser-use", plugin.GetProperty("id").GetString());
        Assert.False(plugin.GetProperty("enabled").GetBoolean());
    }

    private AppServerTestHarness CreateHarness(AppConfig? config = null, SkillsLoader? loader = null)
    {
        config ??= new AppConfig();
        loader ??= CreateSkillsLoader(config);
        return new AppServerTestHarness(
            workspaceCraftPath: _workspaceCraftPath,
            skillsLoader: loader,
            appConfigMonitor: new AppConfigMonitor(config));
    }

    private SkillsLoader CreateSkillsLoader(AppConfig config)
    {
        var loader = new SkillsLoader(_workspaceCraftPath);
        loader.DeployBuiltInSkills();
        loader.SetDisabledSkills(config.Skills.DisabledSkills);
        PluginRuntimeConfigurator.ConfigureSkillsLoader(loader, config, _tempRoot, _workspaceCraftPath);
        return loader;
    }

    private static async Task InstallBrowserUseAsync(AppServerTestHarness harness)
    {
        var install = harness.BuildRequest(AppServerMethods.PluginInstall, new { id = "browser-use" });
        await harness.ExecuteRequestAsync(install);
        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
    }
}
