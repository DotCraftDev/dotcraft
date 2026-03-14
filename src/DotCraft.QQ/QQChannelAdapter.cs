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
using DotCraft.Heartbeat;
using DotCraft.Memory;
using DotCraft.QQ.OneBot;
using DotCraft.Security;
using DotCraft.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Spectre.Console;

namespace DotCraft.QQ;

public sealed class QQChannelAdapter : IAsyncDisposable
{
    private readonly QQBotClient _client;
    
    private readonly AIAgent _agent;
    
    private readonly SessionStore _sessionStore;

    private readonly QQPermissionService _permissionService;

    private readonly QQApprovalService? _approvalService;

    private readonly HeartbeatService? _heartbeatService;

    private readonly CronService? _cronService;

    private readonly AgentFactory? _agentFactory;

    private readonly TraceCollector? _traceCollector;

    private readonly SessionGate _sessionGate;

    private readonly TokenUsageStore? _tokenUsageStore;
    
    private readonly CommandDispatcher _commandDispatcher;

    private readonly ActiveRunRegistry _activeRunRegistry;

    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    
    public QQChannelAdapter(
        QQBotClient client,
        AIAgent agent,
        SessionStore sessionStore,
        QQPermissionService permissionService,
        SessionGate sessionGate,
        ActiveRunRegistry activeRunRegistry,
        QQApprovalService? approvalService = null,
        HeartbeatService? heartbeatService = null,
        CronService? cronService = null,
        AgentFactory? agentFactory = null,
        TraceCollector? traceCollector = null,
        TokenUsageStore? tokenUsageStore = null,
        CustomCommandLoader? customCommandLoader = null,
        HttpClient? httpClient = null)
    {
        _client = client;
        _agent = agent;
        _sessionStore = sessionStore;
        _permissionService = permissionService;
        _approvalService = approvalService;
        _heartbeatService = heartbeatService;
        _cronService = cronService;
        _agentFactory = agentFactory;
        _sessionGate = sessionGate;
        _activeRunRegistry = activeRunRegistry;
        _traceCollector = traceCollector;
        _tokenUsageStore = tokenUsageStore;
        _httpClient = httpClient ?? CreateDefaultHttpClient();
        
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
            using (await _sessionGate.AcquireAsync(sessionId))
            {
                var session = await _sessionStore.LoadOrCreateAsync(_agent, sessionId, CancellationToken.None);

                var textBuffer = new StringBuilder();
                long inputTokens = 0, outputTokens = 0, totalTokens = 0;
                var tokenTracker = _agentFactory?.GetOrCreateTokenTracker(sessionId);

                _traceCollector?.RecordSessionMetadata(
                    sessionId,
                    null,
                    _agentFactory?.LastCreatedTools?.Select(t => t.Name));

                TracingChatClient.CurrentSessionKey = sessionId;
                TracingChatClient.ResetCallState(sessionId);

                using var runCts = new CancellationTokenSource();
                _activeRunRegistry.Register(sessionId, runCts);
                var runToken = runCts.Token;
                var agentInterrupted = false;
                try
                {
                    var userMessage = new ChatMessage(ChatRole.User, contentParts);
                    await foreach (var update in _agent.RunStreamingAsync([userMessage], session, cancellationToken: runToken))
                    {
                        foreach (var content in update.Contents)
                        {
                            switch (content)
                            {
                                case TextReasoningContent reasoning:
                                    if (ReasoningContentHelper.TryGetText(reasoning, out var reasoningText))
                                    {
                                        ReasoningContentHelper.AppendBlock(textBuffer, reasoningText);
                                        LogThinking(reasoningText);
                                    }
                                    break;
                                case FunctionCallContent functionCall:
                                    await FlushTextBufferAsync(evt, textBuffer);

                                    if (DebugModeService.IsEnabled())
                                    {
                                        var icon = ToolRegistry.GetToolIcon(functionCall.Name);
                                        var displayText = ToolRegistry.FormatToolCall(functionCall.Name, functionCall.Arguments) ?? functionCall.Name;
                                        var toolNotice = $"{icon} {displayText}";
                                        await _client.SendMessageAsync(evt, toolNotice);
                                        LogOutgoing(evt, toolNotice);
                                    }
                                    LogToolCall(functionCall.Name, functionCall.Arguments);
                                    break;
                                case FunctionResultContent fr:
                                    LogToolResult(ImageContentSanitizingChatClient.DescribeResult(fr.Result));
                                    break;
                                case UsageContent usage:
                                    if (usage.Details.InputTokenCount.HasValue)
                                        inputTokens = usage.Details.InputTokenCount.Value;
                                    if (usage.Details.OutputTokenCount.HasValue)
                                        outputTokens = usage.Details.OutputTokenCount.Value;
                                    if (usage.Details.TotalTokenCount.HasValue)
                                        totalTokens = usage.Details.TotalTokenCount.Value;
                                    break;
                            }
                        }

                        if (!string.IsNullOrEmpty(update.Text))
                            textBuffer.Append(update.Text);
                    }
                }
                catch (OperationCanceledException) when (runCts.IsCancellationRequested)
                {
                    agentInterrupted = true;
                    AnsiConsole.MarkupLine($"[grey][[QQ]][/] [yellow]Agent run interrupted for session {Markup.Escape(sessionId)}[/]");
                }
                finally
                {
                    _activeRunRegistry.Unregister(sessionId);
                    TracingChatClient.ResetCallState(sessionId);
                    TracingChatClient.CurrentSessionKey = null;
                }

                if (agentInterrupted)
                {
                    await FlushTextBufferAsync(evt, textBuffer);
                    await _sessionStore.SaveAsync(_agent, session, sessionId, CancellationToken.None);
                    return;
                }

                if (!DebugModeService.IsEnabled())
                {
                    await FlushTextBufferAsync(evt, textBuffer);
                }
                
                if (totalTokens == 0 && (inputTokens > 0 || outputTokens > 0))
                    totalTokens = inputTokens + outputTokens;

                if (totalTokens > 0)
                {
                    tokenTracker?.Update(inputTokens, outputTokens);
                    var displayInput = tokenTracker?.LastInputTokens ?? inputTokens;
                    var displayOutput = tokenTracker?.TotalOutputTokens ?? outputTokens;
                    textBuffer.Append($"\n\n[? {displayInput} input ? {displayOutput} output]");
                }

                if (DebugModeService.IsEnabled())
                {
                    await FlushTextBufferAsync(evt, textBuffer);
                }

                if (totalTokens > 0)
                {
                    _tokenUsageStore?.Record(new TokenUsageRecord
                    {
                        Channel = "qq",
                        UserId = evt.UserId.ToString(),
                        DisplayName = evt.Sender.DisplayName,
                        GroupId = evt.IsGroupMessage ? evt.GroupId : null,
                        InputTokens = inputTokens,
                        OutputTokens = outputTokens
                    });
                }

                if (_agentFactory is { Compactor: not null, MaxContextTokens: > 0 } &&
                    inputTokens >= _agentFactory.MaxContextTokens)
                {
                    AnsiConsole.MarkupLine(
                        $"[grey][[QQ]][/] [yellow]Context compacting for session {Markup.Escape(sessionId)}...[/]");
                    await _client.SendMessageAsync(evt, "?? ??????????????...");
                    if (await _agentFactory.Compactor.TryCompactAsync(session))
                    {
                        tokenTracker?.Reset();
                        _traceCollector?.RecordContextCompaction(sessionId);
                        await _client.SendMessageAsync(evt, "? ???????????????");
                    }
                }

                _ = _agentFactory?.TryConsolidateMemory(session, sessionId);

                await _sessionStore.SaveAsync(_agent, session, sessionId, CancellationToken.None);
            }
        }
        catch (SessionGateOverflowException)
        {
            AnsiConsole.MarkupLine($"[grey][[QQ]][/] [yellow]Request evicted for session {Markup.Escape(sessionId)} (queue overflow)[/]");
            try
            {
                await _client.SendMessageAsync(evt, "?????????????????");
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
            SessionStore = _sessionStore,
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
        AnsiConsole.MarkupLine($"[grey][[QQ]][/] [cyan]?? Thinking[/] [grey]{Markup.Escape(preview)}[/]");
    }
}
