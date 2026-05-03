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
    public async Task PluginList_ReturnsSkillOnlyPluginWithEmptyFunctions()
    {
        WriteSkillOnlyPlugin(Path.Combine(_workspaceCraftPath, "plugins", "demo-plugin"));
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugins = response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray().ToArray();
        var plugin = Assert.Single(plugins, item => item.GetProperty("id").GetString() == "demo-plugin");
        Assert.True(plugin.GetProperty("enabled").GetBoolean());
        Assert.True(plugin.GetProperty("installed").GetBoolean());
        Assert.Empty(plugin.GetProperty("functions").EnumerateArray());
        Assert.Contains(
            plugin.GetProperty("skills").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "demo-skill");
    }

    [Fact]
    public async Task PluginList_ReturnsWorkspaceProcessToolPlugin()
    {
        WriteProcessToolPlugin(Path.Combine(_workspaceCraftPath, "plugins", "external-process-echo"));
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var plugins = response.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray().ToArray();
        var plugin = Assert.Single(plugins, item => item.GetProperty("id").GetString() == "external-process-echo");
        Assert.Equal("External Process Echo", plugin.GetProperty("displayName").GetString());
        Assert.True(plugin.GetProperty("enabled").GetBoolean());
        Assert.True(plugin.GetProperty("installed").GetBoolean());
        Assert.False(plugin.GetProperty("installable").GetBoolean());
        Assert.True(plugin.GetProperty("removable").GetBoolean());
        Assert.Equal("workspace", plugin.GetProperty("source").GetString());
        Assert.Contains(
            plugin.GetProperty("functions").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "EchoText"
                    && item.GetProperty("namespace").GetString() == "demo");
        Assert.Contains(
            plugin.GetProperty("skills").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "external-process-echo"
                    && item.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task PluginList_ReturnsDiagnosticsForInvalidLocalManifest()
    {
        WriteInvalidPlugin(Path.Combine(_workspaceCraftPath, "plugins", "broken-plugin"));
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        var result = response.RootElement.GetProperty("result");
        Assert.DoesNotContain(
            result.GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "broken-plugin");
        Assert.Contains(
            result.GetProperty("diagnostics").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "MissingPluginCapabilities"
                    && item.GetProperty("pluginId").GetString() == "broken-plugin");
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
    public async Task PluginRemove_RemovesWorkspaceLocalUserPluginDirectory()
    {
        var pluginRoot = Path.Combine(_workspaceCraftPath, "plugins", "external-process-echo");
        WriteProcessToolPlugin(pluginRoot);
        using var harness = CreateHarness();
        await harness.InitializeAsync();

        var msg = harness.BuildRequest(AppServerMethods.PluginRemove, new { id = "external-process-echo" });
        await harness.ExecuteRequestAsync(msg);

        using var response = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.False(response.RootElement.GetProperty("result").TryGetProperty("plugin", out _));
        Assert.False(Directory.Exists(pluginRoot));

        var list = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(list);

        using var listResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        Assert.DoesNotContain(
            listResponse.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "external-process-echo");
    }

    [Fact]
    public async Task PluginRemove_ExplicitPluginRootIsRejected()
    {
        var pluginRoot = Path.Combine(_tempRoot, "external-plugins", "external-process-echo");
        WriteProcessToolPlugin(pluginRoot);
        var config = new AppConfig();
        config.Plugins.PluginRoots.Add(Path.Combine(_tempRoot, "external-plugins"));
        using var harness = CreateHarness(config);
        await harness.InitializeAsync();

        var list = harness.BuildRequest(AppServerMethods.PluginList, new { includeDisabled = true });
        await harness.ExecuteRequestAsync(list);

        using var listResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(listResponse);
        var plugin = Assert.Single(
            listResponse.RootElement.GetProperty("result").GetProperty("plugins").EnumerateArray(),
            item => item.GetProperty("id").GetString() == "external-process-echo");
        Assert.Equal("explicit", plugin.GetProperty("source").GetString());
        Assert.False(plugin.GetProperty("removable").GetBoolean());

        var remove = harness.BuildRequest(AppServerMethods.PluginRemove, new { id = "external-process-echo" });
        await harness.ExecuteRequestAsync(remove);

        using var removeResponse = await harness.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(removeResponse, AppServerErrors.InvalidParamsCode);
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

    private static void WriteSkillOnlyPlugin(string pluginRoot)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "demo-skill"));
        File.WriteAllText(
            Path.Combine(pluginRoot, "skills", "demo-skill", "SKILL.md"),
            "---\nname: demo-skill\ndescription: Demo skill\n---\n# Demo");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
{
  "schemaVersion": 1,
  "id": "demo-plugin",
  "version": "1.0.0",
  "displayName": "Demo Plugin",
  "description": "Demo skill-only plugin.",
  "capabilities": ["skill"],
  "skills": "./skills/"
}
""");
    }

    private static void WriteProcessToolPlugin(string pluginRoot)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "external-process-echo"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "tools"));
        File.WriteAllText(
            Path.Combine(pluginRoot, "skills", "external-process-echo", "SKILL.md"),
            "---\nname: external-process-echo\ndescription: Echo plugin skill\n---\n# Echo");
        File.WriteAllText(Path.Combine(pluginRoot, "tools", "demo_tool.py"), "print('ready')");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
{
  "schemaVersion": 1,
  "id": "external-process-echo",
  "version": "0.1.0",
  "displayName": "External Process Echo",
  "description": "Echo text through a plugin-owned local process.",
  "capabilities": ["skill", "tool"],
  "skills": "./skills/",
  "tools": [
    {
      "namespace": "demo",
      "name": "EchoText",
      "description": "Echo text through an external plugin process.",
      "inputSchema": {
        "type": "object",
        "properties": {
          "text": { "type": "string" }
        },
        "required": ["text"]
      },
      "backend": {
        "kind": "process",
        "processId": "demo",
        "toolName": "EchoText"
      }
    }
  ],
  "processes": {
    "demo": {
      "command": "python",
      "args": ["./tools/demo_tool.py"],
      "workingDirectory": "./",
      "toolTimeoutSeconds": 20
    }
  },
  "interface": {
    "displayName": "External Process Echo",
    "shortDescription": "Run an echo tool in a plugin process",
    "developerName": "DotHarness",
    "category": "Coding",
    "capabilities": ["Skill", "Tool"],
    "defaultPrompt": "Echo text through the external process plugin.",
    "brandColor": "#2563EB"
  }
}
""");
    }

    private static void WriteInvalidPlugin(string pluginRoot)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
{
  "schemaVersion": 1,
  "id": "broken-plugin",
  "version": "0.1.0",
  "displayName": "Broken Plugin",
  "description": "This manifest lacks supported contributions.",
  "capabilities": ["tool"]
}
""");
    }
}
