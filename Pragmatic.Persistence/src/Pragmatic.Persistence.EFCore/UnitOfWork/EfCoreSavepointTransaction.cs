using Microsoft.EntityFrameworkCore.Storage;
using Pragmatic.Persistence.Repository;

namespace Pragmatic.Persistence.EFCore.UnitOfWork;

/// <summary>
///     A transaction begun while the context already had one: a savepoint inside the outer transaction.
/// </summary>
/// <remarks>
///     <para>
///         EF Core refuses a second transaction on a connection, so a <c>[Transactional]</c> action invoked inside another
///         failed before its body ran: an action composing others in one transaction could not itself be composed. It joins
///         the outer transaction instead. Its commit releases the savepoint and leaves the outcome to the outer transaction,
///         its rollback undoes only what was written since the savepoint, and disposing it leaves the outer transaction open.
///     </para>
///     <para>
///         ⚠️ A rollback to the savepoint undoes rows, not the change tracker: an entity saved inside it stays tracked as
///         unchanged. The outer work goes on with what it wrote itself; reading back what the inner work wrote is a query.
///     </para>
/// </remarks>
internal sealed class EfCoreSavepointTransaction(IDbContextTransaction outer, string savepoint) : ITransaction
{
    private bool _completed;

    /// <summary>The outer transaction's: this one is part of it.</summary>
    public Guid TransactionId => outer.TransactionId;

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken ct = default)
    {
        await outer.ReleaseSavepointAsync(savepoint, ct).ConfigureAwait(false);
        _completed = true;
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken ct = default)
    {
        // Twice is a no-op, as for the outer transaction: an invoker rolls back on a failed result and again on an
        // exception, and a savepoint already rolled back to is still there to return to.
        if (_completed)
            return;

        await outer.RollbackToSavepointAsync(savepoint, ct).ConfigureAwait(false);
        _completed = true;
    }

    /// <summary>Leaves the outer transaction open: it is the outer invocation's to end.</summary>
    public void Dispose()
    {
    }

    /// <summary>Leaves the outer transaction open: it is the outer invocation's to end.</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
