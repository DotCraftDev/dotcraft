using System.Text.Json;
using DotCraft.Abstractions;
using DotCraft.Cron;
using Microsoft.Extensions.AI;

namespace DotCraft.Protocol.AppServer;

/// <summary>
/// Dispatches incoming JSON-RPC requests to the appropriate <see cref="ISessionService"/>
/// method and returns a JSON-RPC response object ready for serialization.
///
/// Each public Handle* method maps directly to one of the wire protocol methods
/// defined in the Session Wire Protocol Specification.
/// </summary>
public sealed class AppServerRequestHandler
{
    private readonly ISessionService _sessionService;
    private readonly AppServerConnection _connection;
    private readonly IAppServerTransport _transport;
    private readonly string _serverVersion;
    private readonly CronService? _cronService;

    /// <summary>
    /// Decision applied by <see cref="AppServerEventDispatcher"/> when the client declares
    /// <c>approvalSupport = false</c>. Sourced from the workspace's default approval policy.
    /// </summary>
    private readonly SessionApprovalDecision _defaultApprovalDecision;

    public AppServerRequestHandler(
        ISessionService sessionService,
        AppServerConnection connection,
        IAppServerTransport transport,
        string serverVersion = "0.1.0",
        SessionApprovalDecision defaultApprovalDecision = SessionApprovalDecision.AcceptOnce,
        CronService? cronService = null)
    {
        _sessionService = sessionService;
        _connection = connection;
        _transport = transport;
        _serverVersion = serverVersion;
        _defaultApprovalDecision = defaultApprovalDecision;
        _cronService = cronService;
    }

    // -------------------------------------------------------------------------
    // Main dispatch
    // -------------------------------------------------------------------------

    /// <summary>
    /// Dispatches an incoming request to the appropriate handler.
    /// Returns the JSON-RPC response to send to the client.
    /// Throws <see cref="AppServerException"/> for protocol errors.
    /// Domain exceptions from <see cref="ISessionService"/> are translated to spec-defined
    /// error codes (Section 8.3): -32010 ThreadNotFound, -32011 ThreadNotActive, -32012 TurnInProgress.
    /// </summary>
    public async Task<object?> HandleRequestAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var method = msg.Method ?? string.Empty;

        // initialize is the only method allowed before the handshake
        if (method != AppServerMethods.Initialize && !_connection.IsInitialized)
            throw AppServerErrors.NotInitialized();

        // After initialize response, block all requests until the client sends the
        // `initialized` notification (IsClientReady). This prevents premature operations
        // before the client has finished processing server capabilities.
        if (method != AppServerMethods.Initialize && _connection.IsInitialized && !_connection.IsClientReady)
            throw AppServerErrors.InvalidRequest("Server is awaiting the 'initialized' notification before handling requests.");

        try
        {
            // Route to the appropriate handler
            return await (method switch
            {
                AppServerMethods.Initialize => HandleInitializeAsync(msg, ct),
                AppServerMethods.ThreadStart => HandleThreadStartAsync(msg, ct),
                AppServerMethods.ThreadResume => HandleThreadResumeAsync(msg, ct),
                AppServerMethods.ThreadList => HandleThreadListAsync(msg, ct),
                AppServerMethods.ThreadRead => HandleThreadReadAsync(msg, ct),
                AppServerMethods.ThreadSubscribe => HandleThreadSubscribeAsync(msg, ct),
                AppServerMethods.ThreadUnsubscribe => HandleThreadUnsubscribeAsync(msg, ct),
                AppServerMethods.ThreadPause => HandleThreadPauseAsync(msg, ct),
                AppServerMethods.ThreadArchive => HandleThreadArchiveAsync(msg, ct),
                AppServerMethods.ThreadDelete => HandleThreadDeleteAsync(msg, ct),
                AppServerMethods.ThreadModeSet => HandleThreadModeSetAsync(msg, ct),
                AppServerMethods.ThreadConfigUpdate => HandleThreadConfigUpdateAsync(msg, ct),
                AppServerMethods.TurnStart => HandleTurnStartAsync(msg, ct),
                AppServerMethods.TurnInterrupt => HandleTurnInterruptAsync(msg, ct),
                AppServerMethods.CronList => HandleCronListAsync(msg, ct),
                AppServerMethods.CronRemove => HandleCronRemoveAsync(msg, ct),
                AppServerMethods.CronEnable => HandleCronEnableAsync(msg, ct),
                _ => throw AppServerErrors.MethodNotFound(method)
            });
        }
        catch (AppServerException)
        {
            throw;
        }
        catch (KeyNotFoundException ex)
        {
            // Thread or turn not found in persistence or in-memory state
            throw AppServerErrors.ThreadNotFound(ExtractQuotedId(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            throw MapOperationException(ex);
        }
    }

    /// <summary>
    /// Handles the <c>initialized</c> client notification (no response required).
    /// </summary>
    public void HandleInitializedNotification()
    {
        _connection.MarkClientReady();
    }

    // -------------------------------------------------------------------------
    // initialize (spec Section 3.2)
    // -------------------------------------------------------------------------

    private Task<object?> HandleInitializeAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<AppServerInitializeParams>(msg);
        if (!_connection.TryMarkInitialized(p.ClientInfo, p.Capabilities))
            throw AppServerErrors.AlreadyInitialized();

        var result = new AppServerInitializeResult
        {
            ServerInfo = new AppServerServerInfo
            {
                Name = "dotcraft",
                Version = _serverVersion,
                ProtocolVersion = "1"
            },
            Capabilities = new AppServerServerCapabilities
            {
                ThreadManagement = true,
                ThreadSubscriptions = true,
                ApprovalFlow = true,
                ModeSwitch = true,
                ConfigOverride = true,
                CronManagement = _cronService != null
            }
        };

        return Task.FromResult<object?>(result);
    }

    // -------------------------------------------------------------------------
    // thread/* methods (spec Section 4)
    // -------------------------------------------------------------------------

    private async Task<object?> HandleThreadStartAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadStartParams>(msg);

        var historyMode = p.HistoryMode?.ToLowerInvariant() == "client"
            ? HistoryMode.Client
            : HistoryMode.Server;

        var thread = await _sessionService.CreateThreadAsync(
            p.Identity,
            p.Config,
            historyMode,
            displayName: p.DisplayName,
            ct: ct);

        // Fix 8: The host sends the thread/start response first, then emits the
        // thread/started notification as required by spec Section 4.1.
        _ = SendNotificationAfterResponseAsync(
            msg.Id,
            new { thread = thread.ToWire() },
            AppServerMethods.ThreadStarted,
            new { thread = thread.ToWire() },
            ct);

        // Return null to signal the response will be sent inline by SendNotificationAfterResponseAsync
        return null;
    }

    private async Task<object?> HandleThreadResumeAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadResumeParams>(msg);
        var thread = await _sessionService.ResumeThreadAsync(p.ThreadId, ct);

        // Gap D: use the client's declared name from initialize instead of hardcoded "appserver".
        var resumedBy = _connection.ClientInfo?.Name ?? "appserver";
        var responseResult = new { thread = thread.ToWire() };
        var notifParams = new { thread = thread.ToWire(), resumedBy };

        if (_connection.HasSubscription(p.ThreadId))
        {
            // Gap C: connection has a passive subscription — the broker/dispatcher path will
            // emit thread/resumed. Send only the response to avoid duplicating the notification.
            await _transport.WriteMessageAsync(BuildResponse(msg.Id, responseResult), ct);
            return null;
        }

        // No subscription: send response then notification inline.
        _ = SendNotificationAfterResponseAsync(msg.Id, responseResult, AppServerMethods.ThreadResumed, notifParams, ct);
        return null;
    }

    private async Task<object?> HandleThreadListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadListParams>(msg);
        var threads = await _sessionService.FindThreadsAsync(
            p.Identity,
            p.IncludeArchived ?? false,
            ct);

        return new ThreadListResult { Data = [.. threads] };
    }

    private async Task<object?> HandleThreadReadAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadReadParams>(msg);
        var thread = await _sessionService.GetThreadAsync(p.ThreadId, ct);
        var includeTurns = p.IncludeTurns ?? false;
        return new { thread = thread.ToWire(includeTurns) };
    }

    private Task<object?> HandleThreadSubscribeAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadSubscribeParams>(msg);

        // Start a background subscription that fans out thread events to this connection
        var subCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!_connection.TryAddSubscription(p.ThreadId, subCts))
        {
            // Already subscribed — idempotent, just return success
            return Task.FromResult<object?>(new { });
        }

        var events = _sessionService.SubscribeThreadAsync(
            p.ThreadId,
            p.ReplayRecent ?? false,
            subCts.Token);

        var dispatcher = new AppServerEventDispatcher(
            events, _connection, _transport, _sessionService,
            defaultApprovalDecision: _defaultApprovalDecision);
        _ = dispatcher.RunAsync(subCts.Token)
            .ContinueWith(t =>
            {
                _connection.TryCancelSubscription(p.ThreadId);
                if (t.IsFaulted)
                    _ = Console.Error.WriteLineAsync(
                        $"[AppServer] Subscription error for thread {p.ThreadId}: {t.Exception?.GetBaseException().Message}");
            }, TaskContinuationOptions.ExecuteSynchronously);

        return Task.FromResult<object?>(new { });
    }

    private Task<object?> HandleThreadUnsubscribeAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadUnsubscribeParams>(msg);
        _connection.TryCancelSubscription(p.ThreadId);
        return Task.FromResult<object?>(new { });
    }

    private async Task<object?> HandleThreadPauseAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadPauseParams>(msg);

        // Gap B: capture previousStatus before the operation so the notification is accurate.
        var thread = await _sessionService.GetThreadAsync(p.ThreadId, ct);
        var previousStatus = thread.Status;

        await _sessionService.PauseThreadAsync(p.ThreadId, ct);

        // Idempotent: if the thread was already paused, no status change occurred.
        if (previousStatus == ThreadStatus.Paused)
            return new { };

        if (_connection.HasSubscription(p.ThreadId))
        {
            // Gap C: subscription exists — broker/dispatcher will emit thread/statusChanged.
            await _transport.WriteMessageAsync(BuildResponse(msg.Id, new { }), ct);
            return null;
        }

        _ = SendNotificationAfterResponseAsync(
            msg.Id, new { },
            AppServerMethods.ThreadStatusChanged,
            new { threadId = p.ThreadId, previousStatus, newStatus = ThreadStatus.Paused },
            ct);
        return null;
    }

    private async Task<object?> HandleThreadArchiveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadArchiveParams>(msg);

        // Gap B: capture previousStatus before the operation so the notification is accurate.
        var thread = await _sessionService.GetThreadAsync(p.ThreadId, ct);
        var previousStatus = thread.Status;

        await _sessionService.ArchiveThreadAsync(p.ThreadId, ct);

        // Idempotent: if the thread was already archived, no status change occurred.
        if (previousStatus == ThreadStatus.Archived)
            return new { };

        if (_connection.HasSubscription(p.ThreadId))
        {
            // Gap C: subscription exists — broker/dispatcher will emit thread/statusChanged.
            await _transport.WriteMessageAsync(BuildResponse(msg.Id, new { }), ct);
            return null;
        }

        _ = SendNotificationAfterResponseAsync(
            msg.Id, new { },
            AppServerMethods.ThreadStatusChanged,
            new { threadId = p.ThreadId, previousStatus, newStatus = ThreadStatus.Archived },
            ct);
        return null;
    }

    private async Task<object?> HandleThreadDeleteAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadDeleteParams>(msg);
        await _sessionService.DeleteThreadPermanentlyAsync(p.ThreadId, ct);
        return new { };
    }

    private async Task<object?> HandleThreadModeSetAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadModeSetParams>(msg);
        await _sessionService.SetThreadModeAsync(p.ThreadId, p.Mode, ct);
        return new { };
    }

    private async Task<object?> HandleThreadConfigUpdateAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<ThreadConfigUpdateParams>(msg);
        await _sessionService.UpdateThreadConfigurationAsync(p.ThreadId, p.Config, ct);
        return new { };
    }

    // -------------------------------------------------------------------------
    // turn/* methods (spec Section 5)
    // -------------------------------------------------------------------------

    private async Task<object?> HandleTurnStartAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<TurnStartParams>(msg);

        if (p.Input.Count == 0)
            throw AppServerErrors.InvalidParams("'input' must contain at least one part.");

        var content = await ResolveInputPartsAsync(p.Input, ct);

        // Set ChannelSessionScope for non-adapter clients (e.g. CLI) so that tools like
        // CronTools can capture the delivery context (channel="cli") at job creation time.
        // External channel adapters manage their own scope via SenderContext.
        ChannelSessionInfo? channelScopeInfo = null;
        if (!_connection.IsChannelAdapter)
        {
            channelScopeInfo = new ChannelSessionInfo
            {
                Channel = "cli",
                UserId = _connection.ClientInfo?.Name ?? "anonymous"
            };
        }

        // Fix 5: Deserialize client-provided history for historyMode=client threads.
        ChatMessage[]? messages = null;
        if (p.Messages.HasValue && p.Messages.Value.ValueKind != JsonValueKind.Null)
        {
            try
            {
                messages = JsonSerializer.Deserialize<ChatMessage[]>(
                    p.Messages.Value.GetRawText(),
                    SessionWireJsonOptions.Default);
            }
            catch (JsonException ex)
            {
                throw AppServerErrors.InvalidParams($"Failed to deserialize 'messages': {ex.Message}");
            }
        }

        // TCS to receive the initial turn from the event dispatcher
        var initialTurnTcs = new TaskCompletionSource<SessionWireTurn>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        // onTurnStarted: send the turn/start response, then signal the dispatcher to
        // proceed with the turn/started notification — guaranteeing correct ordering.
        async Task OnTurnStarted(SessionWireTurn initialTurn)
        {
            // Build and send the turn/start JSON-RPC response before the notification
            var responsePayload = new { turn = initialTurn };
            await _transport.WriteMessageAsync(BuildResponse(msg.Id, responsePayload), ct);
            // Signal that the response was sent successfully
            initialTurnTcs.TrySetResult(initialTurn);
        }

        using var channelScope = channelScopeInfo != null ? ChannelSessionScope.Set(channelScopeInfo) : null;

        var events = _sessionService.SubmitInputAsync(p.ThreadId, content, p.Sender, messages, ct);
        var dispatcher = new AppServerEventDispatcher(
            events, _connection, _transport, _sessionService, OnTurnStarted,
            defaultApprovalDecision: _defaultApprovalDecision);

        var dispatchTask = dispatcher.RunAsync(ct);

        // Propagate dispatch failures to the TCS so we don't hang indefinitely
        _ = dispatchTask.ContinueWith(t =>
        {
            if (t.IsFaulted)
                initialTurnTcs.TrySetException(t.Exception!.GetBaseException());
            else if (t.IsCanceled)
                initialTurnTcs.TrySetCanceled(ct);
            else
                initialTurnTcs.TrySetException(new AppServerException(
                    AppServerErrors.InternalErrorCode,
                    "Event dispatch completed without emitting a TurnStarted event."));
        }, TaskContinuationOptions.ExecuteSynchronously);

        // Wait until the response has been sent (or dispatch failed)
        await initialTurnTcs.Task;

        // Return null to signal the host that the response has already been sent inline
        return null;
    }

    private async Task<object?> HandleTurnInterruptAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        var p = GetParams<TurnInterruptParams>(msg);

        // Issue E: validate thread and turn existence/status before cancelling.
        // GetThreadAsync throws KeyNotFoundException → mapped to -32010 by the outer catch.
        var thread = await _sessionService.GetThreadAsync(p.ThreadId, ct);

        var turn = thread.Turns.FirstOrDefault(t => t.Id == p.TurnId);
        if (turn == null)
            throw AppServerErrors.TurnNotFound(p.TurnId);

        if (turn.Status != TurnStatus.Running && turn.Status != TurnStatus.WaitingApproval)
            throw AppServerErrors.TurnNotRunning(p.TurnId);

        await _sessionService.CancelTurnAsync(p.ThreadId, p.TurnId, ct);
        return new { };
    }

    // -------------------------------------------------------------------------
    // cron/* methods (spec Section 16)
    // -------------------------------------------------------------------------

    private Task<object?> HandleCronListAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (_cronService == null) throw AppServerErrors.MethodNotFound(AppServerMethods.CronList);
        var p = GetParams<CronListParams>(msg);
        var jobs = _cronService.ListJobs(includeDisabled: p.IncludeDisabled);
        return Task.FromResult<object?>(new CronListResult
        {
            Jobs = jobs.Select(MapCronJob).ToList()
        });
    }

    private Task<object?> HandleCronRemoveAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (_cronService == null) throw AppServerErrors.MethodNotFound(AppServerMethods.CronRemove);
        var p = GetParams<CronRemoveParams>(msg);
        if (string.IsNullOrWhiteSpace(p.JobId))
            throw AppServerErrors.InvalidParams("'jobId' is required.");
        var removed = _cronService.RemoveJob(p.JobId);
        if (!removed) throw AppServerErrors.CronJobNotFound(p.JobId);
        return Task.FromResult<object?>(new CronRemoveResult { Removed = true });
    }

    private Task<object?> HandleCronEnableAsync(AppServerIncomingMessage msg, CancellationToken ct)
    {
        if (_cronService == null) throw AppServerErrors.MethodNotFound(AppServerMethods.CronEnable);
        var p = GetParams<CronEnableParams>(msg);
        if (string.IsNullOrWhiteSpace(p.JobId))
            throw AppServerErrors.InvalidParams("'jobId' is required.");
        var job = _cronService.EnableJob(p.JobId, p.Enabled);
        if (job == null) throw AppServerErrors.CronJobNotFound(p.JobId);
        return Task.FromResult<object?>(new CronEnableResult { Job = MapCronJob(job) });
    }

    private static CronJobWireInfo MapCronJob(CronJob job) => new()
    {
        Id = job.Id,
        Name = job.Name,
        Schedule = new CronScheduleWireInfo
        {
            Kind = job.Schedule.Kind,
            EveryMs = job.Schedule.EveryMs,
            AtMs = job.Schedule.AtMs
        },
        Enabled = job.Enabled,
        CreatedAtMs = job.CreatedAtMs,
        DeleteAfterRun = job.DeleteAfterRun,
        State = new CronJobStateWireInfo
        {
            NextRunAtMs = job.State.NextRunAtMs,
            LastRunAtMs = job.State.LastRunAtMs,
            LastStatus = job.State.LastStatus,
            LastError = job.State.LastError
        }
    };

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    // Fix 10: Shared HttpClient for image URL fetch (best-effort, text-only is the primary path).
    private static readonly HttpClient ImageHttpClient = new();

    /// <summary>
    /// Converts wire input parts to <see cref="AIContent"/>, resolving image and localImage parts
    /// to <see cref="DataContent"/> by reading file bytes or fetching URL bytes.
    /// Falls back to a text placeholder on any I/O error so the turn is not blocked.
    /// </summary>
    private static async Task<List<AIContent>> ResolveInputPartsAsync(
        List<SessionWireInputPart> parts,
        CancellationToken ct)
    {
        var result = new List<AIContent>(parts.Count);
        foreach (var part in parts)
        {
            AIContent content;
            switch (part.Type)
            {
                case "localImage" when part.Path is { } path:
                    content = await ResolveLocalImageAsync(path, ct);
                    break;
                case "image" when part.Url is { } url:
                    content = await ResolveRemoteImageAsync(url, ct);
                    break;
                default:
                    content = part.ToAIContent();
                    break;
            }
            result.Add(content);
        }
        return result;
    }

    private static async Task<AIContent> ResolveLocalImageAsync(string path, CancellationToken ct)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, ct);
            var mediaType = InferMediaType(path);
            return new DataContent(bytes, mediaType);
        }
        catch
        {
            // Best-effort: return placeholder if file cannot be read
            return new TextContent($"[localImage:{path}]");
        }
    }

    private static async Task<AIContent> ResolveRemoteImageAsync(string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var response = await ImageHttpClient.GetAsync(url, cts.Token);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(cts.Token);
            var mediaType = response.Content.Headers.ContentType?.MediaType
                ?? InferMediaType(url);
            return new DataContent(bytes, mediaType);
        }
        catch
        {
            // Best-effort: return placeholder if URL cannot be fetched
            return new TextContent($"[image:{url}]");
        }
    }

    private static string InferMediaType(string pathOrUrl)
    {
        var ext = Path.GetExtension(pathOrUrl).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
    }

    // -------------------------------------------------------------------------
    // Domain exception → wire error code translation (Gap A, spec Section 8.3)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Translates an <see cref="InvalidOperationException"/> from the domain layer into the
    /// appropriate <see cref="AppServerException"/> with a spec-defined error code.
    /// </summary>
    private static AppServerException MapOperationException(InvalidOperationException ex)
    {
        var msg = ex.Message;
        var id = ExtractQuotedId(msg);

        if (msg.Contains("archived and cannot be resumed") || msg.Contains("is not Active"))
            return AppServerErrors.ThreadNotActive(id);

        if (msg.Contains("already has a running Turn"))
            return AppServerErrors.TurnInProgress(id);

        // historyMode contract violations are caller errors → InvalidParams (-32602)
        if (msg.Contains("client-managed history") || msg.Contains("server-managed history"))
            return AppServerErrors.InvalidParams(msg);

        return AppServerErrors.InternalError(msg);
    }

    /// <summary>
    /// Extracts the first single-quoted identifier from an exception message.
    /// For example: "Thread 'thread_001' not found." → "thread_001".
    /// </summary>
    private static string ExtractQuotedId(string message)
    {
        var start = message.IndexOf('\'');
        if (start < 0) return string.Empty;
        var end = message.IndexOf('\'', start + 1);
        return end > start ? message[(start + 1)..end] : string.Empty;
    }

    private static T GetParams<T>(AppServerIncomingMessage msg) where T : new()
    {
        if (!msg.Params.HasValue || msg.Params.Value.ValueKind == JsonValueKind.Null)
            return new T();

        try
        {
            return JsonSerializer.Deserialize<T>(
                msg.Params.Value.GetRawText(),
                SessionWireJsonOptions.Default) ?? new T();
        }
        catch (JsonException ex)
        {
            throw AppServerErrors.InvalidParams($"Failed to deserialize params: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a JSON-RPC response followed immediately by a notification on the same connection.
    /// Used by thread lifecycle handlers (Fix 8) to guarantee response-before-notification ordering.
    /// The caller must return null from its handle method to signal the host that the response
    /// has already been sent.
    /// </summary>
    private async Task SendNotificationAfterResponseAsync(
        JsonElement? requestId,
        object responseResult,
        string notificationMethod,
        object notificationParams,
        CancellationToken ct)
    {
        await _transport.WriteMessageAsync(BuildResponse(requestId, responseResult), ct);
        await _transport.WriteMessageAsync(new
        {
            jsonrpc = "2.0",
            method = notificationMethod,
            @params = notificationParams
        }, ct);
    }

    /// <summary>
    /// Builds a standard JSON-RPC 2.0 success response.
    /// </summary>
    public static object BuildResponse(JsonElement? id, object? result) => new
    {
        jsonrpc = "2.0",
        id,
        result
    };

    /// <summary>
    /// Builds a standard JSON-RPC 2.0 error response.
    /// </summary>
    public static object BuildErrorResponse(JsonElement? id, AppServerError error) => new
    {
        jsonrpc = "2.0",
        id,
        error
    };
}
