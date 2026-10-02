using System.Collections.Concurrent;
using Pragmatic.Messaging.Diagnostics;

namespace Pragmatic.Messaging;

/// <summary>
///     In-memory dead letter store for development and testing.
///     Messages are stored in a thread-safe collection and lost on process restart.
/// </summary>
public sealed class InMemoryDeadLetterStore : IDeadLetterStore
{
    private readonly ConcurrentDictionary<Guid, DeadLetterMessage> _messages = new();

    /// <inheritdoc />
    public Task StoreAsync(DeadLetterMessage message, CancellationToken ct = default)
    {
        _messages[message.Id] = message;

        MessagingDiagnostics.DeadLettered.Add(1,
            new KeyValuePair<string, object?>("message.type", message.MessageType));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DeadLetterMessage>> GetAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<DeadLetterMessage> result = [.. _messages.Values];
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<DeadLetterMessage?> GetAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_messages.TryGetValue(id, out var message) ? message : null);

    /// <inheritdoc />
    public Task RemoveAsync(Guid id, CancellationToken ct = default)
    {
        _messages.TryRemove(id, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Returns the current count of dead-lettered messages.
    /// </summary>
    public int Count => _messages.Count;

    /// <summary>
    ///     Clears all dead-lettered messages. Useful in test scenarios.
    /// </summary>
    public void Clear() => _messages.Clear();
}
