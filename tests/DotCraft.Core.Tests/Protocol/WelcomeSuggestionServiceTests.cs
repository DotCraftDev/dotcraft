using System.Text.Json.Nodes;
using DotCraft.Memory;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tools;
using DotCraft.Tests.Sessions.Protocol.AppServer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.AI;

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
    public async Task SuggestAsync_WhenHistoryIsInsufficient_ReturnsNoneAndSkipsModel()
    {
        await CreateThreadWithMessagesAsync("Need help", "/new", "thanks");

        var service = CreateService();

        var result = await service.SuggestAsync(new WelcomeSuggestionsParams
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        });

        Assert.Equal("none", result.Source);
        Assert.Empty(result.Items);
        Assert.Empty(_sessionService.LastSubmittedContent);
        Assert.Null(_sessionService.LastSubmittedMessages);
    }

    [Fact]
    public async Task SuggestAsync_WhenWorkspaceConfigDisablesSuggestions_ReturnsNone()
    {
        Directory.CreateDirectory(_craftPath);
        await File.WriteAllTextAsync(
            Path.Combine(_craftPath, "config.json"),
            """
            {
              "WelcomeSuggestions": {
                "Enabled": false
              }
            }
            """);

        await CreateThreadWithMessagesAsync(
            "Review the Desktop welcome flow and identify where dynamic quick suggestions should plug in.",
            "Trace how thread history and workspace memory are loaded for the current workspace.");

        var service = CreateService();

        var result = await service.SuggestAsync(new WelcomeSuggestionsParams
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        });

        Assert.Equal("none", result.Source);
        Assert.Empty(result.Items);
        Assert.Empty(_sessionService.LastSubmittedContent);
    }

    [Fact]
    public async Task SuggestAsync_FiltersNoise_UsesServerManagedTempThread_AndDeletesTempThread()
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
        Assert.NotEmpty(_sessionService.LastSubmittedContent);
        Assert.Null(_sessionService.LastSubmittedMessages);
        Assert.Contains(
            "Inspect recent workspace history and memory, infer the likely next tasks",
            string.Concat(_sessionService.LastSubmittedContent.OfType<TextContent>().Select(item => item.Text)));

        var remainingThreads = await _threadStore.LoadIndexAsync();
        Assert.Equal(initialThreadCount, remainingThreads.Count);
        Assert.DoesNotContain(remainingThreads, summary =>
            string.Equals(summary.OriginChannel, WelcomeSuggestionConstants.ChannelName, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(remainingThreads, summary => summary.Id == thread.Id);
    }

    [Fact]
    public async Task ReadWelcomeThreadHistory_ReturnsSnippets_AgentSummary_AndDominantIntents()
    {
        var thread = await _sessionService.CreateThreadAsync(CreateIdentity());
        var turnId = SessionIdGenerator.NewTurnId(1);
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
                    Payload = new UserMessagePayload
                    {
                        Text = "Trace how welcome suggestions reuse workspace memory and thread history in Desktop."
                    }
                },
                new SessionItem
                {
                    Id = SessionIdGenerator.NewItemId(2),
                    TurnId = turnId,
                    Type = ItemType.AgentMessage,
                    Status = ItemStatus.Completed,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Payload = new AgentMessagePayload
                    {
                        Text = "The likely implementation path is to reuse the welcome suggestion service and tighten the prompt plus evidence extraction."
                    }
                }
            ]
        });

        await _threadStore.SaveThreadAsync(thread);
        var methods = new WelcomeSuggestionToolMethods(_threadStore, _memoryStore, _workspacePath);

        var result = await methods.ReadWelcomeThreadHistory(thread.Id);

        Assert.Equal(thread.Id, result.ThreadId);
        Assert.NotEmpty(result.UserSnippets);
        Assert.Contains("workspace memory", result.UserSnippets[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("implementation path", result.AgentSummary, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(result.DominantIntents);
    }

    [Fact]
    public async Task ReadWelcomeWorkspaceMemory_ExtractsHighlights()
    {
        _memoryStore.WriteLongTerm("""
            The current focus is improving Desktop welcome suggestions.
            Make the generated prompts specific to thread history and memory.
            """);
        _memoryStore.AppendHistory("""
            Welcome suggestion output should mention concrete modules, prompts, or settings instead of generic onboarding.
            """);

        var methods = new WelcomeSuggestionToolMethods(_threadStore, _memoryStore, _workspacePath);

        var result = await methods.ReadWelcomeWorkspaceMemory();

        Assert.NotEmpty(result.MemoryHighlights);
        Assert.Contains(result.MemoryHighlights, item => item.Contains("welcome suggestions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SuggestAsync_WhenModelReturnsGenericSuggestions_ReturnsNone()
    {
        await CreateThreadWithMessagesAsync(
            "Review how welcome suggestions are generated from workspace history and memory.",
            "Tighten the prompt so suggestions mention specific modules and tasks.");

        _sessionService.SubmitInputHandler = (threadId, _, _) =>
        {
            return
            [
                new SessionEvent
                {
                    EventId = "evt_generic",
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
                            CallId = "call_generic",
                            Arguments = new JsonObject
                            {
                                ["items"] = new JsonArray
                                {
                                    new JsonObject
                                    {
                                        ["title"] = "Explore features",
                                        ["prompt"] = "What features does DotCraft Desktop offer?",
                                        ["reason"] = "Generic onboarding."
                                    },
                                    new JsonObject
                                    {
                                        ["title"] = "Learn the basics",
                                        ["prompt"] = "Teach me the basics of using DotCraft.",
                                        ["reason"] = "Generic onboarding."
                                    },
                                    new JsonObject
                                    {
                                        ["title"] = "Workspace setup",
                                        ["prompt"] = "Help me set up my workspace.",
                                        ["reason"] = "Generic onboarding."
                                    },
                                    new JsonObject
                                    {
                                        ["title"] = "New project",
                                        ["prompt"] = "Help me start a new project.",
                                        ["reason"] = "Generic onboarding."
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

        Assert.Equal("none", result.Source);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task SuggestAsync_WhenSharedInflightIsCanceledByAnotherCaller_ReturnsNoneForCurrentCaller()
    {
        await CreateThreadWithMessagesAsync(
            "Review how welcome suggestions reuse workspace history and memory in Desktop.",
            "Trace the welcome suggestion service and tighten its cancellation behavior.");

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessionService.SubmitInputHandler = (_, _, _) =>
        {
            gate.Task.GetAwaiter().GetResult();
            throw new OperationCanceledException();
        };

        var service = CreateService();
        using var firstCallerCts = new CancellationTokenSource();

        var firstTask = service.SuggestAsync(new WelcomeSuggestionsParams
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        }, firstCallerCts.Token);

        await WaitForAsync(() => _sessionService.LastSubmittedContent.Count > 0);

        var secondTask = service.SuggestAsync(new WelcomeSuggestionsParams
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        }, CancellationToken.None);

        firstCallerCts.Cancel();
        gate.TrySetResult();

        var secondResult = await secondTask;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await firstTask);
        Assert.Equal("none", secondResult.Source);
        Assert.Empty(secondResult.Items);
    }

    [Fact]
    public async Task SuggestAsync_WhenCurrentCallerIsCanceled_ThrowsOperationCanceledException()
    {
        await CreateThreadWithMessagesAsync(
            "Review the inflight welcome suggestion deduplication behavior.",
            "Ensure the current caller cancellation still propagates.");

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessionService.SubmitInputHandler = (_, _, _) =>
        {
            gate.Task.GetAwaiter().GetResult();
            throw new OperationCanceledException();
        };

        var service = CreateService();
        using var cts = new CancellationTokenSource();

        var task = service.SuggestAsync(new WelcomeSuggestionsParams
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        }, cts.Token);

        await WaitForAsync(() => _sessionService.LastSubmittedContent.Count > 0);
        cts.Cancel();
        gate.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public async Task SuggestAsync_WhenSecondSharedCallerIsCanceled_ThrowsPromptlyWithoutCancelingFirstCaller()
    {
        await CreateThreadWithMessagesAsync(
            "Review welcome suggestion inflight sharing behavior.",
            "Ensure each caller can cancel independently.");

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _sessionService.SubmitInputHandler = (threadId, _, _) =>
        {
            gate.Task.GetAwaiter().GetResult();
            return
            [
                new SessionEvent
                {
                    EventId = "evt_second_caller_cancel",
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
                            CallId = "call_second_caller_cancel",
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
        var firstTask = service.SuggestAsync(new WelcomeSuggestionsParams
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        }, CancellationToken.None);

        await WaitForAsync(() => _sessionService.LastSubmittedContent.Count > 0);

        using var secondCallerCts = new CancellationTokenSource();
        var secondTask = service.SuggestAsync(new WelcomeSuggestionsParams
        {
            Identity = CreateIdentity(),
            MaxItems = 4
        }, secondCallerCts.Token);

        try
        {
            secondCallerCts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await secondTask)
                .WaitAsync(TimeSpan.FromSeconds(1));
        }
        finally
        {
            gate.TrySetResult();
        }

        var firstResult = await firstTask;
        Assert.Equal("dynamic", firstResult.Source);
        Assert.Equal(4, firstResult.Items.Count);
    }

    private WelcomeSuggestionService CreateService() =>
        new(_sessionService, _threadStore, _memoryStore, _workspacePath, NullLogger<WelcomeSuggestionService>.Instance);

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

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var started = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - started).TotalMilliseconds > timeoutMs)
                throw new TimeoutException("Condition was not satisfied in time.");

            await Task.Delay(10);
        }
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
