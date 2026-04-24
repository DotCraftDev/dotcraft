using DotCraft.Abstractions;
using DotCraft.Configuration;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Security;
using DotCraft.Skills;
using DotCraft.Tools;

namespace DotCraft.Core.Tests.Tools;

public sealed class BrowserUseToolProviderTests
{
    private sealed class FakeBrowserUseProxy(bool available) : IBrowserUseProxy
    {
        public bool IsAvailable => available;

        public Task<BrowserUseEvaluateResult?> EvaluateAsync(
            string code,
            int? timeoutSeconds = null,
            CancellationToken ct = default) =>
            Task.FromResult<BrowserUseEvaluateResult?>(new BrowserUseEvaluateResult { ResultText = "ok" });

        public Task<bool> ResetAsync(CancellationToken ct = default) => Task.FromResult(true);
    }

    [Fact]
    public void CreateTools_WithoutAvailableProxy_ReturnsNoTools()
    {
        var provider = new BrowserUseToolProvider();
        var tools = provider.CreateTools(CreateContext(new FakeBrowserUseProxy(false))).ToList();
        Assert.Empty(tools);
    }

    [Fact]
    public void CreateTools_WithAvailableProxy_ReturnsBrowserTools()
    {
        var provider = new BrowserUseToolProvider();
        var tools = provider.CreateTools(CreateContext(new FakeBrowserUseProxy(true))).ToList();
        Assert.Contains(tools, t => t.Name == "BrowserJs");
        Assert.Contains(tools, t => t.Name == "BrowserJsReset");
    }

    private static ToolProviderContext CreateContext(IBrowserUseProxy proxy)
    {
        var root = Path.Combine(Path.GetTempPath(), "dotcraft-browser-use-test-" + Guid.NewGuid().ToString("N"));
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
            BrowserUseProxy = proxy
        };
    }
}
