using System.Text.Json.Nodes;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools;
using DotCraft.Tests.Sessions.Protocol.AppServer;

namespace DotCraft.Tests.Sessions.Protocol;

public sealed class WelcomeSuggestionServiceTests : IDisposable
{
    private readonly string _workspacePath;
    private readonly string _craftPath;
    private readonly ThreadStore _threadStore;
    private readonly MemoryStore _memoryStore;
    private readonly TestableSessionService _sessionService;

    public WelcomeSuggestionServiceTests()
    {
        _workspacePath = Path.Combine(Path.GetTempPath(), "welcome-suggest-tests", Guid.NewGuid().ToString("N"));
        _craftPath = Path.Combine(_workspacePath, ".craft");
        Directory.CreateDirectory(_craftPath);

        _threadStore = new ThreadStore(_craftPath);
        _memoryStore = new MemoryStore(_craftPath);
        _sessionService = new TestableSessionService(_threadStore);
    }

    [Fact]
    public async Task SuggestAsync_WhenHistoryIsInsufficient_ReturnsFallback()
    {
        await CreateThreadWithMessagesAsync("Need help", "/new", "thanks");

        var service = CreateService();

        var result = await service.SuggestAsync(new WelcomeSuggestionsParams
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        });

        Assert.Equal("fallback", result.Source);
        Assert.Equal(4, result.Items.Count);
        Assert.Null(_sessionService.LastSubmittedMessages);
    }

    [Fact]
    public async Task SuggestAsync_FiltersNoise_UsesWorkspaceMemory_AndDeletesTempThread()
    {
        var thread = await CreateThreadWithMessagesAsync(
            "Review the Desktop welcome flow and identify where dynamic quick suggestions should plug in.",
            "/new",
            "thanks",
            "Review the Desktop welcome flow and identify where dynamic quick suggestions should plug in.",
            "Trace how thread history and workspace memory are loaded for the current workspace.");
        await CreateThreadWithMessagesAsync(
            "Map the current workspace memory loading path and suggest how to reuse it for welcome suggestions.",
            "继续",
            "Make sure the generated suggestions feel like likely next tasks, not generic categories.");

        _memoryStore.WriteLongTerm("The team is working on Desktop dynamic welcome suggestions.");
        _memoryStore.AppendHistory("Recent focus: thread history, welcome shortcuts, and workspace memory integration.");

        var initialThreadCount = (await _threadStore.LoadIndexAsync()).Count;

        _sessionService.SubmitInputHandler = (threadId, _, _) =>
        {
            return
            [
                new SessionEvent
                {
                    EventId = "evt_1",
                    EventType = SessionEventType.ItemCompleted,
                    ThreadId = threadId,
                    TurnId = "turn_001",
                    ItemId = "item_001",
                    Timestamp = DateTimeOffset.UtcNow,
                    Payload = new SessionItem
                    {
                        Id = "item_001",
                        TurnId = "turn_001",
                        Type = ItemType.ToolCall,
                        Status = ItemStatus.Completed,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CompletedAt = DateTimeOffset.UtcNow,
                        Payload = new ToolCallPayload
                        {
                            ToolName = WelcomeSuggestionMethods.ToolName,
                            CallId = "call_001",
                            Arguments = new JsonObject
                            {
                                ["items"] = new JsonArray
                                {
                                    new JsonObject
                                    {
                                        ["title"] = "Review welcome flow",
                                        ["prompt"] = "Review the Desktop welcome flow and point out where dynamic quick suggestions should be injected.",
                                        ["reason"] = "The recent history repeatedly mentions Desktop welcome flow work."
                                    },
                                    new JsonObject
                                    {
                                        ["title"] = "Trace thread history",
                                        ["prompt"] = "Trace how current-workspace thread history is loaded so we can reuse it to generate welcome suggestions.",
                                        ["reason"] = "Recent threads focus on thread history loading."
                                    },
                                    new JsonObject
                                    {
                                        ["title"] = "Reuse workspace memory",
                                        ["prompt"] = "Audit how MEMORY.md and HISTORY.md are loaded today and propose the cleanest way to feed them into welcome suggestions.",
                                        ["reason"] = "Workspace memory appears in both recent messages and HISTORY.md."
                                    },
                                    new JsonObject
                                    {
                                        ["title"] = "Tune suggestion wording",
                                        ["prompt"] = "Design four welcome suggestions that feel like likely next tasks for this workspace instead of generic feature categories.",
                                        ["reason"] = "Recent user intent explicitly asks for next-task style suggestions."
                                    }
                                }
                            }
                        }
                    }
                }
            ];
        };

        var service = CreateService();
        var result = await service.SuggestAsync(new WelcomeSuggestionsParams
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        });

        Assert.Equal("dynamic", result.Source);
        Assert.Equal(4, result.Items.Count);
        Assert.NotNull(_sessionService.LastSubmittedMessages);
        var evidenceMessage = Assert.Single(_sessionService.LastSubmittedMessages!);
        Assert.Contains("Desktop welcome flow", evidenceMessage.Text);
        Assert.Contains("thread history", evidenceMessage.Text);
        Assert.Contains("MEMORY.md", evidenceMessage.Text);
        Assert.DoesNotContain("/new", evidenceMessage.Text);
        Assert.DoesNotContain("thanks", evidenceMessage.Text);
        Assert.DoesNotContain("继续", evidenceMessage.Text);

        var remainingThreads = await _threadStore.LoadIndexAsync();
        Assert.Equal(initialThreadCount, remainingThreads.Count);
        Assert.DoesNotContain(remainingThreads, summary =>
            string.Equals(summary.OriginChannel, WelcomeSuggestionConstants.ChannelName, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(thread.Id, remainingThreads.First().Id);
    }

    private WelcomeSuggestionService CreateService() =>
        new(_sessionService, _threadStore, _memoryStore, _workspacePath, logger: null);

    private SessionIdentity CreateIdentity() => new()
    {
        ChannelName = "dotcraft-desktop",
        UserId = "local",
        ChannelContext = $"workspace:{_workspacePath}",
        WorkspacePath = _workspacePath
    };

    private async Task<SessionThread> CreateThreadWithMessagesAsync(params string[] messages)
    {
        var thread = await _sessionService.CreateThreadAsync(CreateIdentity());
        foreach (var message in messages)
        {
            var turnId = SessionIdGenerator.NewTurnId(thread.Turns.Count + 1);
            thread.Turns.Add(new SessionTurn
            {
                Id = turnId,
                ThreadId = thread.Id,
                Status = TurnStatus.Completed,
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                Items =
                [
                    new SessionItem
                    {
                        Id = SessionIdGenerator.NewItemId(1),
                        TurnId = turnId,
                        Type = ItemType.UserMessage,
                        Status = ItemStatus.Completed,
                        CreatedAt = DateTimeOffset.UtcNow,
                        CompletedAt = DateTimeOffset.UtcNow,
                        Payload = new UserMessagePayload { Text = message }
                    }
                ]
            });
        }

        thread.LastActiveAt = DateTimeOffset.UtcNow;
        await _threadStore.SaveThreadAsync(thread);
        return thread;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspacePath, recursive: true);
        }
        catch
        {
            // best effort
        }
    }
}
