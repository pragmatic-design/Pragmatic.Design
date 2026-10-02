using System.Collections.Concurrent;

namespace Pragmatic.Messaging;

/// <summary>
///     In-memory idempotency store for development and single-instance scenarios.
///     Uses a ConcurrentDictionary with timestamp-based TTL.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _processed = new();

    /// <summary>
    ///     Claims in progress: id → when the lease runs out. A completed one moves to
    ///     <c>_processed</c> and leaves here, so the two states never overlap.
    /// </summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _inProgress = new();

    /// <inheritdoc />
    public Task<bool> TryMarkAsProcessedAsync(string messageId, CancellationToken ct = default)
    {
        var added = _processed.TryAdd(messageId, DateTimeOffset.UtcNow);
        return Task.FromResult(added);
    }

    /// <inheritdoc />
    public Task<MessageClaim> TryClaimAsync(string messageId, TimeSpan lease, CancellationToken ct = default)
    {
        if (_processed.ContainsKey(messageId))
            return Task.FromResult(MessageClaim.AlreadyHandled);

        var now = DateTimeOffset.UtcNow;
        var expires = now + lease;

        // Taken when nobody holds it, or when the holder's lease has run out. The update is
        // conditional on the value we read, so two workers racing on an expired claim cannot both win.
        while (true)
        {
            if (_inProgress.TryAdd(messageId, expires))
                return Task.FromResult(MessageClaim.Claimed);

            if (!_inProgress.TryGetValue(messageId, out var held))
                continue;

            if (held > now)
                return Task.FromResult(MessageClaim.HeldByAnother);

            if (_inProgress.TryUpdate(messageId, expires, held))
                return Task.FromResult(MessageClaim.Claimed);
        }
    }

    /// <inheritdoc />
    public Task MarkClaimCompletedAsync(string messageId, CancellationToken ct = default)
    {
        _processed[messageId] = DateTimeOffset.UtcNow;
        _inProgress.TryRemove(messageId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken ct = default)
        => Task.FromResult(_processed.ContainsKey(messageId));

    /// <inheritdoc />
    public Task RemoveAsync(string messageId, CancellationToken ct = default)
    {
        _processed.TryRemove(messageId, out _);
        _inProgress.TryRemove(messageId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PurgeOlderThanAsync(TimeSpan age, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - age;
        foreach (var kvp in _processed)
        {
            if (kvp.Value < cutoff)
                _processed.TryRemove(kvp.Key, out _);
        }

        return Task.CompletedTask;
    }

    /// <summary>Current count of tracked message IDs.</summary>
    public int Count => _processed.Count;
}
