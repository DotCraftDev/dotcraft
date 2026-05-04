using System.Text.Json;
using DotCraft.CLI;

namespace DotCraft.Tests.CLI;

public sealed class OneShotNotificationTests
{
    [Fact]
    public void From_AgentMessageDelta_ReturnsDeltaText()
    {
        using var doc = JsonDocument.Parse("""
            {"jsonrpc":"2.0","method":"item/agentMessage/delta","params":{"threadId":"thread_1","delta":"hello"}}
            """);

        var notification = OneShotNotification.From(doc);

        Assert.Equal(OneShotNotificationKind.AgentDelta, notification.Kind);
        Assert.Equal("hello", notification.Text);
    }

    [Fact]
    public void From_TurnFailed_ReturnsFailureText()
    {
        using var doc = JsonDocument.Parse("""
            {"jsonrpc":"2.0","method":"turn/failed","params":{"threadId":"thread_1","error":"boom"}}
            """);

        var notification = OneShotNotification.From(doc);

        Assert.Equal(OneShotNotificationKind.Failed, notification.Kind);
        Assert.Equal("boom", notification.Text);
    }

    [Fact]
    public void From_AgentMessageCompleted_ReturnsFinalText()
    {
        using var doc = JsonDocument.Parse("""
            {"jsonrpc":"2.0","method":"item/completed","params":{"threadId":"thread_1","item":{"type":"agentMessage","text":"final answer"}}}
            """);

        var notification = OneShotNotification.From(doc);

        Assert.Equal(OneShotNotificationKind.AgentCompleted, notification.Kind);
        Assert.Equal("final answer", notification.Text);
    }

    [Fact]
    public void From_ToolStart_ReturnsProgressText()
    {
        using var doc = JsonDocument.Parse("""
            {"jsonrpc":"2.0","method":"item/started","params":{"threadId":"thread_1","item":{"type":"toolCall","payload":{"name":"Shell"}}}}
            """);

        var notification = OneShotNotification.From(doc);

        Assert.Equal(OneShotNotificationKind.Progress, notification.Kind);
        Assert.Contains("Shell", notification.Text, StringComparison.Ordinal);
    }
}
