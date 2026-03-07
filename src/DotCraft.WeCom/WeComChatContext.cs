namespace DotCraft.WeCom;

public sealed class WeComChatContext
{
    public string ChatId { get; init; } = string.Empty;

    public string UserId { get; init; } = string.Empty;

    public string UserName { get; init; } = string.Empty;
}

public static class WeComChatContextScope
{
    private static readonly AsyncLocal<WeComChatContext?> CurrentContext = new();

    public static WeComChatContext? Current
    {
        get => CurrentContext.Value;
        set => CurrentContext.Value = value;
    }

    public static IDisposable Set(WeComChatContext context)
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
