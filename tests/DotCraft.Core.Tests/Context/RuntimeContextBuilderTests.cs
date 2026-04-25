using DotCraft.Context;
using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Context;

public sealed class RuntimeContextBuilderTests
{
    [Fact]
    public void AppendRuntimeContext_QQGroup_IncludesSenderAndGroupAliases()
    {
        var contents = new List<AIContent> { new TextContent("hello") };

        contents.AppendRuntimeContext(new TurnInitiatorContext
        {
            ChannelName = "qq",
            UserId = "10001",
            UserName = "Alice",
            UserRole = "admin",
            ChannelContext = "group:123456",
            GroupId = "123456"
        });

        var text = GetAppendedRuntimeContext(contents);
        Assert.Contains("Channel: qq", text);
        Assert.Contains("Channel Context: group:123456", text);
        Assert.Contains("Sender ID: 10001", text);
        Assert.Contains("Sender Name: Alice", text);
        Assert.Contains("Sender Role: admin", text);
        Assert.Contains("Group/Chat ID: 123456", text);
        Assert.Contains("Sender QQ: 10001", text);
        Assert.Contains("QQ Group ID: 123456", text);
    }

    [Fact]
    public void AppendRuntimeContext_QQPrivate_IncludesSenderQQButNotGroupAlias()
    {
        var contents = new List<AIContent> { new TextContent("hello") };

        contents.AppendRuntimeContext(new TurnInitiatorContext
        {
            ChannelName = "qq",
            UserId = "10001",
            UserName = "Alice",
            ChannelContext = "user:10001"
        });

        var text = GetAppendedRuntimeContext(contents);
        Assert.Contains("Channel: qq", text);
        Assert.Contains("Channel Context: user:10001", text);
        Assert.Contains("Sender QQ: 10001", text);
        Assert.DoesNotContain("QQ Group ID:", text);
        Assert.DoesNotContain("Group/Chat ID:", text);
    }

    [Fact]
    public void AppendRuntimeContext_WeCom_IncludesSenderAndChatAliases()
    {
        var contents = new List<AIContent> { new TextContent("hello") };

        contents.AppendRuntimeContext(new TurnInitiatorContext
        {
            ChannelName = "wecom",
            UserId = "zhangsan",
            UserName = "Zhang San",
            UserRole = "whitelisted",
            ChannelContext = "chat:room-1",
            GroupId = "room-1"
        });

        var text = GetAppendedRuntimeContext(contents);
        Assert.Contains("Channel: wecom", text);
        Assert.Contains("Sender Name: Zhang San", text);
        Assert.Contains("WeCom User ID: zhangsan", text);
        Assert.Contains("WeCom Chat ID: room-1", text);
    }

    [Fact]
    public void AppendRuntimeContext_NoInitiator_OnlyAddsGenericRuntimeContext()
    {
        var contents = new List<AIContent> { new TextContent("hello") };

        contents.AppendRuntimeContext();

        var text = GetAppendedRuntimeContext(contents);
        Assert.Contains("[Runtime Context]", text);
        Assert.Contains("Current Time:", text);
        Assert.DoesNotContain("Channel:", text);
        Assert.DoesNotContain("Sender ID:", text);
    }

    private static string GetAppendedRuntimeContext(IReadOnlyList<AIContent> contents)
    {
        var text = Assert.IsType<TextContent>(contents[^1]);
        return text.Text;
    }
}
