using System.Text.Json;
using DotCraft.Acp;
using DotCraft.Protocol;

namespace DotCraft.App.Tests.Acp;

public sealed class AcpBridgeHandlerExtractTextTests
{
    [Fact]
    public void ExtractTextFromPayload_JsonElementObject_ReturnsText()
    {
        var el = JsonSerializer.SerializeToElement(new { text = "hello" });
        Assert.Equal("hello", AcpBridgeHandler.ExtractTextFromPayload(el));
    }

    [Fact]
    public void ExtractTextFromPayload_UserMessagePayload_ReturnsText()
    {
        var p = new UserMessagePayload { Text = "typed" };
        Assert.Equal("typed", AcpBridgeHandler.ExtractTextFromPayload(p));
    }

    [Fact]
    public void ExtractTextFromPayload_AgentMessagePayload_ReturnsText()
    {
        var p = new AgentMessagePayload { Text = "agent" };
        Assert.Equal("agent", AcpBridgeHandler.ExtractTextFromPayload(p));
    }

    [Fact]
    public void ExtractTextFromPayload_JsonElementMissingText_ReturnsNull()
    {
        var el = JsonSerializer.SerializeToElement(new { other = 1 });
        Assert.Null(AcpBridgeHandler.ExtractTextFromPayload(el));
    }
}
