using System.Text.Json;
using DotCraft.CLI.Rendering;
using DotCraft.Protocol;
using DotCraft.Protocol.AppServer;
using DotCraft.Tracing;

namespace DotCraft.CLI;

/// <summary>
/// Wire-protocol implementation of <see cref="ICliSession"/> that communicates with a
/// <c>dotcraft app-server</c> subprocess over JSON-RPC 2.0 via <see cref="AppServerWireClient"/>.
///
/// Thread/turn operations are mapped to the AppServer wire protocol methods defined in
/// <see cref="AppServerMethods"/>. The turn event stream is consumed via
/// <see cref="StreamAdapter.AdaptWireNotificationsAsync"/> and fed to <see cref="AgentRenderer"/>.
/// Approval requests are handled interactively via <see cref="WireApprovalHandler"/>.
/// </summary>
public sealed class WireCliSession(
    AppServerWireClient wire,
    TokenUsageStore? tokenUsageStore = null) : ICliSession
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // -------------------------------------------------------------------------
    // ICliSession – thread management
    // -------------------------------------------------------------------------

    public async Task<string> CreateThreadAsync(SessionIdentity identity, CancellationToken ct = default)
    {
        var result = await wire.SendRequestAsync(AppServerMethods.ThreadStart, new
        {
            identity = new
            {
                channelName = identity.ChannelName,
                userId = identity.UserId,
                channelContext = identity.ChannelContext,
                workspacePath = identity.WorkspacePath
            },
            historyMode = "server"
        }, ct: ct);

        return result.RootElement.GetProperty("result").GetProperty("thread").GetProperty("id").GetString()
            ?? throw new InvalidOperationException("AppServer returned a thread with no id.");
    }

    public async Task<SessionWireThread> ResumeThreadAsync(string threadId, CancellationToken ct = default)
    {
        // Resume sets the thread status to Active
        await wire.SendRequestAsync(AppServerMethods.ThreadResume, new { threadId }, ct: ct);

        // Fetch full thread including turns for history display
        var readResult = await wire.SendRequestAsync(AppServerMethods.ThreadRead, new
        {
            threadId,
            includeTurns = true
        }, ct: ct);

        var threadEl = readResult.RootElement.GetProperty("result").GetProperty("thread");
        return JsonSerializer.Deserialize<SessionWireThread>(threadEl.GetRawText(), ReadOptions)
            ?? throw new InvalidOperationException("AppServer returned null for thread/read.");
    }

    public async Task<IReadOnlyList<ThreadSummary>> FindThreadsAsync(SessionIdentity identity, CancellationToken ct = default)
    {
        var result = await wire.SendRequestAsync(AppServerMethods.ThreadList, new
        {
            identity = new
            {
                channelName = identity.ChannelName,
                userId = identity.UserId,
                channelContext = identity.ChannelContext,
                workspacePath = identity.WorkspacePath
            }
        }, ct: ct);

        var dataEl = result.RootElement.GetProperty("result").GetProperty("data");
        var summaries = JsonSerializer.Deserialize<List<ThreadSummary>>(dataEl.GetRawText(), ReadOptions)
            ?? [];
        return summaries;
    }

    public async Task ArchiveThreadAsync(string threadId, CancellationToken ct = default)
        => await wire.SendRequestAsync(AppServerMethods.ThreadArchive, new { threadId }, ct: ct);

    public async Task DeleteThreadAsync(string threadId, CancellationToken ct = default)
        => await wire.SendRequestAsync(AppServerMethods.ThreadDelete, new { threadId }, ct: ct);

    public async Task SetThreadModeAsync(string threadId, string mode, CancellationToken ct = default)
        => await wire.SendRequestAsync(AppServerMethods.ThreadModeSet, new { threadId, mode }, ct: ct);

    // -------------------------------------------------------------------------
    // ICliSession – turn execution
    // -------------------------------------------------------------------------

    /// <summary>
    /// Submits user input over the wire, streams turn notifications to <see cref="AgentRenderer"/>,
    /// and handles approval requests interactively.
    /// </summary>
    public async Task RunTurnAsync(string threadId, string input, CancellationToken ct = default)
    {
        using var renderer = new AgentRenderer(tokenTracker: null);
        await renderer.StartAsync(ct);
        await renderer.SendEventAsync(RenderEvent.StreamStart(), ct);

        // Route approval server-requests through the CLI approval UI
        wire.ServerRequestHandler = WireApprovalHandler.Create(() => renderer);

        try
        {
            // Start the turn and get the turn ID from the immediate response
            var startResult = await wire.SendRequestAsync(AppServerMethods.TurnStart, new
            {
                threadId,
                input = new[] { new { type = "text", text = input } }
            }, timeout: TimeSpan.FromSeconds(30), ct: ct);

            var turnId = startResult.RootElement
                .GetProperty("result").GetProperty("turn").GetProperty("id").GetString();

            if (string.IsNullOrEmpty(turnId))
                throw new InvalidOperationException("AppServer returned a turn with no id.");

            // Stream notifications and adapt to RenderEvents
            var notifications = wire.ReadTurnNotificationsAsync(ct: ct);

            await foreach (var evt in StreamAdapter.AdaptWireNotificationsAsync(notifications, ct))
            {
                // Record token usage when the turn completes (content is "inputTokens,outputTokens,totalTokens")
                if (evt.Type == RenderEventType.Usage && tokenUsageStore != null)
                {
                    var parts = evt.Content.Split(',');
                    if (parts.Length >= 2 &&
                        long.TryParse(parts[0], out var inputTokens) &&
                        long.TryParse(parts[1], out var outputTokens))
                    {
                        tokenUsageStore.Record(new TokenUsageRecord
                        {
                            Channel = "cli",
                            UserId = "local",
                            DisplayName = "CLI",
                            InputTokens = inputTokens,
                            OutputTokens = outputTokens
                        });
                    }
                }

                await renderer.SendEventAsync(evt, ct);
            }
        }
        finally
        {
            // Clear the approval handler so it does not leak across turns
            wire.ServerRequestHandler = null;
        }

        await renderer.StopAsync();
    }

    // -------------------------------------------------------------------------
    // IAsyncDisposable
    // -------------------------------------------------------------------------

    public ValueTask DisposeAsync() => wire.DisposeAsync();
}
