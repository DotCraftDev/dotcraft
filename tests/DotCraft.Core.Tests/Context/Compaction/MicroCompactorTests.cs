using DotCraft.Context.Compaction;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Context.Compaction;

public sealed class MicroCompactorTests
{
    private static CompactionConfig Config(
        int triggerCount = 5,
        int keepRecent = 2,
        int gapMinutes = 0,
        bool enabled = true) => new()
    {
        MicrocompactEnabled = enabled,
        MicrocompactTriggerCount = triggerCount,
        MicrocompactKeepRecent = keepRecent,
        MicrocompactGapMinutes = gapMinutes,
    };

    private static ChatMessage AssistantCall(string callId, string toolName = "ReadFile")
    {
        return new ChatMessage(ChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent(callId, toolName, new Dictionary<string, object?>()),
        });
    }

    private static ChatMessage ToolResult(string callId, string payload = "payload")
    {
        return new ChatMessage(ChatRole.User, new List<AIContent>
        {
            new FunctionResultContent(callId, payload),
        });
    }

    [Fact]
    public void Run_NoTrigger_ReturnsUnchanged()
    {
        var micro = new MicroCompactor(Config());
        var messages = new List<ChatMessage> { AssistantCall("c1"), ToolResult("c1") };
        var result = micro.Run(messages);

        Assert.Equal(MicroCompactTrigger.None, result.Trigger);
        Assert.Equal(0, result.ClearedCount);
        Assert.Same(messages, result.Messages);
    }

    [Fact]
    public void Run_CountTrigger_ClearsOlderResultsKeepsRecent()
    {
        var cfg = Config(triggerCount: 3, keepRecent: 2);
        var micro = new MicroCompactor(cfg);

        var messages = new List<ChatMessage>();
        for (var i = 0; i < 4; i++)
        {
            var id = $"c{i}";
            messages.Add(AssistantCall(id));
            messages.Add(ToolResult(id, payload: $"body-{i}"));
        }

        var result = micro.Run(messages);
        Assert.Equal(MicroCompactTrigger.CountBased, result.Trigger);
        // 4 compactable tool results, keep 2 → clear 2.
        Assert.Equal(2, result.ClearedCount);

        // First two tool results should now be the cleared marker.
        var firstResult = (FunctionResultContent)result.Messages[1].Contents[0];
        Assert.Equal(CompactableToolNames.ClearedResultMarker, firstResult.Result as string);

        // Last tool result should be intact.
        var lastResult = (FunctionResultContent)result.Messages[7].Contents[0];
        Assert.Equal("body-3", lastResult.Result as string);
    }

    [Fact]
    public void Run_NonCompactableToolNotCleared()
    {
        var cfg = Config(triggerCount: 2, keepRecent: 1);
        var micro = new MicroCompactor(cfg);

        var messages = new List<ChatMessage>
        {
            AssistantCall("c1", "ReadFile"),
            ToolResult("c1"),
            AssistantCall("c2", "ReadFile"),
            ToolResult("c2"),
            // Non-compactable tool
            AssistantCall("c3", "SomeUnknownTool"),
            ToolResult("c3", "keep-me"),
        };

        var result = micro.Run(messages);
        Assert.Equal(MicroCompactTrigger.CountBased, result.Trigger);

        var unknownResult = (FunctionResultContent)result.Messages[5].Contents[0];
        Assert.Equal("keep-me", unknownResult.Result as string);
    }

    [Fact]
    public void Run_McpToolTreatedAsCompactable()
    {
        Assert.True(CompactableToolNames.IsCompactable("mcp__svr__tool"));
    }

    [Fact]
    public void Run_TimeTrigger_FiresWhenIdleAndOverKeepRecent()
    {
        var cfg = Config(triggerCount: 1000, keepRecent: 2, gapMinutes: 5);
        var micro = new MicroCompactor(cfg);

        var messages = new List<ChatMessage>();
        for (var i = 0; i < 4; i++)
        {
            var id = $"c{i}";
            messages.Add(AssistantCall(id));
            messages.Add(ToolResult(id));
        }

        // lastAssistantTimestampUtc 10 minutes ago → gap > 5 → time trigger.
        var result = micro.Run(messages, DateTimeOffset.UtcNow.AddMinutes(-10));
        Assert.Equal(MicroCompactTrigger.TimeBased, result.Trigger);
        Assert.Equal(2, result.ClearedCount);
    }

    [Fact]
    public void Run_Disabled_NoOp()
    {
        var cfg = Config(triggerCount: 1, keepRecent: 0, enabled: false);
        var micro = new MicroCompactor(cfg);
        var messages = new List<ChatMessage> { AssistantCall("c1"), ToolResult("c1") };
        var result = micro.Run(messages);

        Assert.Equal(MicroCompactTrigger.None, result.Trigger);
    }
}
