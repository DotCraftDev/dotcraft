namespace DotCraft.QQ;

public sealed class QQChatContext
{
    public bool IsGroupMessage { get; init; }

    public long GroupId { get; init; }

    public long UserId { get; init; }

    public string SenderName { get; init; } = string.Empty;
}

public static class QQChatContextScope
{
    private static readonly AsyncLocal<QQChatContext?> CurrentContext = new();

    public static QQChatContext? Current
    {
        get => CurrentContext.Value;
        set => CurrentContext.Value = value;
    }

    public static IDisposable Set(QQChatContext context)
    {
        CurrentContext.Value = context;
        return new ContextScope();
    }

    private sealed class ContextScope : IDisposable
    {
        public void Dispose()
        {
            CurrentContext.Value = null;
        }
    }
}
