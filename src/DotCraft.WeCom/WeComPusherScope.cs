namespace DotCraft.WeCom;

public static class WeComPusherScope
{
    private static readonly AsyncLocal<IWeComPusher?> CurrentPusher = new();

    public static IWeComPusher? Current
    {
        get => CurrentPusher.Value;
        set => CurrentPusher.Value = value;
    }

    public static IDisposable Set(IWeComPusher pusher)
    {
        CurrentPusher.Value = pusher;
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
            CurrentPusher.Value = null;
        }
    }
}
