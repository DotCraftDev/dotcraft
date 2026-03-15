using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using DotCraft.Abstractions;
using DotCraft.Agents;
using DotCraft.Diagnostics;
using DotCraft.Commands.Core;
using DotCraft.Commands.Custom;
using DotCraft.Context;
using DotCraft.Cron;
using DotCraft.Tracing;
using DotCraft.Sessions;
using DotCraft.Sessions.Protocol;
using DotCraft.Heartbeat;
using DotCraft.Memory;
using DotCraft.QQ.OneBot;
using DotCraft.Security;
using DotCraft.Tools;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace DotCraft.QQ;

public sealed class QQChannelAdapter : IAsyncDisposable
{
    private readonly QQBotClient _client;

    private readonly QQPermissionService _permissionService;

    private readonly QQApprovalService? _approvalService;

    private readonly HeartbeatService? _heartbeatService;

    private readonly CronService? _cronService;

    private readonly AgentFactory? _agentFactory;

    private readonly TraceCollector? _traceCollector;

    private readonly TokenUsageStore? _tokenUsageStore;
    
    private readonly CommandDispatcher _commandDispatcher;

    private readonly ActiveRunRegistry _activeRunRegistry;

    private readonly HttpClient _httpClient;

    private readonly ISessionService? _sessionService;

    private readonly string _workspacePath;

    // Pending session-protocol approval requests keyed by "{userContextKey}_{requestId}"
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<bool>>
        _pendingSessionApprovals = new();

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    
    public QQChannelAdapter(
        QQBotClient client,
        QQPermissionService permissionService,
        ActiveRunRegistry activeRunRegistry,
        QQApprovalService? approvalService = null,
        HeartbeatService? heartbeatService = null,
        CronService? cronService = null,
        AgentFactory? agentFactory = null,
        TraceCollector? traceCollector = null,
        TokenUsageStore? tokenUsageStore = null,
        CustomCommandLoader? customCommandLoader = null,
        HttpClient? httpClient = null,
        ISessionService? sessionService = null,
        string workspacePath = "")
    {
        _client = client;
        _permissionService = permissionService;
        _approvalService = approvalService;
        _heartbeatService = heartbeatService;
        _cronService = cronService;
        _agentFactory = agentFactory;
        _activeRunRegistry = activeRunRegistry;
        _traceCollector = traceCollector;
        _tokenUsageStore = tokenUsageStore;
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        _sessionService = sessionService;
        _workspacePath = workspacePath;
        
        _commandDispatcher = CommandDispatcher.CreateDefault(customCommandLoader);

        _client.OnGroupMessage += HandleGroupMessageAsync;
        _client.OnPrivateMessage += HandlePrivateMessageAsync;
    }

    public async ValueTask DisposeAsync()
    {
        await _client.DisposeAsync();
        if (_agentFactory != null)
            await _agentFactory.DisposeAsync();
        _httpClient.Dispose();
    }

    private async Task HandleGroupMessageAsync(OneBotMessageEvent evt)
    {
        var plainText = evt.GetPlainText().Trim();
        var contentParts = await BuildMultimodalContentAsync(evt);

        if (string.IsNullOrEmpty(plainText) && contentParts == null)
            return;

        if (_approvalService != null && _approvalService.TryHandleApprovalReply(evt))
            return;

        if (TryHandleSessionApprovalReply(evt, plainText))
            return;

        var selfId = evt.SelfId;
        var isAtSelf = false;
        foreach (var seg in evt.Message)
        {
            var atQQ = seg.GetAtQQ();
            if (atQQ != null && atQQ == selfId.ToString())
            {
                isAtSelf = true;
                break;
            }
        }

        if (!isAtSelf)
            return;

        var role = _permissionService.GetUserRole(evt.UserId, evt.GroupId);
        if (role == QQUserRole.Unauthorized)
        {
            LogUnauthorized("group", evt.GroupId.ToString(), evt.Sender.DisplayName);
            return;
        }

        LogIncoming("group", evt.GroupId.ToString(), evt.Sender.DisplayName,
            plainText.Length > 0 ? plainText : "[image]");
        await ProcessMessageAsync(evt, plainText, contentParts, role);
    }

    private async Task HandlePrivateMessageAsync(OneBotMessageEvent evt)
    {
        var plainText = evt.GetPlainText().Trim();
        var contentParts = await BuildMultimodalContentAsync(evt);

        if (string.IsNullOrEmpty(plainText) && contentParts == null)
            return;

        if (_approvalService != null && _approvalService.TryHandleApprovalReply(evt))
            return;

        if (TryHandleSessionApprovalReply(evt, plainText))
            return;

        var role = _permissionService.GetUserRole(evt.UserId);
        if (role == QQUserRole.Unauthorized)
        {
            LogUnauthorized("private", evt.UserId.ToString(), evt.Sender.DisplayName);
            return;
        }

        LogIncoming("private", evt.UserId.ToString(), evt.Sender.DisplayName,
            plainText.Length > 0 ? plainText : "[image]");
        await ProcessMessageAsync(evt, plainText, contentParts, role);
    }

    /// <summary>
    /// Extracts image segments from the message as multimodal content parts.
    /// Images are downloaded as inline bytes since platform URLs may not be publicly accessible.
    /// Returns <see langword="null"/> when the message contains no images.
    /// </summary>
    private async Task<IList<AIContent>?> BuildMultimodalContentAsync(OneBotMessageEvent evt)
    {
        var hasImage = false;
        foreach (var seg in evt.Message)
        {
            if (seg.Type == "image")
            {
                hasImage = true;
                break;
            }
        }

        if (!hasImage)
            return null;

        var parts = new List<AIContent>();
        foreach (var seg in evt.Message)
        {
            switch (seg.Type)
            {
                case "text":
                {
                    var text = seg.GetText();
                    if (!string.IsNullOrEmpty(text))
                        parts.Add(new TextContent(text));
                    break;
                }
                case "image":
                {
                    var url = seg.GetImageUrl();
                    if (!string.IsNullOrEmpty(url))
                    {
                        var img = await DownloadImageAsync(url);
                        if (img != null)
                            parts.Add(img);
                    }
                    break;
                }
            }
        }

        return parts.Count > 0 ? parts : null;
    }

    private async Task<DataContent?> DownloadImageAsync(string imageUrl)
    {
        try
        {
            using var response = await _httpClient.GetAsync(imageUrl);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync();
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            return new DataContent(bytes, mediaType);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[yellow][[QQ]][/] Failed to download image: {Markup.Escape(ex.Message)}");
            return null;
        }
    }

    private static HttpClient CreateDefaultHttpClient() => new(new SocketsHttpHandler
    {
        SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        ConnectTimeout = TimeSpan.FromSeconds(10),
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "DotCraft/1.0" } }
    };

    private async Task ProcessMessageAsync(
        OneBotMessageEvent evt, string plainText, IList<AIContent>? multimodalContent, QQUserRole role)
    {
        var sessionId = $"qq_{evt.GetSessionId()}";

        var cmdResult = await HandleCommandAsync(evt, plainText, sessionId);
        if (cmdResult.Handled && cmdResult.ExpandedPrompt == null)
            return;
        if (cmdResult.ExpandedPrompt != null)
            plainText = cmdResult.ExpandedPrompt;

        // Build the final content: use multimodal parts when available, otherwise text-only
        IList<AIContent> contentParts;
        if (cmdResult.ExpandedPrompt != null)
        {
            // Command expansion replaces the original content with text
            contentParts = [new TextContent(plainText)];
        }
        else if (multimodalContent != null)
        {
            contentParts = multimodalContent;
        }
        else
        {
            contentParts = [new TextContent(plainText)];
        }

        RuntimeContextBuilder.AppendTo(contentParts);

        var approvalContext = new ApprovalContext
        {
            UserId = evt.UserId.ToString(),
            UserRole = role.ToString(),
            GroupId = evt.IsGroupMessage ? evt.GroupId : 0,
            Source = ApprovalSource.QQ
        };

        var chatContext = new QQChatContext
        {
            IsGroupMessage = evt.IsGroupMessage,
            GroupId = evt.GroupId,
            UserId = evt.UserId,
            SenderName = evt.Sender.DisplayName
        };

        try
        {
            using (ApprovalContextScope.Set(approvalContext))
            using (QQChatContextScope.Set(chatContext))
            using (ChannelSessionScope.Set(new ChannelSessionInfo
                   {
                       Channel = "qq",
                       UserId = evt.UserId.ToString(),
                       GroupId = evt.IsGroupMessage ? evt.GroupId.ToString() : null,
                       DefaultDeliveryTarget = evt.IsGroupMessage ? $"group:{evt.GroupId}" : null
                   }))
            {
                await ProcessMessageViaSessionServiceAsync(evt, plainText, role);
            }
        }
        catch (SessionGateOverflowException)
        {
            AnsiConsole.MarkupLine($"[grey][[QQ]][/] [yellow]Request evicted for session {Markup.Escape(sessionId)} (queue overflow)[/]");
            try
            {
                await _client.SendMessageAsync(evt, "消息过多，该条已跳过，请稍后重试。");
            }
            catch
            {
                // ignored
            }
        }
        catch (Exception ex)
        {
            LogError(evt.Sender.DisplayName, ex.Message);
            try
            {
                await _client.SendMessageAsync(evt, $"[Error] {ex.Message}");
            }
            catch
            {
                // ignored
            }
        }
    }

    private async Task ProcessMessageViaSessionServiceAsync(
        OneBotMessageEvent evt, string text, QQUserRole role)
    {
        var channelContext = evt.IsGroupMessage
            ? $"group:{evt.GroupId}"
            : $"user:{evt.UserId}";
        var identity = new SessionIdentity
        {
            ChannelName = "qq",
            UserId = evt.UserId.ToString(),
            ChannelContext = channelContext,
            WorkspacePath = _workspacePath
        };

        // Find or create a Thread for this QQ context
        var threads = await _sessionService!.FindThreadsAsync(identity);
        string threadId;
        if (threads.Count > 0 && threads[0].Status != ThreadStatus.Archived)
        {
            threadId = threads[0].Id;
            if (threads[0].Status == ThreadStatus.Paused)
                await _sessionService.ResumeThreadAsync(threadId);
        }
        else
        {
            var config = new ThreadConfiguration();
            var thread = await _sessionService.CreateThreadAsync(
                identity,
                config,
                ct: CancellationToken.None);
            threadId = thread.Id;
        }

        var sender = new SenderContext
        {
            SenderId = evt.UserId.ToString(),
            SenderName = evt.Sender.DisplayName
        };

        var textBuffer = new StringBuilder();
        string? activeTurnId = null;

        try
        {
            using var runCts = new CancellationTokenSource();
            _activeRunRegistry.Register(threadId, runCts);
            try
            {
                await foreach (var sessionEvt in _sessionService
                    .SubmitInputAsync(threadId, text, sender, ct: runCts.Token)
                    .WithCancellation(runCts.Token))
                {
                    if (sessionEvt.TurnId != null)
                        activeTurnId = sessionEvt.TurnId;

                    switch (sessionEvt.EventType)
                    {
                        case SessionEventType.ItemDelta when sessionEvt.DeltaPayload is { } delta:
                            textBuffer.Append(delta.TextDelta);
                            break;

                        case SessionEventType.ItemDelta when sessionEvt.ReasoningDeltaPayload is { } reasoning:
                            // In debug mode, show reasoning inline; otherwise silently collect for console log
                            if (DebugModeService.IsEnabled() && !string.IsNullOrEmpty(reasoning.TextDelta))
                                textBuffer.Append(ReasoningContentHelper.FormatBlock(reasoning.TextDelta));
                            else if (!string.IsNullOrEmpty(reasoning.TextDelta))
                                LogThinking(reasoning.TextDelta);
                            break;

                        case SessionEventType.ItemStarted when sessionEvt.ItemPayload?.Type == ItemType.ToolCall:
                        {
                            await FlushTextBufferAsync(evt, textBuffer);
                            if (DebugModeService.IsEnabled())
                            {
                                var tp = sessionEvt.ItemPayload!.Payload as ToolCallPayload;
                                var toolName = tp?.ToolName ?? string.Empty;
                                var icon = ToolRegistry.GetToolIcon(toolName);
                                var displayText = ToolRegistry.FormatToolCall(toolName, tp?.Arguments) ?? toolName;
                                var toolNotice = $"{icon} {displayText}";
                                await _client.SendMessageAsync(evt, toolNotice);
                            }
                            break;
                        }

                        case SessionEventType.ApprovalRequested:
                        {
                            var item = sessionEvt.ItemPayload;
                            if (item?.Payload is ApprovalRequestPayload req && activeTurnId != null)
                            {
                                await FlushTextBufferAsync(evt, textBuffer);
                                var approved = await RequestSessionApprovalAsync(evt, req);
                                await _sessionService!.ResolveApprovalAsync(activeTurnId, req.RequestId, approved);
                            }
                            break;
                        }

                        case SessionEventType.TurnCompleted:
                        {
                            var usage = sessionEvt.TurnPayload?.TokenUsage;
                            if (usage != null)
                            {
                                _tokenUsageStore?.Record(new TokenUsageRecord
                                {
                                    Channel = "qq",
                                    UserId = evt.UserId.ToString(),
                                    DisplayName = evt.Sender.DisplayName,
                                    GroupId = evt.IsGroupMessage ? evt.GroupId : null,
                                    InputTokens = usage.InputTokens,
                                    OutputTokens = usage.OutputTokens
                                });

                                if (DebugModeService.IsEnabled())
                                    textBuffer.Append($"\n\n[↑ {usage.InputTokens} input ↓ {usage.OutputTokens} output]");
                            }
                            break;
                        }
                    }
                }
            }
            finally
            {
                _activeRunRegistry.Unregister(threadId);
            }
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine($"[grey][[QQ]][/] [yellow]Agent run interrupted for thread {Markup.Escape(threadId)}[/]");
        }

        await FlushTextBufferAsync(evt, textBuffer);
    }

    private async Task FlushTextBufferAsync(OneBotMessageEvent evt, StringBuilder buffer)
    {
        var text = buffer.ToString().Trim();
        buffer.Clear();
        if (string.IsNullOrEmpty(text))
            return;
        await _client.SendMessageAsync(evt, text);
        LogOutgoing(evt, text);
    }

    private async Task<CommandResult> HandleCommandAsync(OneBotMessageEvent evt, string text, string sessionId)
    {
        var role = _permissionService.GetUserRole(evt.UserId, evt.GroupId);
        var context = new CommandContext
        {
            SessionId = sessionId,
            RawText = text,
            UserId = evt.UserId.ToString(),
            UserName = evt.Sender.DisplayName,
            IsAdmin = role == QQUserRole.Admin,
            Source = "QQ",
            GroupId = evt.IsGroupMessage ? evt.GroupId.ToString() : null,
            SessionService = _sessionService,
            HeartbeatService = _heartbeatService,
            CronService = _cronService,
            AgentFactory = _agentFactory,
            ActiveRunRegistry = _activeRunRegistry
        };
        
        var responder = new QQCommandResponder(_client, evt);
        return await _commandDispatcher.TryDispatchAsync(text, context, responder);
    }

    private static void LogIncoming(string type, string targetId, string sender, string text)
    {
        var tag = type == "group"
            ? $"[cyan]Group {Markup.Escape(targetId)}[/]"
            : "[cyan]Private[/]";
        var preview = text.Length > 80 ? text[..80] + "..." : text;
        AnsiConsole.MarkupLine($"[grey][[QQ]][/] {tag} [green]{Markup.Escape(sender)}[/]: {Markup.Escape(preview)}");
    }

    private static void LogOutgoing(OneBotMessageEvent evt, string text)
    {
        var tag = evt.IsGroupMessage
            ? $"[cyan]Group {evt.GroupId}[/]"
            : "[cyan]Private[/]";
        var preview = text.Length > 80 ? text[..80] + "..." : text;
        AnsiConsole.MarkupLine($"[grey][[QQ]][/] {tag} [blue]DotCraft[/]: {Markup.Escape(preview)}");
    }

    private static void LogError(string sender, string error)
    {
        AnsiConsole.MarkupLine($"[grey][[QQ]][/] [red]Error[/] processing message from [green]{Markup.Escape(sender)}[/]: {Markup.Escape(error)}");
    }

    private static void LogUnauthorized(string type, string targetId, string sender)
    {
        var tag = type == "group"
            ? $"[cyan]Group {Markup.Escape(targetId)}[/]"
            : "[cyan]Private[/]";
        AnsiConsole.MarkupLine($"[grey][[QQ]][/] {tag} [yellow]Unauthorized[/] user [green]{Markup.Escape(sender)}[/] ignored");
    }

    private static void LogToolCall(string name, IDictionary<string, object?>? args)
    {
        var icon = ToolRegistry.GetToolIcon(name);
        var displayText = ToolRegistry.FormatToolCall(name, args) ?? name;
        AnsiConsole.MarkupLine($"[grey][[QQ]][/] [yellow]{Markup.Escape($"{icon} {displayText}")}[/]");

        if (DebugModeService.IsEnabled() && args != null)
        {
            try
            {
                var argsStr = JsonSerializer.Serialize(args, SerializerOptions);
                AnsiConsole.MarkupLine($"[grey][[QQ]][/]   [dim]{Markup.Escape(argsStr)}[/]");
            }
            catch { /* ignore serialization errors in debug logging */ }
        }
    }

    private static void LogToolResult(string? result)
    {
        var text = result ?? "(no output)";
        var preview = text.Length > 200 ? text[..200] + "..." : text;
        var normalized = preview.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        AnsiConsole.MarkupLine($"[grey][[QQ]][/]   [grey]{Markup.Escape(normalized)}[/]");
    }

    private static void LogThinking(string text)
    {
        var preview = ReasoningContentHelper.ToInlinePreview(text);
        AnsiConsole.MarkupLine($"[grey][[QQ]][/] [cyan]💭 Thinking[/] [grey]{Markup.Escape(preview)}[/]");
    }

    /// <summary>
    /// Checks whether <paramref name="plainText"/> is an approval reply for a pending session-protocol
    /// approval request from this user/group. If so, resolves the pending TCS and returns true.
    /// </summary>
    private bool TryHandleSessionApprovalReply(OneBotMessageEvent evt, string plainText)
    {
        if (_pendingSessionApprovals.IsEmpty)
            return false;

        var approved = plainText is "同意" or "允许" or "yes" or "y" or "approve"
                    or "同意全部" or "允许全部" or "yes all" or "approve all";
        var rejected = plainText is "拒绝" or "不同意" or "no" or "n" or "reject" or "deny";

        if (!approved && !rejected)
            return false;

        var keyPrefix = evt.IsGroupMessage
            ? $"group_{evt.GroupId}_{evt.UserId}_"
            : $"private_{evt.UserId}_";

        foreach (var key in _pendingSessionApprovals.Keys)
        {
            if (!key.StartsWith(keyPrefix))
                continue;

            if (_pendingSessionApprovals.TryRemove(key, out var tcs))
            {
                tcs.TrySetResult(approved);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Sends an approval prompt to the QQ user and waits for their reply (or timeout).
    /// Used in the session-protocol path instead of the legacy IApprovalService.
    /// </summary>
    private async Task<bool> RequestSessionApprovalAsync(
        OneBotMessageEvent evt, ApprovalRequestPayload req, int timeoutSeconds = 60)
    {
        await FlushTextBufferAsync(evt, new StringBuilder());

        var promptMsg = req.ApprovalType == "shell"
            ? $"⚠️ 需要执行命令权限：`{req.Operation}`\n回复 同意/yes 批准，拒绝/no 拒绝（{timeoutSeconds}秒超时自动拒绝）"
            : $"⚠️ 需要 {req.Operation} 文件权限：`{req.Target}`\n回复 同意/yes 批准，拒绝/no 拒绝（{timeoutSeconds}秒超时自动拒绝）";
        await _client.SendMessageAsync(evt, promptMsg);

        var key = evt.IsGroupMessage
            ? $"group_{evt.GroupId}_{evt.UserId}_{req.RequestId}"
            : $"private_{evt.UserId}_{req.RequestId}";

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingSessionApprovals[key] = tcs;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            cts.Token.Register(() => tcs.TrySetResult(false));
            return await tcs.Task;
        }
        finally
        {
            _pendingSessionApprovals.TryRemove(key, out _);
        }
    }
}
