namespace DotCraft.CLI;

/// <summary>
/// Registers a <see cref="Console.CancelKeyPress"/> handler that requires two Ctrl+C presses
/// within a confirmation window to cancel the active agent run.
///
/// The first press suppresses termination and prints a hint; the second press (within
/// <see cref="ConfirmWindowSeconds"/> seconds) cancels the supplied <see cref="CancellationTokenSource"/>.
/// Disposing this handler unregisters the event subscription.
/// </summary>
internal sealed class InterruptHandler : IDisposable
{
    private const int ConfirmWindowSeconds = 2;

    private readonly CancellationTokenSource _cts;
    private readonly ConsoleCancelEventHandler _handler;

    private long _firstPressTicks;

    public InterruptHandler(CancellationTokenSource cts)
    {
        _cts = cts;
        _handler = OnCancelKeyPress;
        Console.CancelKeyPress += _handler;
    }

    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        // Always suppress the default termination behavior.
        e.Cancel = true;

        if (_cts.IsCancellationRequested)
            return;

        long now = Environment.TickCount64;
        long first = Volatile.Read(ref _firstPressTicks);

        if (first == 0 || (now - first) > ConfirmWindowSeconds * 1000L)
        {
            // First press (or window expired): record timestamp and show hint.
            Volatile.Write(ref _firstPressTicks, now);
            Console.Error.WriteLine("\n[Interrupt] Press Ctrl+C again to cancel the agent.");
        }
        else
        {
            // Second press within the window: cancel the run.
            Volatile.Write(ref _firstPressTicks, 0);
            _cts.Cancel();
        }
    }

    public void Dispose()
    {
        Console.CancelKeyPress -= _handler;
    }
}
