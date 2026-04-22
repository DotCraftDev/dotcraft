using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Context.Compaction;

public sealed class MessageTokenEstimatorTests
{
    [Fact]
    public void RoughTokenCount_EmptyReturnsZero()
    {
        Assert.Equal(0, MessageTokenEstimator.RoughTokenCount(null));
        Assert.Equal(0, MessageTokenEstimator.RoughTokenCount(string.Empty));
    }

    [Fact]
    public void RoughTokenCount_ScalesByFourCharsPerToken()
    {
        // 12 chars / 4 = 3 tokens
        Assert.Equal(3, MessageTokenEstimator.RoughTokenCount("abcdefghijkl"));
    }

    [Fact]
    public void EstimateContent_Text()
    {
        var content = new TextContent("hello world"); // 11 chars -> ceil(11/4) = 3
        Assert.Equal(3, MessageTokenEstimator.EstimateContent(content));
    }

    [Fact]
    public void EstimateContent_ImageUsesFixedCost()
    {
        var content = new DataContent(new byte[] { 1, 2, 3 }, "image/png");
        Assert.Equal(2000, MessageTokenEstimator.EstimateContent(content));
    }

    [Fact]
    public void EstimateContent_FunctionCallIncludesNameAndArgs()
    {
        var args = new Dictionary<string, object?> { ["path"] = "README.md" };
        var call = new FunctionCallContent("call-1", "ReadFile", args);
        var tokens = MessageTokenEstimator.EstimateContent(call);
        Assert.True(tokens > 0);
    }

    [Fact]
    public void EstimateContent_FunctionResultUsesSerializedPayload()
    {
        var fr = new FunctionResultContent("call-1", "file contents go here");
        Assert.True(MessageTokenEstimator.EstimateContent(fr) > 0);
    }

    [Fact]
    public void Estimate_AppliesSafetyPad()
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "hello world"), // 3 base tokens
        };
        // 3 * 4/3 = 4
        Assert.Equal(4, MessageTokenEstimator.Estimate(messages));
    }

    [Fact]
    public void Estimate_EmptyReturnsZero()
    {
        Assert.Equal(0, MessageTokenEstimator.Estimate(Array.Empty<ChatMessage>()));
    }
}
