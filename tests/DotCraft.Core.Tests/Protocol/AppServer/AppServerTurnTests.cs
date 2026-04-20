using System.Text.Json;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using Microsoft.Extensions.AI;

namespace DotCraft.Tests.Sessions.Protocol.AppServer;

/// <summary>
/// Tests for turn/* methods (spec Section 5) and delta notification correctness.
/// Validates:
/// - turn/start response shape and inline response ordering
/// - deltaKind field in item/*/delta notifications (Fix 1)
/// - streamingSupport=false suppresses item deltas (Fix 4)
/// - messages forwarded to SubmitInputAsync for historyMode=client (Fix 5)
/// - turn/interrupt triggers CancelTurnAsync
/// </summary>
public sealed class AppServerTurnTests : IDisposable
{
    private readonly AppServerTestHarness _h = new();

    public AppServerTurnTests()
    {
        _h.InitializeAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => _h.Dispose();

    // -------------------------------------------------------------------------
    // turn/start — basic flow
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnStart_SendsResponseBeforeNotifications()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await _h.ExecuteRequestAsync(msg);

        // First message must be the turn/start response
        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
        Assert.True(response.RootElement.GetProperty("result").TryGetProperty("turn", out _),
            "turn/start result must contain a 'turn' field");
    }

    [Fact]
    public async Task TurnStart_ResponseBeforeTurnStartedNotification_Ordering()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var first = await _h.Transport.ReadNextSentAsync();
        var second = await _h.Transport.ReadNextSentAsync();

        // First: response (has 'result' and 'id'), Second: turn/started notification (has 'method')
        Assert.True(first.RootElement.TryGetProperty("result", out _),
            "First message must be the JSON-RPC response");
        Assert.True(second.RootElement.TryGetProperty("method", out var methodEl));
        Assert.Equal(AppServerMethods.TurnStarted, methodEl.GetString());
    }

    [Fact]
    public async Task TurnStart_FullEventSequence_AllNotificationsArriveInOrder()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await _h.ExecuteRequestAsync(msg);

        // Expected: response, turn/started, item/started, item/agentMessage/delta, item/completed, turn/completed
        var all = await _h.Transport.WaitAndDrainAsync(6, TimeSpan.FromSeconds(10));

        Assert.True(all[0].RootElement.TryGetProperty("result", out _)); // response
        AssertMethod(all[1], AppServerMethods.TurnStarted);
        AssertMethod(all[2], AppServerMethods.ItemStarted);
        AssertMethod(all[3], AppServerMethods.ItemAgentMessageDelta);
        AssertMethod(all[4], AppServerMethods.ItemCompleted);
        AssertMethod(all[5], AppServerMethods.TurnCompleted);
    }

    // -------------------------------------------------------------------------
    // Fix 1: deltaKind must be present in delta notifications (spec Section 2.3)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnStart_AgentMessageDelta_IncludesDeltaKind()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var all = await _h.Transport.WaitAndDrainAsync(6, TimeSpan.FromSeconds(10));

        // all[3] is item/agentMessage/delta
        var deltaNotif = all[3];
        var @params = deltaNotif.RootElement.GetProperty("params");
        Assert.True(@params.TryGetProperty("deltaKind", out var deltaKindEl),
            "Delta notification must include 'deltaKind' field (spec Section 2.3)");
        Assert.Equal("agentMessage", deltaKindEl.GetString());
        Assert.True(@params.TryGetProperty("delta", out _), "Delta notification must include 'delta' field");
    }

    [Fact]
    public async Task TurnStart_ReasoningDelta_IncludesDeltaKindReasoningContent()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        var turn = AppServerTestHarness.MakeTurn(thread.Id);

        // Build event sequence with a reasoning delta instead of agentMessage delta
        var events = new SessionEvent[]
        {
            new() {
                EventId = "e1", EventType = SessionEventType.TurnStarted,
                ThreadId = thread.Id, TurnId = turn.Id, Timestamp = DateTimeOffset.UtcNow, Payload = turn
            },
            new() {
                EventId = "e2", EventType = SessionEventType.ItemDelta,
                ThreadId = thread.Id, TurnId = turn.Id, ItemId = "item_001",
                Timestamp = DateTimeOffset.UtcNow,
                Payload = new ReasoningContentDelta { TextDelta = "thinking..." }
            },
            new() {
                EventId = "e3", EventType = SessionEventType.TurnCompleted,
                ThreadId = thread.Id, TurnId = turn.Id, Timestamp = DateTimeOffset.UtcNow,
                Payload = AppServerTestHarness.MakeCompletedTurn(thread.Id)
            }
        };
        _h.Service.EnqueueSubmitEvents(thread.Id, events);

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Think about it" } }
        });
        await _h.ExecuteRequestAsync(msg);

        // response, turn/started, item/reasoning/delta, turn/completed
        var all = await _h.Transport.WaitAndDrainAsync(4, TimeSpan.FromSeconds(10));

        var reasoningDelta = all[2];
        AssertMethod(reasoningDelta, AppServerMethods.ItemReasoningDelta);
        var @params = reasoningDelta.RootElement.GetProperty("params");
        Assert.Equal("reasoningContent", @params.GetProperty("deltaKind").GetString());
    }

    // -------------------------------------------------------------------------
    // Fix 4: streamingSupport=false suppresses delta notifications
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnStart_StreamingDisabled_DeltasAreSuppressed()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(streamingSupport: false);

        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var msg = harness.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } }
        });
        await harness.ExecuteRequestAsync(msg);

        // With streamingSupport=false, deltas are suppressed.
        // Expected: response, turn/started, item/started, item/completed, turn/completed (5, not 6)
        var all = await harness.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));

        var methods = all
            .Skip(1) // skip the response
            .Select(d => d.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null)
            .ToList();

        Assert.DoesNotContain(AppServerMethods.ItemAgentMessageDelta, methods);
        Assert.DoesNotContain(AppServerMethods.ItemReasoningDelta, methods);
        Assert.Contains(AppServerMethods.TurnCompleted, methods);
    }

    [Fact]
    public async Task TurnStart_LocalImageMetadata_IsAttachedToDataContent()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        var localImagePath = Path.Combine(_h.Identity.WorkspacePath, ".craft", "attachments", "images", "test.png");
        Directory.CreateDirectory(Path.GetDirectoryName(localImagePath)!);
        await File.WriteAllBytesAsync(localImagePath, [0x89, 0x50, 0x4E, 0x47]);

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[]
            {
                new
                {
                    type = "localImage",
                    path = localImagePath,
                    mimeType = "image/png",
                    fileName = "test.png"
                }
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);

        var dataContent = Assert.IsType<DataContent>(_h.Service.LastSubmittedContent.Single());
        Assert.NotNull(dataContent.AdditionalProperties);
        Assert.Equal(localImagePath, dataContent.AdditionalProperties!["localImage.path"]?.ToString());
        Assert.Equal("image/png", dataContent.AdditionalProperties!["localImage.mimeType"]?.ToString());
        Assert.Equal("test.png", dataContent.AdditionalProperties!["localImage.fileName"]?.ToString());
    }

    [Fact]
    public async Task TurnStart_ToolCallArgumentsDelta_EmitsNotificationWithExpectedShape()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(
            thread.Id,
            AppServerTestHarness.BuildStreamingToolCallEventSequence(thread.Id));

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Write a file" } }
        });
        await _h.ExecuteRequestAsync(msg);

        var all = await _h.Transport.WaitAndDrainAsync(7, TimeSpan.FromSeconds(10));
        AssertMethod(all[3], AppServerMethods.ItemToolCallArgumentsDelta);
        var @params = all[3].RootElement.GetProperty("params");
        Assert.Equal("toolCallArguments", @params.GetProperty("deltaKind").GetString());
        Assert.Equal("WriteFile", @params.GetProperty("toolName").GetString());
        Assert.Equal("call_001", @params.GetProperty("callId").GetString());
        Assert.True(@params.GetProperty("delta").GetString()?.Length > 0);
    }

    [Fact]
    public async Task TurnStart_ToolCallArgumentsDelta_StreamingDisabled_IsSuppressed()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(streamingSupport: false);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id,
            AppServerTestHarness.BuildStreamingToolCallEventSequence(thread.Id));

        var msg = harness.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Write a file" } }
        });
        await harness.ExecuteRequestAsync(msg);

        var all = await harness.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));
        var methods = all
            .Skip(1)
            .Select(d => d.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null)
            .ToList();
        Assert.DoesNotContain(AppServerMethods.ItemToolCallArgumentsDelta, methods);
    }

    [Fact]
    public async Task TurnStart_ToolCallArgumentsDelta_NotificationOptOut_IsSuppressed()
    {
        using var harness = new AppServerTestHarness();
        await harness.InitializeAsync(optOutMethods: [AppServerMethods.ItemToolCallArgumentsDelta]);
        var thread = await harness.Service.CreateThreadAsync(harness.Identity);
        harness.Service.EnqueueSubmitEvents(
            thread.Id,
            AppServerTestHarness.BuildStreamingToolCallEventSequence(thread.Id));

        var msg = harness.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Write a file" } }
        });
        await harness.ExecuteRequestAsync(msg);

        var all = await harness.Transport.WaitAndDrainAsync(5, TimeSpan.FromSeconds(10));
        var methods = all
            .Skip(1)
            .Select(d => d.RootElement.TryGetProperty("method", out var m) ? m.GetString() : null)
            .ToList();
        Assert.DoesNotContain(AppServerMethods.ItemToolCallArgumentsDelta, methods);
    }

    // -------------------------------------------------------------------------
    // Fix 5: messages field forwarded to SubmitInputAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnStart_WithMessages_DoesNotReturnError()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);
        _h.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

        // historyMode=client thread providing conversation history
        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[] { new { type = "text", text = "Hello" } },
            messages = new[]
            {
                new { role = "user", content = new[] { new { type = "text", text = "Previous message" } } }
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(response);
    }

    [Fact]
    public async Task TurnStart_BuiltInCommandRef_ReturnsInvalidParamsAndDoesNotSubmitInput()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new[]
            {
                new
                {
                    type = "commandRef",
                    name = "new",
                    rawText = "/new"
                }
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        Assert.Empty(_h.Service.LastSubmittedContent);
    }

    [Fact]
    public async Task TurnStart_MixedTextAndBuiltInCommandRef_ReturnsInvalidParamsAndDoesNotSubmitInput()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = new object[]
            {
                new { type = "text", text = "Please " },
                new
                {
                    type = "commandRef",
                    name = "help",
                    rawText = "/help"
                }
            }
        });
        await _h.ExecuteRequestAsync(msg);

        var response = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(response, AppServerErrors.InvalidParamsCode);
        Assert.Empty(_h.Service.LastSubmittedContent);
    }

    [Fact]
    public async Task TurnStart_CustomCommandRef_MaterializesExpandedPrompt()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"turn_start_custom_{Guid.NewGuid():N}");
        var workspaceCraftPath = Path.Combine(tempRoot, ".craft");

        try
        {
            Directory.CreateDirectory(Path.Combine(workspaceCraftPath, "commands"));
            await File.WriteAllTextAsync(
                Path.Combine(workspaceCraftPath, "commands", "code-review.md"),
                """
                ---
                description: Review changed files
                ---
                Review these files carefully: $ARGUMENTS
                """);

            using var harness = new AppServerTestHarness(workspaceCraftPath: workspaceCraftPath);
            await harness.InitializeAsync();

            var thread = await harness.Service.CreateThreadAsync(harness.Identity);
            harness.Service.EnqueueSubmitEvents(thread.Id, AppServerTestHarness.BuildTurnEventSequence(thread.Id));

            var msg = harness.BuildRequest(AppServerMethods.TurnStart, new
            {
                threadId = thread.Id,
                input = new[]
                {
                    new
                    {
                        type = "commandRef",
                        name = "code-review",
                        rawText = "/code-review src/foo.cs"
                    }
                }
            });
            await harness.ExecuteRequestAsync(msg);

            var response = await harness.Transport.ReadNextSentAsync();
            AppServerTestHarness.AssertIsSuccessResponse(response);

            var textContent = Assert.IsType<TextContent>(Assert.Single(harness.Service.LastSubmittedContent));
            Assert.Equal("Review these files carefully: src/foo.cs", textContent.Text);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    // -------------------------------------------------------------------------
    // turn/interrupt
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnInterrupt_CallsCancelTurnAsync()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        // Add a running turn to the thread so validation passes (Issue E fix)
        var runningTurn = new SessionTurn
        {
            Id = "turn_001",
            ThreadId = thread.Id,
            Status = TurnStatus.Running,
            StartedAt = DateTimeOffset.UtcNow
        };
        thread.Turns.Add(runningTurn);

        var msg = _h.BuildRequest(AppServerMethods.TurnInterrupt, new
        {
            threadId = thread.Id,
            turnId = "turn_001"
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsSuccessResponse(doc);

        Assert.Single(_h.Service.CancelledTurns);
        Assert.Equal(thread.Id, _h.Service.CancelledTurns[0].threadId);
        Assert.Equal("turn_001", _h.Service.CancelledTurns[0].turnId);
    }

    // -------------------------------------------------------------------------
    // turn/start — empty input validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TurnStart_EmptyInput_ReturnsInvalidParams()
    {
        var thread = await _h.Service.CreateThreadAsync(_h.Identity);

        var msg = _h.BuildRequest(AppServerMethods.TurnStart, new
        {
            threadId = thread.Id,
            input = Array.Empty<object>()
        });
        await _h.ExecuteRequestAsync(msg);

        var doc = await _h.Transport.ReadNextSentAsync();
        AppServerTestHarness.AssertIsErrorResponse(doc, AppServerErrors.InvalidParamsCode);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void AssertMethod(JsonDocument doc, string expectedMethod)
    {
        Assert.True(doc.RootElement.TryGetProperty("method", out var methodEl),
            $"Expected notification with method '{expectedMethod}' but got no 'method' property. " +
            $"Document: {doc.RootElement}");
        Assert.Equal(expectedMethod, methodEl.GetString());
    }
}
