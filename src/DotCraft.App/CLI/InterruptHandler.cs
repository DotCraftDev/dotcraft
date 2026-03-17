namespace DotCraft.CLI;

/// <summary>
/// Registers a background thread that polls for an Escape key press and cancels
/// the active agent run when detected.
/// </summary>
internal sealed class InterruptHandler : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly ConsoleCancelEventHandler _ctrlCHandler;
    private volatile bool _disposed;

    public InterruptHandler(CancellationTokenSource cts)
    {
        _cts = cts;

        // Suppress Ctrl+C default termination so the process stays alive.
        _ctrlCHandler = (_, e) => e.Cancel = true;
        Console.CancelKeyPress += _ctrlCHandler;

        var pollThread = new Thread(PollEscapeKey)
        {
            IsBackground = true,
            Name = "EscKeyPoller"
        };
        pollThread.Start();
    }

    private void PollEscapeKey()
    {
        while (!_disposed && !_cts.IsCancellationRequested)
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Escape)
                {
                    _cts.Cancel();
                    return;
                }
            }
            Thread.Sleep(100);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Console.CancelKeyPress -= _ctrlCHandler;
    }
}
