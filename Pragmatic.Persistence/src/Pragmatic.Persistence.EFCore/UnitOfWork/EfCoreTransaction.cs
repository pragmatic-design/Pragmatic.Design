using Microsoft.EntityFrameworkCore.Storage;
using Pragmatic.Persistence.Repository;

namespace Pragmatic.Persistence.EFCore.UnitOfWork;

/// <summary>
///     EF Core implementation of <see cref="ITransaction"/>.
///     Wraps an <see cref="IDbContextTransaction"/>.
/// </summary>
public sealed class EfCoreTransaction : ITransaction
{
    private readonly IDbContextTransaction _transaction;
    private readonly Action? _onCommitted;
    private readonly Action? _onRolledBack;

    /// <param name="transaction">The wrapped EF Core transaction.</param>
    /// <param name="onCommitted">
    ///     Optional callback invoked after a successful commit, used by the owning unit of work to
    ///     observe transaction state.
    /// </param>
    /// <param name="onRolledBack">Optional callback invoked after a rollback.</param>
    public EfCoreTransaction(
        IDbContextTransaction transaction,
        Action? onCommitted = null,
        Action? onRolledBack = null)
    {
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        _onCommitted = onCommitted;
        _onRolledBack = onRolledBack;
    }

    /// <inheritdoc />
    public Guid TransactionId => _transaction.TransactionId;

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _transaction.CommitAsync(ct).ConfigureAwait(false);
        _onCommitted?.Invoke();
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken ct = default)
    {
        await _transaction.RollbackAsync(ct).ConfigureAwait(false);
        _onRolledBack?.Invoke();
    }

    /// <inheritdoc />
    public void Dispose()
        => _transaction.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
        => _transaction.DisposeAsync();
}
