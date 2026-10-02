using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Hybrid;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     A HybridCache with no machinery: a dictionary, and nothing else.
/// </summary>
/// <remarks>
///     <para>
///         Substituted for the real one rather than wrapped around it. Wrapping added a lock and a list
///         append per operation, and that alone made the lost increments disappear — the window is
///         narrow enough that observing it closes it.
///     </para>
///     <para>
///         This separates two questions that the failing test cannot tell apart: whether
///         <c>HybridCacheStack</c>'s own serialisation is wrong, or whether the cache underneath does
///         something the stack does not expect. Given a trivially correct store, only the first can
///         still fail.
///     </para>
/// </remarks>
internal sealed class TrivialHybridCache : HybridCache
{
    private readonly ConcurrentDictionary<string, object?> _entries = new(StringComparer.Ordinal);

    public override async ValueTask<T> GetOrCreateAsync<TState, T>(
        string key, TState state, Func<TState, CancellationToken, ValueTask<T>> factory,
        HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(key, out var existing))
            return (T)existing!;

        // No stampede protection, no coalescing: the factory runs for whoever asks.
        var created = await factory(state, cancellationToken).ConfigureAwait(false);

        // Deliberately not stored — the stack calls this with writes disabled to probe for a hit, and
        // storing the default would turn a miss into a poisoned entry.
        return created;
    }

    public override ValueTask SetAsync<T>(
        string key, T value, HybridCacheEntryOptions? options = null, IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        _entries[key] = value;
        return default;
    }

    public override ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _entries.TryRemove(key, out _);
        return default;
    }

    public override ValueTask RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
        => default;
}
