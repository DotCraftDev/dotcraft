using System.Diagnostics;
using System.Text.Json.Nodes;
using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Plugins;
using DotCraft.Protocol;
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
    public void ManifestParser_AcceptsSkillOnlyManifest()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        WriteSkillOnlyPlugin(pluginRoot, id: "demo-plugin");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.NotNull(result.Manifest);
        Assert.Empty(result.Manifest!.Functions);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.Combine(pluginRoot, "skills")),
            Path.TrimEndingDirectorySeparator(result.Manifest.SkillsPath!));
    }

    [Fact]
    public void ManifestParser_AcceptsToolsAndProcesses()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        Directory.CreateDirectory(Path.Combine(pluginRoot, "tools"));
        File.WriteAllText(Path.Combine(pluginRoot, "tools", "demo_tool.py"), "print('ok')");
        WriteProcessToolPlugin(pluginRoot, id: "demo-plugin", functionName: "EchoText");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == PluginDiagnosticSeverity.Error);
        Assert.NotNull(result.Manifest);
        var manifest = result.Manifest!;
        var function = Assert.Single(manifest.Functions);
        Assert.Equal("EchoText", function.Name);
        Assert.Equal("process", function.Backend.Kind);
        Assert.Equal("demo", function.Backend.ProcessId);
        Assert.True(manifest.Processes.ContainsKey("demo"));
        Assert.Equal("./tools/demo_tool.py", Assert.Single(manifest.Processes["demo"].Args));
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
    public void ManifestParser_RejectsEscapingProcessPath()
    {
        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, "demo");
        WriteProcessToolPlugin(
            pluginRoot,
            id: "demo-plugin",
            functionName: "EchoText",
            processExtra: """
,
      "workingDirectory": "./../outside"
""");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, d => d.Code == "InvalidPluginProcessWorkingDirectory");
    }

    [Fact]
    public void ManifestParser_RejectsInvalidProcessId()
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
  "tools": [
    {
      "namespace": "demo",
      "name": "EchoText",
      "description": "Echo text.",
      "inputSchema": { "type": "object", "properties": {} },
      "backend": {
        "kind": "process",
        "processId": "bad id",
        "toolName": "EchoText"
      }
    }
  ],
  "processes": {
    "bad id": {
      "command": "python",
      "args": ["./tools/demo_tool.py"]
    }
  }
}
""");

        var result = PluginManifestParser.Load(pluginRoot);

        Assert.Null(result.Manifest);
        Assert.Contains(result.Diagnostics, d => d.Code == "InvalidPluginProcessId");
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
    public void SkillOnlyManifest_DoesNotProducePluginFunctionTools()
    {
        var context = CreateContext(new FakeNodeReplProxy(true));
        WriteSkillOnlyPlugin(Path.Combine(context.BotPath, "plugins", "demo"), id: "demo");
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
            backendKind: "custom-process",
            backendProviderId: "demo",
            backendFunctionName: "DemoFunction");
        var diagnostics = new PluginDiagnosticsStore();
        var provider = new PluginFunctionToolProvider([new NodeReplPluginFunctionProvider()], diagnostics);

        _ = provider.CreateTools(context).ToList();

        Assert.Contains(diagnostics.Snapshot(), d => d.Code == "UnsupportedPluginBackend");
    }

    [Fact]
    public void Binding_ProcessBackendRegistersToolWithoutBuiltinProvider()
    {
        var context = CreateContext(new FakeNodeReplProxy(true));
        WriteProcessToolPlugin(
            Path.Combine(context.BotPath, "plugins", "demo"),
            id: "demo",
            functionName: "EchoText");
        var diagnostics = new PluginDiagnosticsStore();
        var provider = new PluginFunctionToolProvider([], diagnostics, new PluginDynamicToolProcessManager());

        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(provider.CreateTools(context)));

        Assert.Equal("EchoText", tool.Name);
        Assert.DoesNotContain(diagnostics.Snapshot(), d => d.Code == "PluginProcessUnavailable");
    }

    [Fact]
    public void Binding_ProcessBackendMissingProcessProducesDiagnostic()
    {
        var context = CreateContext(new FakeNodeReplProxy(true));
        WriteProcessToolPlugin(
            Path.Combine(context.BotPath, "plugins", "demo"),
            id: "demo",
            functionName: "EchoText",
            includeProcess: false);
        var diagnostics = new PluginDiagnosticsStore();
        var provider = new PluginFunctionToolProvider([], diagnostics, new PluginDynamicToolProcessManager());

        var tools = provider.CreateTools(context).ToList();

        Assert.Empty(tools);
        Assert.Contains(diagnostics.Snapshot(), d => d.Code == "PluginProcessUnavailable");
    }

    [Fact]
    public async Task ProcessInvoker_RoundTripsToolCallOverStdio()
    {
        var python = FindPython();
        if (python == null)
            return;

        var root = NewTempDir();
        var pluginRoot = Path.Combine(root, ".craft", "plugins", "demo");
        Directory.CreateDirectory(Path.Combine(pluginRoot, "tools"));
        File.WriteAllText(
            Path.Combine(pluginRoot, "tools", "demo_tool.py"),
            """
import json
import sys

for line in sys.stdin:
    msg = json.loads(line)
    method = msg.get("method")
    if method == "plugin/initialize":
        print(json.dumps({"jsonrpc": "2.0", "id": msg["id"], "result": {"ready": True}}), flush=True)
    elif method == "plugin/toolCall":
        text = msg.get("params", {}).get("arguments", {}).get("text", "")
        result = {
            "success": text != "fail",
            "contentItems": [{"type": "text", "text": "echo:" + text}],
            "structuredResult": {"length": len(text)},
            "errorCode": None if text != "fail" else "DemoFailed",
            "errorMessage": None if text != "fail" else "Demo failure"
        }
        print(json.dumps({"jsonrpc": "2.0", "id": msg["id"], "result": result}), flush=True)
""");
        WriteProcessToolPlugin(
            pluginRoot,
            id: "demo",
            functionName: "EchoText",
            processExtra: $"""
,
      "startupTimeoutSeconds": 5,
      "toolTimeoutSeconds": 5
""",
            command: python);
        var manifest = PluginManifestParser.Load(pluginRoot).Manifest!;
        await using var manager = new PluginDynamicToolProcessManager();

        var result = await manager.InvokeAsync(
            manifest,
            manifest.Processes["demo"],
            "EchoText",
            new PluginFunctionInvocationContext
            {
                Descriptor = manifest.Functions.Single().ToDescriptor(manifest.Id),
                Execution = CreateExecutionContext(root),
                CallId = "call_1",
                Arguments = new JsonObject { ["text"] = "hello" }
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("echo:hello", Assert.Single(result.ContentItems!).Text);
        Assert.Equal(5, result.StructuredResult?["length"]?.GetValue<int>());

        var failed = await manager.InvokeAsync(
            manifest,
            manifest.Processes["demo"],
            "EchoText",
            new PluginFunctionInvocationContext
            {
                Descriptor = manifest.Functions.Single().ToDescriptor(manifest.Id),
                Execution = CreateExecutionContext(root),
                CallId = "call_2",
                Arguments = new JsonObject { ["text"] = "fail" }
            },
            CancellationToken.None);

        Assert.False(failed.Success);
        Assert.Equal("DemoFailed", failed.ErrorCode);
        Assert.Equal("Demo failure", failed.ErrorMessage);
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

    private static void WriteSkillOnlyPlugin(
        string pluginRoot,
        string id,
        string displayName = "Demo")
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        Directory.CreateDirectory(Path.Combine(pluginRoot, "skills", "demo-skill"));
        File.WriteAllText(
            Path.Combine(pluginRoot, "skills", "demo-skill", "SKILL.md"),
            "---\nname: demo-skill\ndescription: Demo skill\n---\n# Demo");
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{id}}",
  "version": "1.0.0",
  "displayName": "{{displayName}}",
  "description": "Demo plugin.",
  "capabilities": ["skill"],
  "skills": "./skills/"
}
""");
    }

    private static void WriteProcessToolPlugin(
        string pluginRoot,
        string id,
        string functionName,
        bool includeProcess = true,
        string processExtra = "",
        string command = "python")
    {
        Directory.CreateDirectory(Path.Combine(pluginRoot, ".craft-plugin"));
        var processBlock = includeProcess
            ? $$"""
,
  "processes": {
    "demo": {
      "command": "{{command.Replace("\\", "\\\\")}}",
      "args": ["./tools/demo_tool.py"]{{processExtra}}
    }
  }
"""
            : string.Empty;
        File.WriteAllText(
            Path.Combine(pluginRoot, ".craft-plugin", "plugin.json"),
            $$"""
{
  "schemaVersion": 1,
  "id": "{{id}}",
  "version": "1.0.0",
  "displayName": "Demo",
  "description": "Demo plugin.",
  "capabilities": ["tool"],
  "tools": [
    {
      "namespace": "demo",
      "name": "{{functionName}}",
      "description": "Echo text through an external process.",
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
        "toolName": "{{functionName}}"
      }
    }
  ]{{processBlock}}
}
""");
    }

    private static PluginFunctionExecutionContext CreateExecutionContext(string workspacePath)
        => new()
        {
            ThreadId = "thread_1",
            TurnId = "turn_001",
            OriginChannel = string.Empty,
            WorkspacePath = workspacePath,
            RequireApprovalOutsideWorkspace = false,
            ApprovalService = new AutoApproveApprovalService(),
            PathBlacklist = new PathBlacklist([]),
            Turn = new SessionTurn { Id = "turn_001", ThreadId = "thread_1" },
            NextItemSequence = () => 1,
            EmitItemStarted = _ => { },
            EmitItemCompleted = _ => { }
        };

    private static string? FindPython()
    {
        foreach (var candidate in new[] { "python", "python3" })
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    ArgumentList = { "--version" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (process == null)
                    continue;
                if (process.WaitForExit(3000) && process.ExitCode == 0)
                    return candidate;
            }
            catch
            {
                // Try the next common Python command name.
            }
        }

        return null;
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
