namespace Pragmatic.Persistence.Repository;

/// <summary>
///     Represents a database transaction.
/// </summary>
public interface ITransaction : IDisposable, IAsyncDisposable
{
    /// <summary>
    ///     Gets the transaction identifier.
    /// </summary>
    Guid TransactionId { get; }

    /// <summary>
    ///     Commits the transaction.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    ///     Rolls back the transaction.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task RollbackAsync(CancellationToken ct = default);
}
