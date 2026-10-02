using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Pragmatic.FeatureFlags.Providers;

/// <summary>
///     Fans <see cref="FeatureFlagChange"/> notifications out to every active watcher.
///     <para>
///         A single shared channel cannot do this: several watchers reading one reader <i>compete</i> for
///         messages, so each observes an arbitrary subset. Every watcher therefore gets its own channel,
///         and a publication is written to all of them.
///     </para>
///     <para>
///         Changes published while nobody is watching are held in a small pending buffer and delivered to
///         the first watcher that arrives — a flag defined during startup seeding is still observable by a
///         watcher started afterwards. The buffer is capped: once it is full the oldest entry is dropped,
///         so a process that never watches cannot grow without bound.
///     </para>
/// </summary>
internal sealed class FeatureFlagChangeBroadcaster
{
    /// <summary>Changes retained while there is no watcher. Small: a change means "re-read the flag".</summary>
    private const int PendingBufferCapacity = 64;

    /// <summary>Per-watcher backlog before the oldest change is dropped for that watcher.</summary>
    private const int WatcherBufferCapacity = 1024;

    private readonly ConcurrentDictionary<long, Channel<FeatureFlagChange>> _watchers = new();
    private readonly Queue<FeatureFlagChange> _pending = new();
    private readonly object _pendingGate = new();
    private long _nextWatcherId;

    public void Publish(FeatureFlagChange change)
    {
        if (_watchers.IsEmpty)
        {
            lock (_pendingGate)
            {
                // Re-check inside the lock: a watcher may have subscribed since, in which case the
                // change belongs to it rather than to the buffer.
                if (_watchers.IsEmpty)
                {
                    if (_pending.Count == PendingBufferCapacity)
                        _pending.Dequeue();
                    _pending.Enqueue(change);
                    return;
                }
            }
        }

        foreach (var watcher in _watchers.Values)
            watcher.Writer.TryWrite(change);
    }

    public async IAsyncEnumerable<FeatureFlagChange> WatchAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _nextWatcherId);
        var channel = Channel.CreateBounded<FeatureFlagChange>(
            new BoundedChannelOptions(WatcherBufferCapacity) { FullMode = BoundedChannelFullMode.DropOldest });

        lock (_pendingGate)
        {
            // Drain the backlog into this watcher before it becomes visible to Publish, so a change
            // cannot be delivered twice or slip between the two steps.
            while (_pending.Count > 0)
                channel.Writer.TryWrite(_pending.Dequeue());

            _watchers[id] = channel;
        }

        try
        {
            await foreach (var change in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                yield return change;
        }
        finally
        {
            _watchers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }

    /// <summary>Completes every watcher's stream — used when the owning store is disposed.</summary>
    public void Complete()
    {
        foreach (var watcher in _watchers.Values)
            watcher.Writer.TryComplete();
    }
}
