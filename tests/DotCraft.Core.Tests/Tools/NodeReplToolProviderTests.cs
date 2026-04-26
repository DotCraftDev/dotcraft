using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;
using Microsoft.Extensions.AI;

namespace DotCraft.Core.Tests.Tools;

public sealed class NodeReplToolProviderTests
{
    private sealed class FakeNodeReplProxy(bool available, NodeReplEvaluateResult? result = null) : INodeReplProxy
    {
        public bool IsAvailable => available;

        public Task<NodeReplEvaluateResult?> EvaluateAsync(
            string code,
            int? timeoutSeconds = null,
            CancellationToken ct = default) =>
            Task.FromResult<NodeReplEvaluateResult?>(result ?? new NodeReplEvaluateResult { ResultText = "ok" });

        public Task<bool> ResetAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    [Fact]
    public void CreateTools_WithoutAvailableProxy_ReturnsNoTools()
    {
        var provider = new NodeReplToolProvider();
        var tools = provider.CreateTools(CreateContext(new FakeNodeReplProxy(false))).ToList();
        Assert.Empty(tools);
    }

    [Fact]
    public void CreateTools_WithAvailableProxy_ReturnsNodeReplTools()
    {
        var provider = new NodeReplToolProvider();
        var tools = provider.CreateTools(CreateContext(new FakeNodeReplProxy(true))).ToList();
        Assert.Contains(tools, t => t.Name == "NodeReplJs");
        Assert.Contains(tools, t => t.Name == "NodeReplReset");
        Assert.Equal(2, tools.Count);
    }

    [Fact]
    public async Task NodeReplJs_WhenProxyReturnsError_ReturnsToolResultText()
    {
        var provider = new NodeReplToolProvider();
        var proxy = new FakeNodeReplProxy(true, new NodeReplEvaluateResult
        {
            Error = "NodeReplJs timed out after 1000ms."
        });
        var tool = Assert.IsAssignableFrom<AIFunction>(
            provider.CreateTools(CreateContext(proxy)).Single(t => t.Name == "NodeReplJs"));

        var result = await tool.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>
        {
            ["code"] = "await agent.hang()"
        }));

        var contents = Assert.IsAssignableFrom<IReadOnlyList<AIContent>>(result);
        var text = Assert.Single(contents.OfType<TextContent>());
        Assert.Contains("Error: NodeReplJs timed out after 1000ms.", text.Text);
    }

    private static ToolProviderContext CreateContext(INodeReplProxy proxy)
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-node-repl-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new ToolProviderContext
        {
            Config = new AppConfig(),
            ChatClient = null!,
            WorkspacePath = root,
            BotPath = Path.Combine(root, ".craft"),
            MemoryStore = new MemoryStore(Path.Combine(root, ".craft")),
            SkillsLoader = new SkillsLoader(Path.Combine(root, ".craft")),
            ApprovalService = new AutoApproveApprovalService(),
            PathBlacklist = new PathBlacklist([]),
            NodeReplProxy = proxy
        };
    }
}
