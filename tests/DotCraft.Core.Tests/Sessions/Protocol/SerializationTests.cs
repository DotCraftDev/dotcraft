using System.Text.Json;
using System.Text.Json.Nodes;
using DotCraft.Sessions.Protocol;

namespace DotCraft.Core.Tests.Sessions.Protocol;

public class SerializationTests
{
    private static readonly JsonSerializerOptions Opts = SessionJsonOptions.Default;

    // -------------------------------------------------------------------------
    // Enum serialization
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(ThreadStatus.Active, "Active")]
    [InlineData(ThreadStatus.Paused, "Paused")]
    [InlineData(ThreadStatus.Archived, "Archived")]
    public void ThreadStatus_SerializesAsString(ThreadStatus status, string expected)
    {
        var json = JsonSerializer.Serialize(status, Opts);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(TurnStatus.Running, "Running")]
    [InlineData(TurnStatus.Completed, "Completed")]
    [InlineData(TurnStatus.WaitingApproval, "WaitingApproval")]
    [InlineData(TurnStatus.Failed, "Failed")]
    [InlineData(TurnStatus.Cancelled, "Cancelled")]
    public void TurnStatus_SerializesAsString(TurnStatus status, string expected)
    {
        var json = JsonSerializer.Serialize(status, Opts);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(ItemType.UserMessage, "UserMessage")]
    [InlineData(ItemType.AgentMessage, "AgentMessage")]
    [InlineData(ItemType.ReasoningContent, "ReasoningContent")]
    [InlineData(ItemType.ToolCall, "ToolCall")]
    [InlineData(ItemType.ToolResult, "ToolResult")]
    [InlineData(ItemType.ApprovalRequest, "ApprovalRequest")]
    [InlineData(ItemType.ApprovalResponse, "ApprovalResponse")]
    [InlineData(ItemType.Error, "Error")]
    public void ItemType_SerializesAsString(ItemType type, string expected)
    {
        var json = JsonSerializer.Serialize(type, Opts);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(ItemStatus.Started, "Started")]
    [InlineData(ItemStatus.Streaming, "Streaming")]
    [InlineData(ItemStatus.Completed, "Completed")]
    public void ItemStatus_SerializesAsString(ItemStatus status, string expected)
    {
        var json = JsonSerializer.Serialize(status, Opts);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(HistoryMode.Server, "Server")]
    [InlineData(HistoryMode.Client, "Client")]
    public void HistoryMode_SerializesAsString(HistoryMode mode, string expected)
    {
        var json = JsonSerializer.Serialize(mode, Opts);
        Assert.Equal($"\"{expected}\"", json);
    }

    [Theory]
    [InlineData(SessionEventType.ThreadCreated, "ThreadCreated")]
    [InlineData(SessionEventType.TurnStarted, "TurnStarted")]
    [InlineData(SessionEventType.ItemDelta, "ItemDelta")]
    [InlineData(SessionEventType.ApprovalRequested, "ApprovalRequested")]
    public void SessionEventType_SerializesAsString(SessionEventType type, string expected)
    {
        var json = JsonSerializer.Serialize(type, Opts);
        Assert.Equal($"\"{expected}\"", json);
    }

    // -------------------------------------------------------------------------
    // SessionItem round-trips
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionItem_UserMessage_RoundTrip()
    {
        var item = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_001",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 3, 15, 10, 0, 1, TimeSpan.Zero)
        };
        item.Payload = new UserMessagePayload { Text = "Hello!", SenderId = "u123", SenderName = "Alice" };

        var json = JsonSerializer.Serialize(item, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionItem>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Equal(item.Id, deserialized.Id);
        Assert.Equal(item.TurnId, deserialized.TurnId);
        Assert.Equal(item.Type, deserialized.Type);
        Assert.Equal(item.Status, deserialized.Status);
        Assert.Equal(item.CreatedAt, deserialized.CreatedAt);
        Assert.Equal(item.CompletedAt, deserialized.CompletedAt);

        var payload = deserialized.AsUserMessage;
        Assert.NotNull(payload);
        Assert.Equal("Hello!", payload.Text);
        Assert.Equal("u123", payload.SenderId);
        Assert.Equal("Alice", payload.SenderName);
    }

    [Fact]
    public void SessionItem_AgentMessage_RoundTrip()
    {
        var item = BuildItem(ItemType.AgentMessage, ItemStatus.Completed,
            new AgentMessagePayload { Text = "Sure, I can help." });

        var deserialized = RoundTrip(item);
        var payload = deserialized.AsAgentMessage;
        Assert.NotNull(payload);
        Assert.Equal("Sure, I can help.", payload.Text);
    }

    [Fact]
    public void SessionItem_ReasoningContent_RoundTrip()
    {
        var item = BuildItem(ItemType.ReasoningContent, ItemStatus.Completed,
            new ReasoningContentPayload { Text = "Let me think..." });

        var deserialized = RoundTrip(item);
        Assert.NotNull(deserialized.AsReasoningContent);
        Assert.Equal("Let me think...", deserialized.AsReasoningContent!.Text);
    }

    [Fact]
    public void SessionItem_ToolCall_RoundTrip()
    {
        var args = new JsonObject { ["path"] = "/tmp/file.txt" };
        var item = BuildItem(ItemType.ToolCall, ItemStatus.Completed,
            new ToolCallPayload { ToolName = "read_file", Arguments = args, CallId = "call_abc" });

        var deserialized = RoundTrip(item);
        var payload = deserialized.AsToolCall;
        Assert.NotNull(payload);
        Assert.Equal("read_file", payload.ToolName);
        Assert.Equal("call_abc", payload.CallId);
        Assert.NotNull(payload.Arguments);
    }

    [Fact]
    public void SessionItem_ToolResult_RoundTrip()
    {
        var item = BuildItem(ItemType.ToolResult, ItemStatus.Completed,
            new ToolResultPayload { CallId = "call_abc", Result = "file contents", Success = true });

        var deserialized = RoundTrip(item);
        var payload = deserialized.AsToolResult;
        Assert.NotNull(payload);
        Assert.Equal("call_abc", payload.CallId);
        Assert.Equal("file contents", payload.Result);
        Assert.True(payload.Success);
    }

    [Fact]
    public void SessionItem_ApprovalRequest_RoundTrip()
    {
        var item = BuildItem(ItemType.ApprovalRequest, ItemStatus.Completed,
            new ApprovalRequestPayload
            {
                ApprovalType = "file",
                Operation = "write",
                Target = "/etc/config",
                RequestId = "req_001"
            });

        var deserialized = RoundTrip(item);
        var payload = deserialized.AsApprovalRequest;
        Assert.NotNull(payload);
        Assert.Equal("file", payload.ApprovalType);
        Assert.Equal("write", payload.Operation);
        Assert.Equal("/etc/config", payload.Target);
        Assert.Equal("req_001", payload.RequestId);
    }

    [Fact]
    public void SessionItem_ApprovalResponse_RoundTrip()
    {
        var item = BuildItem(ItemType.ApprovalResponse, ItemStatus.Completed,
            new ApprovalResponsePayload { RequestId = "req_001", Approved = true });

        var deserialized = RoundTrip(item);
        var payload = deserialized.AsApprovalResponse;
        Assert.NotNull(payload);
        Assert.Equal("req_001", payload.RequestId);
        Assert.True(payload.Approved);
    }

    [Fact]
    public void SessionItem_Error_RoundTrip()
    {
        var item = BuildItem(ItemType.Error, ItemStatus.Completed,
            new ErrorPayload { Message = "Something went wrong", Code = "agent_error", Fatal = true });

        var deserialized = RoundTrip(item);
        var payload = deserialized.AsError;
        Assert.NotNull(payload);
        Assert.Equal("Something went wrong", payload.Message);
        Assert.Equal("agent_error", payload.Code);
        Assert.True(payload.Fatal);
    }

    [Fact]
    public void SessionItem_NullPayload_RoundTrip()
    {
        var item = BuildItem(ItemType.UserMessage, ItemStatus.Started, null);
        var deserialized = RoundTrip(item);
        Assert.Null(deserialized.Payload);
    }

    // -------------------------------------------------------------------------
    // SessionTurn round-trips
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionTurn_RoundTrip_PreservesItemOrder()
    {
        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_20260315_abc123",
            Status = TurnStatus.Completed,
            StartedAt = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 3, 15, 10, 2, 0, TimeSpan.Zero),
            TokenUsage = new TokenUsageInfo { InputTokens = 100, OutputTokens = 50, TotalTokens = 150 },
            OriginChannel = "cli"
        };

        var userMsg = BuildItem(ItemType.UserMessage, ItemStatus.Completed,
            new UserMessagePayload { Text = "Hello" });
        userMsg.Id = "item_001";
        userMsg.TurnId = "turn_001";

        var agentMsg = BuildItem(ItemType.AgentMessage, ItemStatus.Completed,
            new AgentMessagePayload { Text = "Hi there" });
        agentMsg.Id = "item_002";
        agentMsg.TurnId = "turn_001";

        turn.Input = userMsg;
        turn.Items = [userMsg, agentMsg];

        var json = JsonSerializer.Serialize(turn, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionTurn>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Equal(turn.Id, deserialized.Id);
        Assert.Equal(turn.ThreadId, deserialized.ThreadId);
        Assert.Equal(TurnStatus.Completed, deserialized.Status);
        Assert.Equal(2, deserialized.Items.Count);
        Assert.Equal("item_001", deserialized.Items[0].Id);
        Assert.Equal("item_002", deserialized.Items[1].Id);
        Assert.NotNull(deserialized.TokenUsage);
        Assert.Equal(100, deserialized.TokenUsage.InputTokens);
        Assert.Equal("cli", deserialized.OriginChannel);
    }

    [Fact]
    public void SessionTurn_NullableFields_RoundTrip()
    {
        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_x",
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
            // CompletedAt, TokenUsage, Error are null
        };

        var json = JsonSerializer.Serialize(turn, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionTurn>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.CompletedAt);
        Assert.Null(deserialized.TokenUsage);
        Assert.Null(deserialized.Error);
    }

    // -------------------------------------------------------------------------
    // SessionThread round-trips
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionThread_RoundTrip_FullObject()
    {
        var thread = new SessionThread
        {
            Id = "thread_20260315_a3f2k9",
            WorkspacePath = "/path/to/workspace",
            UserId = "user123",
            OriginChannel = "qq",
            DisplayName = "Help me fix the login bug",
            Status = ThreadStatus.Active,
            CreatedAt = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero),
            LastActiveAt = new DateTimeOffset(2026, 3, 15, 10, 5, 0, TimeSpan.Zero),
            HistoryMode = HistoryMode.Server,
            Metadata = new Dictionary<string, string>
            {
                ["customKey"] = "qq_12345_67890",
                ["qqGroupId"] = "12345"
            }
        };

        var json = JsonSerializer.Serialize(thread, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionThread>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Equal(thread.Id, deserialized.Id);
        Assert.Equal(thread.WorkspacePath, deserialized.WorkspacePath);
        Assert.Equal(thread.UserId, deserialized.UserId);
        Assert.Equal(thread.OriginChannel, deserialized.OriginChannel);
        Assert.Equal(thread.DisplayName, deserialized.DisplayName);
        Assert.Equal(ThreadStatus.Active, deserialized.Status);
        Assert.Equal(HistoryMode.Server, deserialized.HistoryMode);
        Assert.Equal(2, deserialized.Metadata.Count);
        Assert.Equal("qq_12345_67890", deserialized.Metadata["customKey"]);
        Assert.Empty(deserialized.Turns);
    }

    [Fact]
    public void SessionThread_NullableFields_RoundTrip()
    {
        var thread = new SessionThread
        {
            Id = "thread_20260315_xyz",
            WorkspacePath = "/workspace",
            OriginChannel = "cli",
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow
            // UserId, DisplayName, Configuration are null
        };

        var json = JsonSerializer.Serialize(thread, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionThread>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Null(deserialized.UserId);
        Assert.Null(deserialized.DisplayName);
        Assert.Null(deserialized.Configuration);
    }

    // -------------------------------------------------------------------------
    // SessionEvent round-trips
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionEvent_TurnStarted_RoundTrip()
    {
        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_abc",
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };

        var evt = new SessionEvent
        {
            EventId = "evt_001",
            EventType = SessionEventType.TurnStarted,
            ThreadId = "thread_abc",
            TurnId = "turn_001",
            Timestamp = DateTimeOffset.UtcNow
        };
        evt.Payload = turn;

        var json = JsonSerializer.Serialize(evt, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionEvent>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Equal("evt_001", deserialized.EventId);
        Assert.Equal(SessionEventType.TurnStarted, deserialized.EventType);
        Assert.Equal("thread_abc", deserialized.ThreadId);
        Assert.Equal("turn_001", deserialized.TurnId);
        Assert.Null(deserialized.ItemId);

        var turnPayload = deserialized.TurnPayload;
        Assert.NotNull(turnPayload);
        Assert.Equal("turn_001", turnPayload.Id);
    }

    [Fact]
    public void SessionEvent_ItemDelta_RoundTrip()
    {
        var evt = new SessionEvent
        {
            EventId = "evt_005",
            EventType = SessionEventType.ItemDelta,
            ThreadId = "thread_abc",
            TurnId = "turn_001",
            ItemId = "item_002",
            Timestamp = DateTimeOffset.UtcNow
        };
        evt.Payload = new AgentMessageDelta { TextDelta = "Hello " };

        var json = JsonSerializer.Serialize(evt, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionEvent>(json, Opts);

        Assert.NotNull(deserialized);
        Assert.Equal(SessionEventType.ItemDelta, deserialized.EventType);
        var delta = deserialized.DeltaPayload;
        Assert.NotNull(delta);
        Assert.Equal("Hello ", delta.TextDelta);
    }

    [Fact]
    public void SessionEvent_ThreadStatusChanged_RoundTrip()
    {
        var evt = new SessionEvent
        {
            EventId = "evt_010",
            EventType = SessionEventType.ThreadStatusChanged,
            ThreadId = "thread_abc",
            Timestamp = DateTimeOffset.UtcNow
        };
        evt.Payload = new ThreadStatusChangedPayload
        {
            PreviousStatus = ThreadStatus.Active,
            NewStatus = ThreadStatus.Paused
        };

        var json = JsonSerializer.Serialize(evt, Opts);
        var deserialized = JsonSerializer.Deserialize<SessionEvent>(json, Opts);

        Assert.NotNull(deserialized);
        var payload = deserialized.StatusChangedPayload;
        Assert.NotNull(payload);
        Assert.Equal(ThreadStatus.Active, payload.PreviousStatus);
        Assert.Equal(ThreadStatus.Paused, payload.NewStatus);
    }

    // -------------------------------------------------------------------------
    // SessionIdentity equality
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionIdentity_Equality_ByAllFields()
    {
        var a = new SessionIdentity
        {
            ChannelName = "qq",
            UserId = "user123",
            ChannelContext = "group_456",
            WorkspacePath = "/workspace"
        };

        var b = new SessionIdentity
        {
            ChannelName = "qq",
            UserId = "user123",
            ChannelContext = "group_456",
            WorkspacePath = "/workspace"
        };

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void SessionIdentity_Inequality_DifferentChannel()
    {
        var a = new SessionIdentity { ChannelName = "qq", UserId = "user123", WorkspacePath = "/w" };
        var b = new SessionIdentity { ChannelName = "acp", UserId = "user123", WorkspacePath = "/w" };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SessionIdentity_Inequality_DifferentContext()
    {
        var a = new SessionIdentity { ChannelName = "qq", UserId = "u1", ChannelContext = "g1", WorkspacePath = "/w" };
        var b = new SessionIdentity { ChannelName = "qq", UserId = "u1", ChannelContext = "g2", WorkspacePath = "/w" };
        Assert.NotEqual(a, b);
    }

    // -------------------------------------------------------------------------
    // SessionIdGenerator format validation
    // -------------------------------------------------------------------------

    [Fact]
    public void NewThreadId_HasCorrectFormat()
    {
        var id = SessionIdGenerator.NewThreadId();
        // e.g. "thread_20260315_a3f2k9"
        Assert.StartsWith("thread_", id);
        var parts = id.Split('_');
        Assert.Equal(3, parts.Length);
        Assert.Equal(8, parts[1].Length); // yyyyMMdd
        Assert.Equal(6, parts[2].Length);
    }

    [Fact]
    public void NewTurnId_HasCorrectFormat()
    {
        Assert.Equal("turn_001", SessionIdGenerator.NewTurnId(1));
        Assert.Equal("turn_042", SessionIdGenerator.NewTurnId(42));
        Assert.Equal("turn_999", SessionIdGenerator.NewTurnId(999));
    }

    [Fact]
    public void NewItemId_HasCorrectFormat()
    {
        Assert.Equal("item_001", SessionIdGenerator.NewItemId(1));
        Assert.Equal("item_010", SessionIdGenerator.NewItemId(10));
        Assert.Equal("item_100", SessionIdGenerator.NewItemId(100));
    }

    [Fact]
    public void NewThreadId_IsUnique()
    {
        var ids = Enumerable.Range(0, 100).Select(_ => SessionIdGenerator.NewThreadId()).ToHashSet();
        Assert.Equal(100, ids.Count);
    }

    // -------------------------------------------------------------------------
    // TokenUsageInfo
    // -------------------------------------------------------------------------

    [Fact]
    public void TokenUsageInfo_Addition()
    {
        var a = new TokenUsageInfo { InputTokens = 100, OutputTokens = 50, TotalTokens = 150 };
        var b = new TokenUsageInfo { InputTokens = 200, OutputTokens = 80, TotalTokens = 280 };
        var sum = a + b;
        Assert.Equal(300, sum.InputTokens);
        Assert.Equal(130, sum.OutputTokens);
        Assert.Equal(430, sum.TotalTokens);
    }

    [Fact]
    public void TokenUsageInfo_RoundTrip()
    {
        var usage = new TokenUsageInfo { InputTokens = 1200, OutputTokens = 800, TotalTokens = 2000 };
        var json = JsonSerializer.Serialize(usage, Opts);
        var deserialized = JsonSerializer.Deserialize<TokenUsageInfo>(json, Opts);
        Assert.NotNull(deserialized);
        Assert.Equal(usage.InputTokens, deserialized.InputTokens);
        Assert.Equal(usage.OutputTokens, deserialized.OutputTokens);
        Assert.Equal(usage.TotalTokens, deserialized.TotalTokens);
    }

    // -------------------------------------------------------------------------
    // ThreadSummary.FromThread
    // -------------------------------------------------------------------------

    [Fact]
    public void ThreadSummary_FromThread_CopiesFields()
    {
        var thread = new SessionThread
        {
            Id = "thread_20260315_abc",
            UserId = "u1",
            OriginChannel = "qq",
            DisplayName = "Test",
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string> { ["k"] = "v" }
        };
        thread.Turns.Add(new SessionTurn { Id = "turn_001", ThreadId = thread.Id, Status = TurnStatus.Completed, StartedAt = DateTimeOffset.UtcNow });
        thread.Turns.Add(new SessionTurn { Id = "turn_002", ThreadId = thread.Id, Status = TurnStatus.Completed, StartedAt = DateTimeOffset.UtcNow });

        var summary = ThreadSummary.FromThread(thread);

        Assert.Equal(thread.Id, summary.Id);
        Assert.Equal(thread.UserId, summary.UserId);
        Assert.Equal(thread.OriginChannel, summary.OriginChannel);
        Assert.Equal(thread.DisplayName, summary.DisplayName);
        Assert.Equal(ThreadStatus.Active, summary.Status);
        Assert.Equal(2, summary.TurnCount);
        Assert.Equal("v", summary.Metadata["k"]);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static SessionItem BuildItem(ItemType type, ItemStatus status, object? payload)
    {
        var item = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_001",
            Type = type,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        };
        item.Payload = payload;
        return item;
    }

    private static SessionItem RoundTrip(SessionItem item)
    {
        var json = JsonSerializer.Serialize(item, Opts);
        var result = JsonSerializer.Deserialize<SessionItem>(json, Opts);
        Assert.NotNull(result);
        return result;
    }

    // -------------------------------------------------------------------------
    // SessionTurn.Input deserialization — regression guard for /load history display
    // -------------------------------------------------------------------------

    [Fact]
    public void SessionTurn_Input_UserMessagePayload_SurvivesRoundTrip()
    {
        var userItem = new SessionItem
        {
            Id = "item_001",
            TurnId = "turn_001",
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = new DateTimeOffset(2026, 3, 15, 7, 32, 21, TimeSpan.Zero),
            CompletedAt = new DateTimeOffset(2026, 3, 15, 7, 32, 21, TimeSpan.Zero),
            Payload = new UserMessagePayload { Text = "你好" }
        };

        var turn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = "thread_20260315_lbdp2i",
            Status = TurnStatus.Completed,
            StartedAt = new DateTimeOffset(2026, 3, 15, 7, 32, 21, TimeSpan.Zero),
            Input = userItem,
            Items = [userItem]
        };

        var thread = new SessionThread
        {
            Id = "thread_20260315_lbdp2i",
            WorkspacePath = "/workspace",
            OriginChannel = "cli",
            UserId = "local",
            Status = ThreadStatus.Active,
            CreatedAt = turn.StartedAt,
            LastActiveAt = turn.StartedAt,
            Turns = [turn]
        };

        var json = JsonSerializer.Serialize(thread, Opts);
        var loaded = JsonSerializer.Deserialize<SessionThread>(json, Opts);

        Assert.NotNull(loaded);
        Assert.Single(loaded.Turns);

        var loadedTurn = loaded.Turns[0];

        // Verify turn.Input is deserialized with correct type
        Assert.NotNull(loadedTurn.Input);
        Assert.Equal(ItemType.UserMessage, loadedTurn.Input.Type);
        var inputPayload = loadedTurn.Input.Payload as UserMessagePayload;
        Assert.NotNull(inputPayload);
        Assert.Equal("你好", inputPayload.Text);

        // Verify turn.Items[0] also has correct payload
        Assert.Single(loadedTurn.Items);
        var firstItemPayload = loadedTurn.Items[0].Payload as UserMessagePayload;
        Assert.NotNull(firstItemPayload);
        Assert.Equal("你好", firstItemPayload.Text);
    }

    [Fact]
    public void SessionThread_WithMultipleTurns_Input_UserMessagePayload_SurvivesRoundTrip()
    {
        static SessionItem MakeUserItem(string turnId, string text) => new()
        {
            Id = "item_001",
            TurnId = turnId,
            Type = ItemType.UserMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new UserMessagePayload { Text = text }
        };

        static SessionItem MakeAgentItem(string turnId, string text) => new()
        {
            Id = "item_002",
            TurnId = turnId,
            Type = ItemType.AgentMessage,
            Status = ItemStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Payload = new AgentMessagePayload { Text = text }
        };

        var turn1User = MakeUserItem("turn_001", "你好");
        var turn2User = MakeUserItem("turn_002", "你有哪些工具呢");

        var thread = new SessionThread
        {
            Id = "thread_20260315_test",
            WorkspacePath = "/workspace",
            OriginChannel = "cli",
            UserId = "local",
            Status = ThreadStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            LastActiveAt = DateTimeOffset.UtcNow,
            Turns =
            [
                new SessionTurn
                {
                    Id = "turn_001",
                    ThreadId = "thread_20260315_test",
                    Status = TurnStatus.Completed,
                    StartedAt = DateTimeOffset.UtcNow,
                    Input = turn1User,
                    Items = [turn1User, MakeAgentItem("turn_001", "你好！我是DotCraft")]
                },
                new SessionTurn
                {
                    Id = "turn_002",
                    ThreadId = "thread_20260315_test",
                    Status = TurnStatus.Completed,
                    StartedAt = DateTimeOffset.UtcNow,
                    Input = turn2User,
                    Items = [turn2User, MakeAgentItem("turn_002", "我有以下工具...")]
                }
            ]
        };

        var json = JsonSerializer.Serialize(thread, Opts);
        var loaded = JsonSerializer.Deserialize<SessionThread>(json, Opts);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Turns.Count);

        for (var i = 0; i < loaded.Turns.Count; i++)
        {
            var t = loaded.Turns[i];
            Assert.NotNull(t.Input);
            Assert.Equal(ItemType.UserMessage, t.Input.Type);
            var payload = t.Input.Payload as UserMessagePayload;
            Assert.NotNull(payload);
        }

        Assert.Equal("你好", (loaded.Turns[0].Input!.Payload as UserMessagePayload)!.Text);
        Assert.Equal("你有哪些工具呢", (loaded.Turns[1].Input!.Payload as UserMessagePayload)!.Text);
    }
}
