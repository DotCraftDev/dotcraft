using DotCraft.Protocol;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class SessionWireModelsTests
{
    [Fact]
    public void ToWireMethodName_ToolCallArgumentsDelta_ReturnsExpectedMethod()
    {
        var evt = new SessionEvent
        {
            EventId = "e1",
            EventType = SessionEventType.ItemDelta,
            ThreadId = "thread_1",
            TurnId = "turn_1",
            ItemId = "item_1",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new ToolCallArgumentsDelta
            {
                ToolName = "WriteFile",
                CallId = "call_1",
                Delta = "{\"content\":\"hello\""
            }
        };

        Assert.Equal("item/toolCall/argumentsDelta", evt.ToWireMethodName());
    }

    [Fact]
    public void ToWire_ToolCallArgumentsDelta_SerializesPayloadShape()
    {
        var evt = new SessionEvent
        {
            EventId = "e1",
            EventType = SessionEventType.ItemDelta,
            ThreadId = "thread_1",
            TurnId = "turn_1",
            ItemId = "item_1",
            Timestamp = DateTimeOffset.UtcNow,
            Payload = new ToolCallArgumentsDelta
            {
                ToolName = "WriteFile",
                CallId = "call_1",
                Delta = "{\"content\":\"hello\""
            }
        };

        var wire = evt.ToWire();
        Assert.Equal("toolCallArgumentsDelta", wire.PayloadKind);

        var payloadJson = System.Text.Json.JsonSerializer.Serialize(
            wire.Payload, SessionWireJsonOptions.Default);
        using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
        var root = doc.RootElement;
        Assert.Equal("toolCallArguments", root.GetProperty("deltaKind").GetString());
        Assert.Equal("WriteFile", root.GetProperty("toolName").GetString());
        Assert.Equal("call_1", root.GetProperty("callId").GetString());
        Assert.Equal("{\"content\":\"hello\"", root.GetProperty("delta").GetString());
    }
}
