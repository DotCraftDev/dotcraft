using System.Text.Json;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Consumes a <see cref="SessionEvent"/> stream from <see cref="ISessionService.SubmitInputAsync"/>
/// or <see cref="ISessionService.SubscribeThreadAsync"/> and fans each event out as a JSON-RPC
/// notification to the connected client.
///
/// The approval flow is handled inline: when an <see cref="SessionEventType.ApprovalRequested"/>
/// event arrives, the dispatcher sends an <c>item/approval/request</c> JSON-RPC request to the
/// client, awaits the response, then calls <see cref="ISessionService.ResolveApprovalAsync"/>.
/// </summary>
public sealed class AppServerEventDispatcher
{
    private readonly IAsyncEnumerable<SessionEvent> _events;
    private readonly AppServerConnection _connection;
    private readonly IAppServerTransport _transport;
    private readonly ISessionService _sessionService;

    /// <summary>
    /// Decision to apply when the client declared <c>approvalSupport = false</c>.
    /// Defaults to <see cref="SessionApprovalDecision.Reject"/> if not specified.
    /// Should be sourced from the workspace's default approval policy.
    /// </summary>
    private readonly SessionApprovalDecision _defaultApprovalDecision;

    /// <summary>
    /// Called when the first <see cref="SessionEventType.TurnStarted"/> event arrives.
    /// The dispatcher awaits this delegate before sending the <c>turn/started</c> notification,
    /// so the caller can send the <c>turn/start</c> response first and guarantee correct ordering.
    /// </summary>
    private readonly Func<SessionWireTurn, Task>? _onTurnStarted;

    /// <param name="events">Event stream to consume.</param>
    /// <param name="connection">Connection state for opt-out filtering and capability checks.</param>
    /// <param name="transport">Transport for sending notifications and approval requests.</param>
    /// <param name="sessionService">Session service for resolving approvals.</param>
    /// <param name="onTurnStarted">
    /// Optional async callback invoked (and awaited) when the first <c>TurnStarted</c> event
    /// is received. The dispatcher waits for this to complete before sending the notification,
    /// ensuring the <c>turn/start</c> response reaches the client before <c>turn/started</c>.
    /// </param>
    /// <param name="defaultApprovalDecision">
    /// The decision to apply when the client declared <c>approvalSupport = false</c>.
    /// Per spec Section 7.4, the workspace's default approval policy applies in that case.
    /// Defaults to <see cref="SessionApprovalDecision.Reject"/>.
    /// </param>
    public AppServerEventDispatcher(
        IAsyncEnumerable<SessionEvent> events,
        AppServerConnection connection,
        IAppServerTransport transport,
        ISessionService sessionService,
        Func<SessionWireTurn, Task>? onTurnStarted = null,
        SessionApprovalDecision defaultApprovalDecision = SessionApprovalDecision.Reject)
    {
        _events = events;
        _connection = connection;
        _transport = transport;
        _sessionService = sessionService;
        _onTurnStarted = onTurnStarted;
        _defaultApprovalDecision = defaultApprovalDecision;
    }

    /// <summary>
    /// Starts iterating the event stream and dispatches events as JSON-RPC notifications.
    /// Runs until the stream completes or the cancellation token is signalled.
    /// </summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        await foreach (var evt in _events.WithCancellation(ct))
        {
            await DispatchEventAsync(evt, ct);
        }
    }

    // -------------------------------------------------------------------------
    // Event dispatch
    // -------------------------------------------------------------------------

    private async Task DispatchEventAsync(SessionEvent evt, CancellationToken ct)
    {
        // Fix 7: Do not send any server-initiated notifications until the client has
        // sent the `initialized` notification signalling readiness. The TurnStarted
        // case is exempt because it also signals the turn/start response callback.
        var method = evt.ToWireMethodName();

        switch (evt.EventType)
        {
            case SessionEventType.TurnStarted:
                // Give the turn/start handler a chance to send its response first,
                // guaranteeing the response arrives before the notification.
                if (_onTurnStarted != null && evt.TurnPayload is { } turn)
                    await _onTurnStarted(turn.ToWire(includeItems: false) with { Items = [] });

                // Only send the turn/started notification when client is ready
                if (_connection.IsClientReady && _connection.ShouldSendNotification(method))
                    await SendNotificationAsync(method, BuildParams(evt), ct);
                break;

            case SessionEventType.ApprovalRequested:
                await HandleApprovalRequestedAsync(evt, ct);
                break;

            case SessionEventType.ItemDelta:
                // Fix 4: Suppress delta notifications when client declared streamingSupport = false.
                // Also skip empty deltas — the LLM emits empty string chunks at stream boundaries.
                if (_connection.IsClientReady
                    && _connection.SupportsStreaming
                    && !IsEmptyDelta(evt)
                    && _connection.ShouldSendNotification(method))
                    await SendNotificationAsync(method, BuildParams(evt), ct);
                break;

            default:
                if (_connection.IsClientReady && _connection.ShouldSendNotification(method))
                    await SendNotificationAsync(method, BuildParams(evt), ct);
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Notification params builder
    //
    // Each notification method has a specific params shape per the wire spec.
    // This method maps SessionEvent → the correct params object.
    // -------------------------------------------------------------------------

    private static object? BuildParams(SessionEvent evt) => evt.EventType switch
    {
        // Thread notifications (spec Section 6.1)
        SessionEventType.ThreadCreated => new
        {
            thread = evt.ThreadPayload?.ToWire()
        },
        SessionEventType.ThreadResumed => new
        {
            thread = evt.ThreadPayload?.ToWire(),
            resumedBy = evt.ResumedPayload?.ResumedBy
        },
        SessionEventType.ThreadStatusChanged => new
        {
            threadId = evt.ThreadId,
            previousStatus = evt.StatusChangedPayload?.PreviousStatus,
            newStatus = evt.StatusChangedPayload?.NewStatus
        },

        // Turn notifications (spec Section 6.2)
        SessionEventType.TurnStarted => new
        {
            turn = evt.TurnPayload?.ToWire(includeItems: true)
        },
        SessionEventType.TurnCompleted => new
        {
            turn = evt.TurnPayload?.ToWire(includeItems: true)
        },
        SessionEventType.TurnFailed => new
        {
            turn = evt.TurnPayload?.ToWire(includeItems: true),
            error = evt.TurnFailedPayload?.Error
        },
        SessionEventType.TurnCancelled => new
        {
            turn = evt.TurnPayload?.ToWire(includeItems: true),
            reason = evt.TurnCancelledPayload?.Reason
        },

        // Item notifications (spec Section 6.3)
        SessionEventType.ItemStarted => new
        {
            threadId = evt.ThreadId,
            turnId = evt.TurnId,
            item = evt.ItemPayload?.ToWire()
        },
        // Fix 1: Include deltaKind so clients can distinguish agentMessage from reasoningContent
        // without inspecting surrounding state (spec Section 2.3).
        SessionEventType.ItemDelta when evt.DeltaPayload is { } delta => new
        {
            threadId = evt.ThreadId,
            turnId = evt.TurnId,
            itemId = evt.ItemId,
            deltaKind = delta.DeltaKind,
            delta = delta.TextDelta
        },
        SessionEventType.ItemDelta when evt.ReasoningDeltaPayload is { } reasoning => new
        {
            threadId = evt.ThreadId,
            turnId = evt.TurnId,
            itemId = evt.ItemId,
            deltaKind = reasoning.DeltaKind,
            delta = reasoning.TextDelta
        },
        SessionEventType.ItemCompleted => new
        {
            threadId = evt.ThreadId,
            turnId = evt.TurnId,
            item = evt.ItemPayload?.ToWire()
        },

        // Approval resolved notification (spec Section 6.4)
        SessionEventType.ApprovalResolved => new
        {
            threadId = evt.ThreadId,
            turnId = evt.TurnId,
            item = evt.ItemPayload?.ToWire()
        },

        _ => null
    };

    // -------------------------------------------------------------------------
    // Approval flow (spec Section 7)
    // -------------------------------------------------------------------------

    private async Task HandleApprovalRequestedAsync(SessionEvent evt, CancellationToken ct)
    {
        var item = evt.ItemPayload;
        if (item?.Payload is not ApprovalRequestPayload req)
            return;

        if (!_connection.SupportsApproval)
        {
            // Fix 6: Apply workspace default policy instead of hard-coding Reject.
            // Per spec Section 7.4: "the server applies the workspace's default approval policy".
            await TryResolveApprovalAsync(evt, req.RequestId, _defaultApprovalDecision, ct);
            return;
        }

        var approvalParams = new AppServerApprovalRequestParams
        {
            ThreadId = evt.ThreadId,
            TurnId = evt.TurnId ?? string.Empty,
            ItemId = evt.ItemId ?? string.Empty,
            RequestId = req.RequestId,
            ApprovalType = req.ApprovalType,
            Operation = req.Operation,
            Target = req.Target,
            ScopeKey = req.ScopeKey,
            Reason = req.Reason
        };

        AppServerIncomingMessage response;
        try
        {
            response = await _transport.SendClientRequestAsync(
                AppServerMethods.ItemApprovalRequest,
                approvalParams,
                ct,
                timeout: TimeSpan.FromSeconds(120));
        }
        catch (OperationCanceledException)
        {
            // Timeout or disconnect: apply default policy so the agent can continue or fail gracefully
            await TryResolveApprovalAsync(evt, req.RequestId, _defaultApprovalDecision, ct);
            return;
        }

        var decision = ParseApprovalDecision(response);
        await TryResolveApprovalAsync(evt, req.RequestId, decision, ct);
    }

    private async Task TryResolveApprovalAsync(
        SessionEvent evt,
        string requestId,
        SessionApprovalDecision decision,
        CancellationToken ct)
    {
        if (evt.TurnId == null)
            return;

        try
        {
            await _sessionService.ResolveApprovalAsync(evt.ThreadId, evt.TurnId, requestId, decision, ct);
        }
        catch (OperationCanceledException) { /* Ignore if session was cancelled */ }
    }

    private static SessionApprovalDecision ParseApprovalDecision(AppServerIncomingMessage response)
    {
        if (!response.Result.HasValue)
            return SessionApprovalDecision.Reject;

        try
        {
            var result = JsonSerializer.Deserialize<AppServerApprovalResponseResult>(
                response.Result.Value.GetRawText(),
                SessionWireJsonOptions.Default);

            return result?.Decision switch
            {
                "accept" => SessionApprovalDecision.AcceptOnce,
                "acceptForSession" => SessionApprovalDecision.AcceptForSession,
                "acceptAlways" => SessionApprovalDecision.AcceptAlways,
                "decline" => SessionApprovalDecision.Reject,
                "cancel" => SessionApprovalDecision.CancelTurn,
                _ => SessionApprovalDecision.Reject
            };
        }
        catch
        {
            return SessionApprovalDecision.Reject;
        }
    }

    // -------------------------------------------------------------------------
    // Transport helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns true when an ItemDelta event carries an empty text chunk.
    /// The LLM streaming API emits empty strings at stream open/close boundaries;
    /// these carry no information and should not be forwarded to the client.
    /// </summary>
    private static bool IsEmptyDelta(SessionEvent evt) =>
        evt.EventType == SessionEventType.ItemDelta &&
        (evt.DeltaPayload is { } d ? string.IsNullOrEmpty(d.TextDelta)
            : evt.ReasoningDeltaPayload is { } r && string.IsNullOrEmpty(r.TextDelta));

    private Task SendNotificationAsync(string method, object? @params, CancellationToken ct)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method,
            @params
        };
        return _transport.WriteMessageAsync(notification, ct);
    }
}
