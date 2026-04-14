using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DotCraft.Protocol;

/// <summary>
/// Thread-scoped event broker that fans out lifecycle events to multiple subscribers.
/// </summary>
internal sealed class ThreadEventBroker(string threadId)
{
    private readonly ConcurrentDictionary<long, Channel<SessionEvent>> _subscribers = new();
    private readonly Lock _recentEventsLock = new();
    private readonly Queue<SessionEvent> _recentEvents = [];
    private long _subscriberSequence;
    private int _eventSequence;

    private const int ReplayBufferSize = 32;

    /// <summary>
    /// Creates a turn-scoped event channel backed by this broker.
    /// </summary>
    public SessionEventChannel CreateTurnChannel(
        string turnId,
        Action<SessionEvent>? debugTap = null) =>
        new(threadId, turnId, NextEventId, Publish, debugTap);

    /// <summary>
    /// Publishes a thread-level lifecycle event.
    /// </summary>
    public void PublishThreadEvent(SessionEventType eventType, object payload)
    {
        Publish(new SessionEvent
        {
            EventId = NextEventId(),
            EventType = eventType,
            ThreadId = threadId,
            Timestamp = DateTimeOffset.UtcNow,
            Payload = payload
        });
    }

    /// <summary>
    /// Publishes a thread status change event.
    /// </summary>
    public void PublishThreadStatusChanged(ThreadStatus previousStatus, ThreadStatus newStatus)
    {
        PublishThreadEvent(
            SessionEventType.ThreadStatusChanged,
            new ThreadStatusChangedPayload
            {
                PreviousStatus = previousStatus,
                NewStatus = newStatus
            });
    }

    /// <summary>
    /// Subscribes to thread events until the returned async sequence is cancelled or completed.
    /// </summary>
    public IAsyncEnumerable<SessionEvent> SubscribeAsync(
        bool replayRecent = false,
        CancellationToken ct = default)
    {
        var subscriberId = Interlocked.Increment(ref _subscriberSequence);
        var channel = Channel.CreateUnbounded<SessionEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        if (replayRecent)
        {
            lock (_recentEventsLock)
            {
                _subscribers[subscriberId] = channel;
                foreach (var evt in _recentEvents)
                {
                    channel.Writer.TryWrite(evt);
                }
            }
        }
        else
        {
            _subscribers[subscriberId] = channel;
        }

        return ReadAllAsync(subscriberId, channel, ct);
    }

    private string NextEventId() =>
        $"evt_{Interlocked.Increment(ref _eventSequence):D4}";

    private void Publish(SessionEvent evt)
    {
        lock (_recentEventsLock)
        {
            _recentEvents.Enqueue(evt);
            while (_recentEvents.Count > ReplayBufferSize)
            {
                _recentEvents.Dequeue();
            }

            foreach (var subscriber in _subscribers.Values)
            {
                subscriber.Writer.TryWrite(evt);
            }
        }
    }

    private async IAsyncEnumerable<SessionEvent> ReadAllAsync(
        long subscriberId,
        Channel<SessionEvent> channel,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            {
                yield return evt;
            }
        }
        finally
        {
            _subscribers.TryRemove(subscriberId, out _);
            channel.Writer.TryComplete();
        }
    }
}
