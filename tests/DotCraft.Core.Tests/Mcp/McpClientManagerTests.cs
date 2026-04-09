using DotCraft.Mcp;

namespace DotCraft.Tests.Mcp;

public sealed class McpClientManagerTests
{
    private static McpServerConfig DisabledStdio(string name) =>
        new()
        {
            Name = name,
            Enabled = false,
            Transport = "stdio",
            Command = "npx",
            Arguments = ["-y", "@playwright/mcp@latest"]
        };

    [Fact]
    public async Task ConnectAsync_DisabledServers_LeavesToolIndexesEmpty()
    {
        await using var manager = new McpClientManager();

        await manager.ConnectAsync([DisabledStdio("playwright")]);

        Assert.Empty(manager.Tools);
        Assert.Empty(manager.ToolServerMap);

        var statuses = await manager.ListStatusesAsync();
        var status = Assert.Single(statuses);
        Assert.Equal("disabled", status.StartupState);
        Assert.Equal(0, status.ToolCount);
    }

    [Fact]
    public async Task UpsertAndRemove_DisabledServer_KeepsToolIndexesEmpty()
    {
        await using var manager = new McpClientManager();
        await manager.ConnectAsync([]);

        var upserted = await manager.UpsertAsync(DisabledStdio("browser"));
        Assert.Equal("disabled", upserted.StartupState);
        Assert.Equal(0, upserted.ToolCount);
        Assert.Empty(manager.Tools);
        Assert.Empty(manager.ToolServerMap);

        var removed = await manager.RemoveAsync("browser");
        Assert.True(removed);
        Assert.Empty(manager.Tools);
        Assert.Empty(manager.ToolServerMap);
    }

    [Fact]
    public void Source_DoesNotUseSyncOverAsync_ForToolRebuild()
    {
        var sourcePath = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "src", "DotCraft.Core", "Mcp", "McpClientManager.cs");
        sourcePath = Path.GetFullPath(sourcePath);

        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain(".GetAwaiter().GetResult()", source, StringComparison.Ordinal);

        var rebuildStart = source.IndexOf("private void RebuildToolIndexUnsafe()", StringComparison.Ordinal);
        Assert.True(rebuildStart >= 0, "Could not locate RebuildToolIndexUnsafe.");
        var rebuildBody = source[rebuildStart..];
        Assert.DoesNotContain("ListToolsAsync", rebuildBody, StringComparison.Ordinal);
    }
}
