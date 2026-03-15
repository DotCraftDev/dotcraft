using System.Text;
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
using DotCraft.Security;
using DotCraft.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace DotCraft.WeCom;

/// <summary>
/// WeCom channel adapter - bridges WeCom messages to the DotCraft Agent.
/// </summary>
public sealed class WeComChannelAdapter : IAsyncDisposable
{
    private readonly SessionStore _sessionStore;
    
    private readonly HeartbeatService? _heartbeatService;
    
    private readonly CronService? _cronService;
    
    private readonly WeComPermissionService _permissionService;
    
    private readonly WeComApprovalService _approvalService;

    private readonly TraceCollector? _traceCollector;

    private readonly TokenUsageStore? _tokenUsageStore;
    
    private readonly AgentFactory? _agentFactory;
    
    private readonly CommandDispatcher _commandDispatcher;

    private readonly ActiveRunRegistry _activeRunRegistry;

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    private readonly ISessionService? _sessionService;

    private readonly string _workspacePath;

    // Pending session-protocol approval requests keyed by "user_{userId}_{requestId}"
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<bool>>
        _pendingSessionApprovals = new();

    public WeComChannelAdapter(
        SessionStore sessionStore,
        WeComBotRegistry registry,
        WeComPermissionService permissionService,
        WeComApprovalService approvalService,
        ActiveRunRegistry activeRunRegistry,
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
        _sessionStore = sessionStore;
        _heartbeatService = heartbeatService;
        _cronService = cronService;
        _agentFactory = agentFactory;
        _permissionService = permissionService;
        _approvalService = approvalService;
        _activeRunRegistry = activeRunRegistry;
        _traceCollector = traceCollector;
        _tokenUsageStore = tokenUsageStore;
        _ownsHttpClient = httpClient == null;
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        _sessionService = sessionService;
        _workspacePath = workspacePath;
        
        _commandDispatcher = CommandDispatcher.CreateDefault(customCommandLoader);

        // Attach handlers to all registered bot paths
        foreach (var path in registry.GetAllPaths())
        {
            registry.SetHandlers(path,
                textHandler: HandleTextMessageAsync,
                commonHandler: HandleCommonMessageAsync,
                eventHandler: HandleEventMessageAsync);
            AnsiConsole.MarkupLine($"[grey][[WeCom]][/] [green]Registered handler for:[/] {Markup.Escape(path)}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_agentFactory != null)
            await _agentFactory.DisposeAsync();
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }

    #region Message Handlers

    private async Task HandleTextMessageAsync(string[] parameters, WeComFrom from, IWeComPusher pusher)
    {
        var plainText = string.Join(" ", parameters).Trim();
        if (string.IsNullOrEmpty(plainText))
        {
            await pusher.PushTextAsync("请输入消息内容");
            return;
        }

        var chatId = pusher.GetChatId();
        var sessionId = $"wecom_{chatId}_{from.UserId}";

        LogIncoming("text", chatId, $"{from.Name} (uid={from.UserId})", plainText);

        // Try to handle approval reply first (legacy and session-protocol paths)
        if (_approvalService.TryHandleApprovalReply(plainText, from.UserId))
        {
            LogIncoming("approval", chatId, from.Name, $"审批回复: {plainText}");
            return;
        }

        if (TryHandleSessionApprovalReply(plainText, from.UserId))
        {
            LogIncoming("approval", chatId, from.Name, $"Session审批回复: {plainText}");
            return;
        }

        // Handle slash commands
        var cmdResult = await HandleCommandAsync(pusher, plainText, sessionId, from);
        if (cmdResult.Handled && cmdResult.ExpandedPrompt == null)
            return;
        if (cmdResult.ExpandedPrompt != null)
            plainText = cmdResult.ExpandedPrompt;

        IList<AIContent> contentParts = [new TextContent(plainText)];
        RuntimeContextBuilder.AppendTo(contentParts);

        await RunAgentAsync(contentParts, from, pusher);
    }

    private async Task HandleCommonMessageAsync(WeComMessage message, IWeComPusher pusher)
    {
        var from = message.From ?? new WeComFrom();
        var contentParts = await BuildMultimodalContentAsync(message);

        if (contentParts != null)
        {
            var logType = message.MsgType == WeComMsgType.Mixed ? "mixed" : message.MsgType;
            LogIncoming(logType, message.ChatId, from.Name, $"发送了{DescribeMsgType(message.MsgType)}");
            RuntimeContextBuilder.AppendTo(contentParts);
            await RunAgentAsync(contentParts, from, pusher);
            return;
        }

        // Unsupported types: log and echo back diagnostic info
        var info = $"收到 {message.MsgType} 类型消息";
        switch (message.MsgType)
        {
            case WeComMsgType.Attachment:
                info += $"\nCallbackId: {message.Attachment?.CallbackId}";
                LogIncoming("attachment", message.ChatId, from.Name, "发送了附件");
                break;
            case WeComMsgType.File:
                info += $"\n文件URL: {message.File?.Url}";
                LogIncoming("file", message.ChatId, from.Name, "发送了文件");
                break;
            default:
                LogIncoming(message.MsgType, message.ChatId, from.Name, info);
                break;
        }

        await pusher.PushTextAsync(info);
    }

    /// <summary>
    /// Converts Image, Mixed, and Voice messages into multimodal content parts.
    /// Images are downloaded as inline bytes since platform URLs are not publicly accessible.
    /// Returns <see langword="null"/> for unsupported message types.
    /// </summary>
    private async Task<IList<AIContent>?> BuildMultimodalContentAsync(WeComMessage message)
    {
        switch (message.MsgType)
        {
            case WeComMsgType.Image when !string.IsNullOrEmpty(message.Image?.ImageUrl):
            {
                var img = await DownloadImageAsync(message.Image!.ImageUrl);
                return img != null ? [img] : null;
            }

            case WeComMsgType.Mixed when message.MixedMessage?.MsgItems is { Count: > 0 } items:
            {
                var parts = new List<AIContent>(items.Count);
                foreach (var item in items)
                {
                    if (item.MsgType == WeComMsgType.Text && !string.IsNullOrEmpty(item.Text?.Content))
                        parts.Add(new TextContent(item.Text!.Content));
                    else if (item.MsgType == WeComMsgType.Image && !string.IsNullOrEmpty(item.Image?.ImageUrl))
                    {
                        var img = await DownloadImageAsync(item.Image!.ImageUrl);
                        if (img != null)
                            parts.Add(img);
                    }
                }
                return parts.Count > 0 ? parts : null;
            }

            case WeComMsgType.Voice when !string.IsNullOrEmpty(message.Voice?.Content):
                return [new TextContent(message.Voice!.Content)];

            default:
                return null;
        }
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
            AnsiConsole.MarkupLine($"[yellow][[WeCom]][/] Failed to download image: {Markup.Escape(ex.Message)}");
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

    private static string DescribeMsgType(string msgType) => msgType switch
    {
        WeComMsgType.Image => "图片",
        WeComMsgType.Mixed => "图文混排",
        WeComMsgType.Voice => "语音",
        _ => msgType
    };

    /// <summary>
    /// Shared agent invocation: sets up session, streams agent response, tracks tokens and errors.
    /// Accepts multimodal content to support text-only and image+text prompts.
    /// </summary>
    private async Task RunAgentAsync(IList<AIContent> contentParts, WeComFrom from, IWeComPusher pusher)
    {
        var chatId = pusher.GetChatId();
        var sessionId = $"wecom_{chatId}_{from.UserId}";

        var userRole = _permissionService.GetUserRole(from.UserId, chatId);
        var roleString = userRole switch
        {
            WeComUserRole.Admin => "Admin",
            WeComUserRole.Whitelisted => "Whitelisted",
            _ => "User"
        };

        var approvalContext = new ApprovalContext
        {
            UserId = from.UserId,
            UserRole = roleString,
            GroupId = 0,
            Source = ApprovalSource.WeCom
        };

        try
        {
            using (ApprovalContextScope.Set(approvalContext))
            using (WeComPusherScope.Set(pusher))
            using (WeComChatContextScope.Set(new WeComChatContext
                   {
                       ChatId = chatId,
                       UserId = from.UserId,
                       UserName = from.Name
                   }))
            using (ChannelSessionScope.Set(new ChannelSessionInfo
                   {
                       Channel = "wecom",
                       UserId = from.UserId,
                       GroupId = chatId,
                       DefaultDeliveryTarget = chatId
                   }))
            {
                // Extract text from content parts for Session Protocol path
                var textContent = string.Join(" ", contentParts.OfType<TextContent>().Select(t => t.Text));
                await RunAgentViaSessionServiceAsync(textContent, from, pusher, chatId);
            }
        }
        catch (SessionGateOverflowException)
        {
            AnsiConsole.MarkupLine($"[grey][[WeCom]][/] [yellow]Request evicted for session {Markup.Escape(sessionId)} (queue overflow)[/]");
            try
            {
                await pusher.PushTextAsync("消息过多，该条已跳过，请稍后重试。");
            }
            catch
            {
                // ignored
            }
        }
        catch (Exception ex)
        {
            LogError(from.Name, ex.Message);
            try
            {
                await pusher.PushTextAsync($"[Error] {ex.Message}");
            }
            catch
            {
                // ignored
            }
        }
    }

    private async Task RunAgentViaSessionServiceAsync(string text, WeComFrom from, IWeComPusher pusher, string chatId)
    {
        var identity = new SessionIdentity
        {
            ChannelName = "wecom",
            UserId = from.UserId,
            ChannelContext = $"chat:{chatId}",
            WorkspacePath = _workspacePath
        };

        // Find or create a Thread for this WeCom chat context
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
            var thread = await _sessionService.CreateThreadAsync(identity, ct: CancellationToken.None);
            threadId = thread.Id;
        }

        var sender = new SenderContext
        {
            SenderId = from.UserId,
            SenderName = from.Name
        };

        var textBuffer = new StringBuilder();
        string? activeTurnId = null;

        try
        {
            using var runCts = new CancellationTokenSource();
            _activeRunRegistry.Register(threadId, runCts);
            try
            {
                await foreach (var evt in _sessionService
                    .SubmitInputAsync(threadId, text, sender, ct: runCts.Token)
                    .WithCancellation(runCts.Token))
                {
                    if (evt.TurnId != null)
                        activeTurnId = evt.TurnId;

                    switch (evt.EventType)
                    {
                        case SessionEventType.ItemDelta when evt.DeltaPayload is { } delta:
                            textBuffer.Append(delta.TextDelta);
                            break;

                        case SessionEventType.ItemDelta when evt.ReasoningDeltaPayload is { } reasoning:
                            // In debug mode show reasoning inline; otherwise log to console only
                            if (DebugModeService.IsEnabled() && !string.IsNullOrEmpty(reasoning.TextDelta))
                                textBuffer.Append(ReasoningContentHelper.FormatBlock(reasoning.TextDelta));
                            break;

                        case SessionEventType.ItemStarted when evt.ItemPayload?.Type == ItemType.ToolCall:
                        {
                            await FlushTextBufferAsync(pusher, textBuffer);
                            if (DebugModeService.IsEnabled())
                            {
                                var tp = evt.ItemPayload!.Payload as ToolCallPayload;
                                var toolName = tp?.ToolName ?? string.Empty;
                                var icon = ToolRegistry.GetToolIcon(toolName);
                                var displayText = ToolRegistry.FormatToolCall(toolName, tp?.Arguments) ?? toolName;
                                await pusher.PushTextAsync($"{icon} {displayText}");
                            }
                            break;
                        }

                        case SessionEventType.ApprovalRequested:
                        {
                            var item = evt.ItemPayload;
                            if (item?.Payload is ApprovalRequestPayload req && activeTurnId != null)
                            {
                                await FlushTextBufferAsync(pusher, textBuffer);
                                var approved = await RequestSessionApprovalAsync(pusher, from, req);
                                await _sessionService!.ResolveApprovalAsync(activeTurnId, req.RequestId, approved);
                            }
                            break;
                        }

                        case SessionEventType.TurnCompleted:
                        {
                            var usage = evt.TurnPayload?.TokenUsage;
                            if (usage != null)
                            {
                                _tokenUsageStore?.Record(new TokenUsageRecord
                                {
                                    Channel = "wecom",
                                    UserId = from.UserId,
                                    DisplayName = from.Name,
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
            AnsiConsole.MarkupLine($"[grey][[WeCom]][/] [yellow]Agent run interrupted for thread {Markup.Escape(threadId)}[/]");
        }

        await FlushTextBufferAsync(pusher, textBuffer);
    }

    private static async Task<string?> HandleEventMessageAsync(string eventType, string chatType, WeComFrom from,
        IWeComPusher pusher)
    {
        var message = eventType switch
        {
            WeComEventType.AddToChat =>
                $"欢迎 {from.Name} 将我添加到{(chatType == WeComChatType.Group ? "群聊" : "会话")}！输入 /help 查看可用命令。",
            WeComEventType.EnterChat => $"你好，{from.Name}！我是 DotCraft，随时为您服务。输入 /help 查看可用命令。",
            WeComEventType.DeleteFromChat => "再见！",
            _ => null
        };

        if (message != null)
        {
            LogIncoming("event", pusher.GetChatId(), from.Name, eventType);
            await pusher.PushTextAsync(message);
            LogOutgoing(pusher.GetChatId(), message);
        }

        return null; // Already replied via pusher
    }

    #endregion

    #region Commands

    private async Task<CommandResult> HandleCommandAsync(IWeComPusher pusher, string text, string sessionId, WeComFrom from)
    {
        var userId = from.UserId;
        var userName = from.Name;
        var chatId = pusher.GetChatId();
        
        var userRole = _permissionService.GetUserRole(userId, chatId);
        var context = new CommandContext
        {
            SessionId = sessionId,
            RawText = text,
            UserId = userId,
            UserName = userName,
            IsAdmin = userRole == WeComUserRole.Admin,
            Source = "WeCom",
            GroupId = chatId,
            SessionStore = _sessionStore,
            HeartbeatService = _heartbeatService,
            CronService = _cronService,
            AgentFactory = _agentFactory,
            ActiveRunRegistry = _activeRunRegistry
        };
        
        var responder = new WeComCommandResponder(pusher);
        return await _commandDispatcher.TryDispatchAsync(text, context, responder);
    }

    #endregion

    #region Helpers

    private static async Task FlushTextBufferAsync(IWeComPusher pusher, StringBuilder buffer)
    {
        var text = buffer.ToString().Trim();
        buffer.Clear();
        if (string.IsNullOrEmpty(text))
            return;

        await pusher.PushMarkdownAsync(text);
        LogOutgoing(pusher.GetChatId(), text);
    }

    #endregion

    #region Logging

    private static void LogIncoming(string type, string chatId, string sender, string text)
    {
        var tag = $"[cyan]Chat {Markup.Escape(chatId)}[/]";
        var preview = text.Length > 80 ? text[..80] + "..." : text;
        AnsiConsole.MarkupLine(
            $"[grey][[WeCom]][/] {tag} [green]{Markup.Escape(sender)}[/] ({type}): {Markup.Escape(preview)}");
    }

    private static void LogOutgoing(string chatId, string text)
    {
        var tag = $"[cyan]Chat {Markup.Escape(chatId)}[/]";
        var preview = text.Length > 80 ? text[..80] + "..." : text;
        AnsiConsole.MarkupLine(
            $"[grey][[WeCom]][/] {tag} [blue]DotCraft[/]: {Markup.Escape(preview)}");
    }

    private static void LogError(string sender, string error)
    {
        AnsiConsole.MarkupLine(
            $"[grey][[WeCom]][/] [red]Error[/] processing message from [green]{Markup.Escape(sender)}[/]: {Markup.Escape(error)}");
    }

    private static void LogToolCall(string name, IDictionary<string, object?>? args)
    {
        var icon = ToolRegistry.GetToolIcon(name);
        var displayText = ToolRegistry.FormatToolCall(name, args) ?? name;
        AnsiConsole.MarkupLine(
            $"[grey][[WeCom]][/] [yellow]{Markup.Escape($"{icon} {displayText}")}[/]");

        if (DebugModeService.IsEnabled() && args != null)
        {
            try
            {
                var argsStr = JsonSerializer.Serialize(args, new JsonSerializerOptions { WriteIndented = false });
                AnsiConsole.MarkupLine($"[grey][[WeCom]][/]   [dim]{Markup.Escape(argsStr)}[/]");
            }
            catch { /* ignore serialization errors in debug logging */ }
        }
    }

    private static void LogToolResult(string? result)
    {
        var text = result ?? "(no output)";
        var preview = text.Length > 200 ? text[..200] + "..." : text;
        var normalized = preview.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        AnsiConsole.MarkupLine(
            $"[grey][[WeCom]][/]   [grey]{Markup.Escape(normalized)}[/]");
    }

    private static void LogThinking(string text)
    {
        var preview = ReasoningContentHelper.ToInlinePreview(text);
        AnsiConsole.MarkupLine($"[grey][[WeCom]][/] [cyan]💭 Thinking[/] [grey]{Markup.Escape(preview)}[/]");
    }

    /// <summary>
    /// Checks whether <paramref name="plainText"/> is an approval reply for a pending session-protocol
    /// approval request from <paramref name="userId"/>. If so, resolves the TCS and returns true.
    /// </summary>
    private bool TryHandleSessionApprovalReply(string plainText, string userId)
    {
        if (_pendingSessionApprovals.IsEmpty)
            return false;

        var approved = plainText is "同意" or "允许" or "yes" or "y" or "approve"
                    or "同意全部" or "允许全部" or "yes all" or "approve all";
        var rejected = plainText is "拒绝" or "不同意" or "no" or "n" or "reject" or "deny";

        if (!approved && !rejected)
            return false;

        var keyPrefix = $"user_{userId}_";
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
    /// Sends an approval prompt via <paramref name="pusher"/> and waits for the user's WeCom reply.
    /// </summary>
    private async Task<bool> RequestSessionApprovalAsync(
        IWeComPusher pusher, WeComFrom from, ApprovalRequestPayload req, int timeoutSeconds = 60)
    {
        var promptMsg = req.ApprovalType == "shell"
            ? $"⚠️ 需要执行命令权限：`{req.Operation}`\n回复 同意/yes 批准，拒绝/no 拒绝（{timeoutSeconds}秒超时自动拒绝）"
            : $"⚠️ 需要 {req.Operation} 文件权限：`{req.Target}`\n回复 同意/yes 批准，拒绝/no 拒绝（{timeoutSeconds}秒超时自动拒绝）";
        await pusher.PushTextAsync(promptMsg);

        var key = $"user_{from.UserId}_{req.RequestId}";
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

    #endregion
}
