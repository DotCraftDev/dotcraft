using DotCraft.QQ.OneBot;

namespace DotCraft.QQ;

public sealed class QQBotClient : IAsyncDisposable
{
    private readonly OneBotReverseWsServer _server;

    public event Func<OneBotMessageEvent, Task>? OnGroupMessage;
    
    public event Func<OneBotMessageEvent, Task>? OnPrivateMessage;
    
    public event Func<OneBotNoticeEvent, Task>? OnNotice;
    
    public event Func<OneBotRequestEvent, Task>? OnRequest;
    
    public event Action<string>? OnLog;

    public bool IsConnected => _server.ConnectionCount > 0;

    public QQBotClient(string host = "127.0.0.1", int port = 6700, string? accessToken = null)
    {
        _server = new OneBotReverseWsServer(host, port, accessToken);
        _server.OnMessageEvent += HandleMessageEvent;
        _server.OnNoticeEvent += HandleNoticeEvent;
        _server.OnRequestEvent += HandleRequestEvent;
        _server.OnMetaEvent += HandleMetaEvent;
        _server.OnConnected += id => Log($"QQ client connected: {id}");
        _server.OnDisconnected += id => Log($"QQ client disconnected: {id}");
        _server.OnLog += Log;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return _server.StartAsync(cancellationToken);
    }

    public Task StopAsync()
    {
        return _server.StopAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _server.DisposeAsync();
    }

    public Task<OneBotActionResponse> SendGroupMessageAsync(long groupId, string text)
    {
        var message = new List<OneBotMessageSegment> { OneBotMessageSegment.Text(text) };
        return SendGroupMessageAsync(groupId, message);
    }

    public Task<OneBotActionResponse> SendGroupMessageAsync(long groupId, List<OneBotMessageSegment> message)
    {
        var action = OneBotAction.SendGroupMessage(groupId, message);
        return _server.SendActionAsync(action);
    }

    public Task<OneBotActionResponse> SendPrivateMessageAsync(long userId, string text)
    {
        var message = new List<OneBotMessageSegment> { OneBotMessageSegment.Text(text) };
        return SendPrivateMessageAsync(userId, message);
    }

    public Task<OneBotActionResponse> SendPrivateMessageAsync(long userId, List<OneBotMessageSegment> message)
    {
        var action = OneBotAction.SendPrivateMessage(userId, message);
        return _server.SendActionAsync(action);
    }

    public Task<OneBotActionResponse> SendMessageAsync(OneBotMessageEvent sourceEvent, string text)
    {
        if (sourceEvent.IsGroupMessage)
            return SendGroupMessageAsync(sourceEvent.GroupId, text);
        return SendPrivateMessageAsync(sourceEvent.UserId, text);
    }

    public Task<OneBotActionResponse> SendMessageAsync(OneBotMessageEvent sourceEvent, List<OneBotMessageSegment> message)
    {
        if (sourceEvent.IsGroupMessage)
            return SendGroupMessageAsync(sourceEvent.GroupId, message);
        return SendPrivateMessageAsync(sourceEvent.UserId, message);
    }

    public Task<OneBotActionResponse> SendGroupRecordAsync(long groupId, string file)
    {
        var message = new List<OneBotMessageSegment> { OneBotMessageSegment.Record(file) };
        return SendGroupMessageAsync(groupId, message);
    }

    public Task<OneBotActionResponse> SendPrivateRecordAsync(long userId, string file)
    {
        var message = new List<OneBotMessageSegment> { OneBotMessageSegment.Record(file) };
        return SendPrivateMessageAsync(userId, message);
    }

    public Task<OneBotActionResponse> SendGroupVideoAsync(long groupId, string file)
    {
        var message = new List<OneBotMessageSegment> { OneBotMessageSegment.Video(file) };
        return SendGroupMessageAsync(groupId, message);
    }

    public Task<OneBotActionResponse> SendPrivateVideoAsync(long userId, string file)
    {
        var message = new List<OneBotMessageSegment> { OneBotMessageSegment.Video(file) };
        return SendPrivateMessageAsync(userId, message);
    }

    public Task<OneBotActionResponse> UploadGroupFileAsync(long groupId, string file, string name, string? folder = null)
    {
        var action = OneBotAction.UploadGroupFile(groupId, file, name, folder);
        return _server.SendActionAsync(action);
    }

    public Task<OneBotActionResponse> UploadPrivateFileAsync(long userId, string file, string name)
    {
        var action = OneBotAction.UploadPrivateFile(userId, file, name);
        return _server.SendActionAsync(action);
    }

    public Task<OneBotActionResponse> CallActionAsync(OneBotAction action, TimeSpan? timeout = null)
    {
        return _server.SendActionAsync(action, timeout);
    }

    public Task<OneBotActionResponse> GetLoginInfoAsync()
    {
        return _server.SendActionAsync(OneBotAction.GetLoginInfo());
    }

    public Task<OneBotActionResponse> GetGroupInfoAsync(long groupId)
    {
        return _server.SendActionAsync(OneBotAction.GetGroupInfo(groupId));
    }

    public Task<OneBotActionResponse> GetGroupMemberInfoAsync(long groupId, long userId, bool noCache = false)
    {
        return _server.SendActionAsync(OneBotAction.GetGroupMemberInfo(groupId, userId, noCache));
    }

    private async Task HandleMessageEvent(OneBotMessageEvent evt)
    {
        if (evt.IsGroupMessage && OnGroupMessage != null)
            await OnGroupMessage(evt);
        else if (evt.IsPrivateMessage && OnPrivateMessage != null)
            await OnPrivateMessage(evt);
    }

    private async Task HandleNoticeEvent(OneBotNoticeEvent evt)
    {
        if (OnNotice != null)
            await OnNotice(evt);
    }

    private async Task HandleRequestEvent(OneBotRequestEvent evt)
    {
        if (OnRequest != null)
            await OnRequest(evt);
    }

    private Task HandleMetaEvent(OneBotMetaEvent evt)
    {
        if (evt.IsLifecycle)
            Log($"OneBot lifecycle event: sub_type={evt.SubType}");
        return Task.CompletedTask;
    }

    private void Log(string message)
    {
        OnLog?.Invoke(message);
    }
}
