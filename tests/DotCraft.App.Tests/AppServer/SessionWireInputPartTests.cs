using DotCraft.Protocol;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.AppServer;

public sealed class SessionWireInputPartTests
{
    [Fact]
    public void ToAIContent_ExpandsLeadingAttachedFileMarkers_ForModelVisibleText()
    {
        var part = new SessionWireInputPart
        {
            Type = "text",
            Text = "[[Attached File: C:\\logs\\a.txt]]\n[[Attached File: D:\\docs\\b.md]]\n\nReview these"
        };

        var content = Assert.IsType<TextContent>(part.ToAIContent());

        Assert.Equal("C:\\logs\\a.txt\nD:\\docs\\b.md\n\nReview these", content.Text);
    }

    [Fact]
    public void ToAIContent_LeavesPlainTextUnchanged_WhenNoAttachedFileMarkersExist()
    {
        var part = new SessionWireInputPart
        {
            Type = "text",
            Text = "Keep [[Attached File: C:\\logs\\a.txt]] literal"
        };

        var content = Assert.IsType<TextContent>(part.ToAIContent());

        Assert.Equal("Keep [[Attached File: C:\\logs\\a.txt]] literal", content.Text);
    }
}

