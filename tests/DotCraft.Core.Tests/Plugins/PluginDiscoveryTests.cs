using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Plugins;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Core.Tests.Plugins;

public sealed class PluginDiscoveryTests
{
    [Fact]
    public void ManifestParser_AcceptsValidManifest()
    {
        var root = NewTempDir();
        WritePlugin(
            Path.Combine(root, "demo"),
            id: "demo-plugin",
            functionName: "DemoFunction");

        var result = PluginManifestParser.Load(Path.Combine(root, "demo"));

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        var manifest = result.Manifest;
        Assert.NotNull(manifest);
        Assert.Equal("demo-plugin", manifest.Id);
        Assert.Equal("DemoFunction", Assert.Single(manifest.Functions).Name);
    }

    [Fact]
    public void ManifestParser_AcceptsInterfaceAndSkillsPath()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "demo-skill"));
        File.WriteAllText(Path.Combine(pluginRoot, "skills", "demo-skill", "SKILL.md"), "---\nname: demo-skill\n---\n# Demo");
        Directory.CreateDirectory(Path.Combine(pluginRoot, "assets"));
        File.WriteAllText(Path.Combine(pluginRoot, "assets", "icon.svg"), "<svg />");
        WritePlugin(
            pluginRoot,
            id: "demo-plugin",
            functionName: "DemoFunction",
            extra: """
,
  "skills": "./skills/",
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

        Assert.NotNull(result.Manifest);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.Combine(pluginRoot, "skills")),
            Path.TrimEndingDirectorySeparator(result.Manifest!.SkillsPath!));
        Assert.Equal("Demo Plugin", result.Manifest.Interface?.DisplayName);
        Assert.Equal(Path.Combine(pluginRoot, "assets", "icon.svg"), result.Manifest.Interface?.ComposerIcon);
    }

    [Fact]
    public void ManifestParser_RejectsEscapingSkillsPath()
    {
        var root = NewTempDir();
        WritePlugin(
            Path.Combine(root, "demo"),
            id: "demo-plugin",
            functionName: "DemoFunction",
            extra: """
,
  "skills": "../skills"
""");

        var result = PluginManifestParser.Load(Path.Combine(root, "demo"));

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, d => d.Code == "InvalidPluginManifestPath");
    }

    [Fact]
    public void ManifestParser_RejectsPathEscape()
    {
        var root = NewTempDir();
        WritePlugin(
            Path.Combine(root, "demo"),
            id: "demo-plugin",
            functionName: "DemoFunction",
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
        WritePlugin(
            Path.Combine(root, "demo"),
            id: "demo-plugin",
            functionName: "DemoFunction",
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
        WritePlugin(Path.Combine(globalRoot, "demo"), id: "demo", displayName: "Global", functionName: "DemoGlobal");
        WritePlugin(Path.Combine(explicitRoot, "demo"), id: "demo", displayName: "Explicit", functionName: "DemoExplicit");
        WritePlugin(Path.Combine(botPath, "plugins", "demo"), id: "demo", displayName: "Workspace", functionName: "DemoWorkspace");
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
        WritePlugin(Path.Combine(botPath, "plugins", "demo"), id: "demo", functionName: "DemoFunction");
        var config = new AppConfig();

        var enabled = new PluginDiscoveryService(Path.Combine(root, "global")).Discover(config, workspace, botPath);
        config.Plugins.DisabledPlugins.Add("demo");
        var disabled = new PluginDiscoveryService(Path.Combine(root, "global")).Discover(config, workspace, botPath);

        Assert.Single(enabled.Plugins);
        Assert.Empty(disabled.Plugins);
        Assert.Contains(disabled.Diagnostics, d => d.Code == "PluginDisabled");
    }

    [Fact]
    public void NodeReplManifest_BindsDescriptorToCSharpInvoker()
    {
        var context = CreateContext(new FakeNodeReplProxy(true));
        WritePlugin(
            Path.Combine(context.BotPath, "plugins", "browser-use"),
            id: "browser-use",
            displayName: "Node REPL",
            functionName: "ManifestNodeReplJs",
            backendFunctionName: "NodeReplJs",
            description: "Manifest provided description.");
        var diagnostics = new PluginDiagnosticsStore();
        var provider = new PluginFunctionToolProvider([new NodeReplPluginFunctionProvider()], diagnostics);

        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(provider.CreateTools(context)));

        Assert.Equal("ManifestNodeReplJs", tool.Name);
        Assert.Equal("Manifest provided description.", tool.Description);
        Assert.DoesNotContain(diagnostics.Snapshot(), d => d.Code == "BuiltinBackendUnavailable");
    }

    [Fact]
    public void NodeReplManifest_WhenBrowserUseNotInstalled_ReturnsNoTools()
    {
        var context = CreateContext(new FakeNodeReplProxy(true));
        var provider = new PluginFunctionToolProvider([new NodeReplPluginFunctionProvider()], new PluginDiagnosticsStore());

        var tools = provider.CreateTools(context).ToList();

        Assert.Empty(tools);
    }

    [Fact]
    public void NodeReplManifest_WhenProxyUnavailable_ReturnsNoTools()
    {
        var context = CreateContext(new FakeNodeReplProxy(false));
        WritePlugin(Path.Combine(context.BotPath, "plugins", "browser-use"), id: "browser-use", functionName: "NodeReplJs");
        var provider = new PluginFunctionToolProvider([new NodeReplPluginFunctionProvider()], new PluginDiagnosticsStore());

        var tools = provider.CreateTools(context).ToList();

        Assert.Empty(tools);
    }

    [Fact]
    public void Binding_UnsupportedBackendProducesDiagnostic()
    {
        var context = CreateContext(new FakeNodeReplProxy(true));
        WritePlugin(
            Path.Combine(context.BotPath, "plugins", "demo"),
            id: "demo",
            functionName: "DemoFunction",
            backendKind: "process",
            backendProviderId: "demo",
            backendFunctionName: "DemoFunction");
        var diagnostics = new PluginDiagnosticsStore();
        var provider = new PluginFunctionToolProvider([new NodeReplPluginFunctionProvider()], diagnostics);

        _ = provider.CreateTools(context).ToList();

        Assert.Contains(diagnostics.Snapshot(), d => d.Code == "UnsupportedPluginBackend");
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

    private static void WritePlugin(
        string pluginRoot,
        string id,
        string functionName,
        string displayName = "Demo",
        string description = "Demo function.",
        string backendKind = "builtin",
        string? backendProviderId = null,
        string? backendFunctionName = null,
        string extra = "")
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        backendProviderId ??= id;
        backendFunctionName ??= functionName;
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{id}}",
  "version": "1.0.0",
  "displayName": "{{displayName}}",
  "description": "Demo plugin.",
  "capabilities": ["test"]{{extra}},
  "functions": [
    {
      "namespace": "test",
      "name": "{{functionName}}",
      "description": "{{description}}",
      "inputSchema": { "type": "object", "properties": {} },
      "backend": {
        "kind": "{{backendKind}}",
        "providerId": "{{backendProviderId}}",
        "functionName": "{{backendFunctionName}}"
      }
    }
  ]
}
""");
    }

    private static ToolProviderContext CreateContext(INodeReplProxy proxy)
    {
        var root = NewTempDir();
        var botPath = Path.Combine(root, ".craft");
        Directory.CreateDirectory(botPath);
        return new ToolProviderContext
        {
            Config = new AppConfig(),
            ChatClient = null!,
            WorkspacePath = root,
            BotPath = botPath,
            MemoryStore = new MemoryStore(botPath),
            SkillsLoader = new SkillsLoader(botPath),
            ApprovalService = new AutoApproveApprovalService(),
            PathBlacklist = new PathBlacklist([]),
            NodeReplProxy = proxy
        };
    }

    private sealed class FakeNodeReplProxy(bool available) : INodeReplProxy
    {
        public bool IsAvailable => available;

        public Task<NodeReplEvaluateResult?> EvaluateAsync(
            string code,
            int? timeoutSeconds = null,
            CancellationToken ct = default) =>
            Task.FromResult<NodeReplEvaluateResult?>(new NodeReplEvaluateResult { ResultText = "ok" });
    }

    private sealed class NoopPluginInvoker : IPluginFunctionInvoker
    {
        public ValueTask<PluginFunctionInvocationResult> InvokeAsync(
            PluginFunctionInvocationContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PluginFunctionInvocationResult());
    }
}
