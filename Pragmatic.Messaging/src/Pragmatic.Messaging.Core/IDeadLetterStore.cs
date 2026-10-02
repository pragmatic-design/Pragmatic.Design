namespace Pragmatic.Messaging;

/// <summary>
///     Persists messages that exhausted all retry attempts.
/// </summary>
public interface IDeadLetterStore
{
    /// <summary>
    ///     Stores a dead-lettered message.
    /// </summary>
    Task StoreAsync(DeadLetterMessage message, CancellationToken ct = default);

    /// <summary>
    ///     Retrieves all dead-lettered messages (for inspection/replay).
    /// </summary>
    Task<IReadOnlyList<DeadLetterMessage>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Retrieves one dead-lettered message by id, or null.</summary>
    Task<DeadLetterMessage?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>Removes a dead-lettered message (after replay or manual discard). Idempotent.</summary>
    Task RemoveAsync(Guid id, CancellationToken ct = default);
}
