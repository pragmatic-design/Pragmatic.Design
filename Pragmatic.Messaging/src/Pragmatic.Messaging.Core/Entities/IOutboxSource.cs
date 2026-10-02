namespace Pragmatic.Messaging.Entities;

/// <summary>
///     Abstraction over outbox storage per boundary DbContext.
///     The SG generates an implementation per <c>[EnableOutbox]</c> DbContext.
/// </summary>
public interface IOutboxSource
{
    /// <summary>
    ///     The boundary name this outbox belongs to.
    /// </summary>
    string BoundaryName { get; }

    /// <summary>
    ///     Retrieves pending (unprocessed) outbox messages, ordered by creation time.
    /// </summary>
    /// <param name="batchSize">Maximum number of messages to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(int batchSize, CancellationToken ct = default);

    /// <summary>
    ///     Marks a message as successfully processed.
    /// </summary>
    Task MarkProcessedAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>
    ///     Marks a message as failed with an error and increments retry count.
    /// </summary>
    Task MarkFailedAsync(Guid messageId, string error, CancellationToken ct = default);

    /// <summary>
    ///     Deletes delivered (<c>ProcessedAt != null</c>) rows older than <paramref name="olderThan"/>,
    ///     bounding the outbox table's growth. Called periodically by the purge service. The default is
    ///     a no-op so a custom source can opt out of retention; the EF Core source deletes in a set-based
    ///     statement.
    /// </summary>
    /// <returns>The number of rows deleted.</returns>
    Task<int> PurgeProcessedAsync(TimeSpan olderThan, CancellationToken ct = default) => Task.FromResult(0);

    /// <summary>
    ///     Read-only snapshot for health checks: the number of unprocessed rows and the creation time
    ///     of the oldest one. Unlike <see cref="GetPendingAsync"/> this NEVER claims/leases rows, so a
    ///     health probe cannot starve the delivery pump. The default returns an empty snapshot.
    /// </summary>
    Task<(int Pending, DateTimeOffset? OldestCreatedAt)> InspectPendingAsync(CancellationToken ct = default)
        => Task.FromResult((0, (DateTimeOffset?)null));

    /// <summary>
    ///     Read-only list of pending (unprocessed) rows for DISPLAY (the ops dashboard). Unlike
    ///     <see cref="GetPendingAsync"/> this NEVER claims/leases rows, so an open dashboard — which
    ///     auto-refreshes — cannot starve the delivery pump by holding leases. Ordered oldest-first and
    ///     capped at <paramref name="max"/>. The default returns an empty list: a source that cannot
    ///     list without leasing simply shows nothing rather than interfering with delivery.
    /// </summary>
    Task<IReadOnlyList<OutboxMessage>> PeekPendingAsync(int max, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<OutboxMessage>>([]);
}
