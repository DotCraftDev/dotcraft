using DotCraft.Configuration;
using DotCraft.Plugins;

namespace DotCraft.Core.Tests.Plugins;

public sealed class PluginDiscoveryTests
{
    [Fact]
    public void ManifestParser_AcceptsInterfaceAndSkillsPath()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "demo-skill"));
        File.WriteAllText(Path.Combine(pluginRoot, "skills", "demo-skill", "SKILL.md"), "---\nname: demo-skill\n---\n# Demo");
        Directory.CreateDirectory(Path.Combine(pluginRoot, "assets"));
        File.WriteAllText(Path.Combine(pluginRoot, "assets", "icon.svg"), "<svg />");
        WriteSkillOnlyPlugin(
            pluginRoot,
            id: "demo-plugin",
            extra: """
,
  "interface": {
    "displayName": "Demo Plugin",
    "shortDescription": "Demo short.",
    "longDescription": "Demo long.",
    "developerName": "DotCraft",
    "category": "Coding",
    "capabilities": ["Read"],
    "defaultPrompt": "Try demo",
    "brandColor": "#123456",
    "composerIcon": "./assets/icon.svg",
    "logo": "./assets/icon.svg"
  }
""");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.NotNull(result.Manifest);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.Combine(pluginRoot, "skills")),
            Path.TrimEndingDirectorySeparator(result.Manifest!.SkillsPath!));
        Assert.Equal("Demo Plugin", result.Manifest.Interface?.DisplayName);
        Assert.Equal(Path.Combine(pluginRoot, "assets", "icon.svg"), result.Manifest.Interface?.ComposerIcon);
    }

    [Fact]
    public void ManifestParser_AcceptsSkillOnlyManifest()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        WriteSkillOnlyPlugin(pluginRoot, id: "demo-plugin");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.NotNull(result.Manifest);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.Combine(pluginRoot, "skills")),
            Path.TrimEndingDirectorySeparator(result.Manifest!.SkillsPath!));
    }

    [Fact]
    public void ManifestParser_AcceptsMcpOnlyManifest()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        WriteMcpOnlyPlugin(pluginRoot, id: "demo-plugin");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.NotNull(result.Manifest);
        Assert.Equal(Path.Combine(pluginRoot, ".mcp.json"), result.Manifest!.McpServersPath);
    }

    [Fact]
    public void ManifestParser_AcceptsInterfaceOnlyManifest()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        WriteInterfaceOnlyPlugin(pluginRoot, id: "demo-plugin");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.NotNull(result.Manifest);
        Assert.Equal("Demo Plugin", result.Manifest!.Interface?.DisplayName);
    }

    [Fact]
    public void ManifestParser_IgnoresLegacyNativeToolFieldsWhenSupportedCapabilityExists()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        WriteSkillOnlyPlugin(
            pluginRoot,
            id: "demo-plugin",
            extra: """
,
  "tools": [
    {
      "namespace": "demo",
      "name": "EchoText",
      "description": "Legacy tool.",
      "inputSchema": { "type": "object" },
      "backend": { "kind": "process", "processId": "demo" }
    }
  ],
  "processes": {
    "demo": {
      "command": "python",
      "args": ["./tools/demo_tool.py"]
    }
  }
""");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.NotNull(result.Manifest);
        Assert.Contains(result.Diagnostics, d => d.Code == "UnsupportedPluginNativeTools");
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
    }

    [Fact]
    public void ManifestParser_RejectsLegacyNativeToolOnlyManifest()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
{
  "schemaVersion": 1,
  "id": "demo-plugin",
  "version": "1.0.0",
  "displayName": "Demo",
  "description": "Demo plugin.",
  "capabilities": ["tool"],
  "functions": [
    {
      "namespace": "demo",
      "name": "EchoText",
      "description": "Legacy function.",
      "inputSchema": { "type": "object" },
      "backend": { "kind": "builtin", "providerId": "demo", "functionName": "EchoText" }
    }
  ]
}
""");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, d => d.Code == "UnsupportedPluginNativeTools");
        Assert.Contains(result.Diagnostics, d => d.Code == "MissingPluginCapabilities");
    }

    [Fact]
    public void PluginMcpServerLoader_LoadsEnabledPluginServersWithPluginRootCwd()
    {
        var root = NewTempDir();
        var workspace = Path.Combine(root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        var pluginRoot = Path.Combine(botPath, "plugins", "demo");
        WriteMcpOnlyPlugin(pluginRoot, id: "demo-plugin");
        var config = new AppConfig();

        var servers = PluginMcpServerLoader.LoadEnabledPluginServers(
            config,
            workspace,
            botPath,
            out var diagnostics);

        Assert.DoesNotContain(diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        var server = Assert.Single(servers);
        Assert.Equal("demo-plugin:review", server.Name);
        Assert.Equal("stdio", server.Transport);
        Assert.Equal("node", server.Command);
        Assert.Equal(Path.Combine(pluginRoot, "server"), server.Cwd);
    }

    [Fact]
    public void ManifestParser_RejectsManifestWithoutSupportedCapabilities()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            """
{
  "schemaVersion": 1,
  "id": "demo-plugin",
  "version": "1.0.0",
  "displayName": "Demo",
  "description": "Demo plugin.",
  "capabilities": ["test"]
}
""");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, d => d.Code == "MissingPluginCapabilities");
    }

    [Fact]
    public void ManifestParser_RejectsEscapingSkillsPath()
    {
        var root = NewTempDir();
        WriteSkillOnlyPlugin(
            Path.Combine(root, "demo"),
            id: "demo-plugin",
            extra: """
,
  "skills": "../skills"
""",
            includeSkillsField: false);

        var result = PluginManifestParser.Load(Path.Combine(root, "demo"));

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, d => d.Code == "InvalidPluginManifestPath");
    }

    [Fact]
    public void ManifestParser_RejectsPathEscape()
    {
        var root = NewTempDir();
        WriteInterfaceOnlyPlugin(
            Path.Combine(root, "demo"),
            id: "demo-plugin",
            extra: """
,
  "paths": {
    "asset": "./../secret.txt"
  }
""");

        var result = PluginManifestParser.Load(Path.Combine(root, "demo"));

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, d => d.Code == "InvalidPluginManifestPath");
    }

    [Fact]
    public void ManifestParser_RejectsAnyParentPathSegment()
    {
        var root = NewTempDir();
        WriteInterfaceOnlyPlugin(
            Path.Combine(root, "demo"),
            id: "demo-plugin",
            extra: """
,
  "paths": {
    "asset": "./assets/../asset.txt"
  }
""");

        var result = PluginManifestParser.Load(Path.Combine(root, "demo"));

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, d => d.Code == "InvalidPluginManifestPath");
    }

    [Fact]
    public void BuiltInPluginDeployer_DeploysBrowserUseManifest()
    {
        var root = NewTempDir();
        var deployer = new BuiltInPluginDeployer(root);

        var diagnostics = deployer.Deploy();

        Assert.DoesNotContain(diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.True(File.Exists(Path.Combine(root, "browser-use", ".craft-plugin", "plugin.json")));
        Assert.True(File.Exists(Path.Combine(root, "browser-use", ".builtin")));
        Assert.True(File.Exists(Path.Combine(root, "browser-use", "skills", "browser-use", "SKILL.md")));
    }

    [Fact]
    public void BuiltInPluginDeployer_DoesNotOverwriteUserOwnedPlugin()
    {
        var root = NewTempDir();
        var userPlugin = Path.Combine(root, "browser-use");
        Directory.CreateDirectory(userPlugin);
        File.WriteAllText(Path.Combine(userPlugin, "owned.txt"), "mine");

        var diagnostics = new BuiltInPluginDeployer(root).Deploy();

        Assert.True(File.Exists(Path.Combine(userPlugin, "owned.txt")));
        Assert.False(File.Exists(Path.Combine(userPlugin, ".builtin")));
        Assert.Contains(diagnostics, d => d.Code == "BuiltInPluginUserOwned");
    }

    [Fact]
    public void Discovery_UsesWorkspaceThenExplicitThenGlobalPrecedence()
    {
        var root = NewTempDir();
        var workspace = Path.Combine(root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        var explicitRoot = Path.Combine(root, "explicit");
        var globalRoot = Path.Combine(root, "global");
        WriteInterfaceOnlyPlugin(Path.Combine(globalRoot, "demo"), id: "demo", displayName: "Global");
        WriteInterfaceOnlyPlugin(Path.Combine(explicitRoot, "demo"), id: "demo", displayName: "Explicit");
        WriteInterfaceOnlyPlugin(Path.Combine(botPath, "plugins", "demo"), id: "demo", displayName: "Workspace");
        var config = new AppConfig();
        config.Plugins.PluginRoots.Add(explicitRoot);

        var result = new PluginDiscoveryService(globalRoot).Discover(config, workspace, botPath);

        var plugin = Assert.Single(result.Plugins);
        Assert.Equal("Workspace", plugin.Manifest.DisplayName);
        Assert.Contains(result.Diagnostics, d => d.Code == "DuplicatePluginId");
    }

    [Fact]
    public void Discovery_LocalPluginsAreEnabledByDefaultAndDisabledPluginsOverride()
    {
        var root = NewTempDir();
        var workspace = Path.Combine(root, "workspace");
        var botPath = Path.Combine(workspace, ".craft");
        WriteInterfaceOnlyPlugin(Path.Combine(botPath, "plugins", "demo"), id: "demo");
        var config = new AppConfig();

        var enabled = new PluginDiscoveryService(Path.Combine(root, "global")).Discover(config, workspace, botPath);
        config.Plugins.DisabledPlugins.Add("demo");
        var disabled = new PluginDiscoveryService(Path.Combine(root, "global")).Discover(config, workspace, botPath);

        Assert.Single(enabled.Plugins);
        Assert.Empty(disabled.Plugins);
        Assert.Contains(disabled.Diagnostics, d => d.Code == "PluginDisabled");
    }

    [Fact]
    public void ConflictResolver_RejectsDuplicateFunctionNames()
    {
        var diagnostics = new List<PluginDiagnostic>();
        var invoker = new NoopPluginInvoker();
        var registrations = new[]
        {
            new PluginFunctionRegistration(Descriptor("plugin-a", "SameName"), invoker),
            new PluginFunctionRegistration(Descriptor("plugin-b", "SameName"), invoker)
        };

        var resolved = PluginFunctionConflictResolver.ResolveRegistrations(registrations, diagnostics);

        Assert.Single(resolved);
        Assert.Contains(diagnostics, d => d.Code == "DuplicatePluginFunctionName");
    }

    private static PluginFunctionDescriptor Descriptor(string pluginId, string name) =>
        new()
        {
            PluginId = pluginId,
            Name = name,
            Description = name,
            InputSchema = new System.Text.Json.Nodes.JsonObject { ["type"] = "object" }
        };

    private static string NewTempDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-plugin-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WriteSkillOnlyPlugin(
        string pluginRoot,
        string id,
        string displayName = "Demo",
        string extra = "",
        bool includeSkillsField = true)
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "demo-skill"));
        File.WriteAllText(
            Path.Combine(pluginRoot, "skills", "demo-skill", "SKILL.md"),
            "---\nname: demo-skill\ndescription: Demo skill\n---\n# Demo");
        var skills = includeSkillsField ? """
,
  "skills": "./skills/"
""" : string.Empty;
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{id}}",
  "version": "1.0.0",
  "displayName": "{{displayName}}",
  "description": "Demo plugin.",
  "capabilities": ["skill"]{{skills}}{{extra}}
}
""");
    }

    private static void WriteMcpOnlyPlugin(
        string pluginRoot,
        string id,
        string displayName = "Demo")
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{id}}",
  "version": "1.0.0",
  "displayName": "{{displayName}}",
  "description": "Demo plugin.",
  "capabilities": ["mcp"]
}
""");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".mcp.json"),
            """
{
  "mcpServers": {
    "review": {
      "transport": "stdio",
      "command": "node",
      "args": ["server.js"],
      "cwd": "./server"
    }
  }
}
""");
    }

    private static void WriteInterfaceOnlyPlugin(
        string pluginRoot,
        string id,
        string displayName = "Demo",
        string extra = "")
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{id}}",
  "version": "1.0.0",
  "displayName": "{{displayName}}",
  "description": "Demo plugin.",
  "capabilities": ["metadata"],
  "interface": {
    "displayName": "Demo Plugin",
    "shortDescription": "Demo short.",
    "developerName": "DotCraft",
    "category": "Coding",
    "capabilities": ["Metadata"],
    "defaultPrompt": "Try demo",
    "brandColor": "#2563EB"
  }{{extra}}
}
""");
    }

    private sealed class NoopPluginInvoker : IPluginFunctionInvoker
    {
        public ValueTask<PluginFunctionInvocationResult> InvokeAsync(
            PluginFunctionInvocationContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PluginFunctionInvocationResult());
    }
}
