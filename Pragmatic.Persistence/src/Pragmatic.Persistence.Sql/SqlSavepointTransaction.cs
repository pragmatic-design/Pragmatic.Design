using Pragmatic.Persistence.Repository;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     A transaction begun while the session already had one: a savepoint inside the outer transaction.
/// </summary>
/// <remarks>
///     <para>
///         The session refuses a second transaction, so a <c>[Transactional]</c> action invoked inside another failed before its body ran: an
///         action composing others in one transaction could not itself be composed. It joins the outer transaction instead, as
///         <c>EfCoreSavepointTransaction</c> does on EF Core. Its rollback undoes only what was written since the savepoint, and disposing it
///         leaves the outer transaction open.
///     </para>
///     <para>
///         Its commit leaves the outcome to the outer transaction and sends nothing: SQL Server and Oracle have no statement that releases a
///         savepoint, and on the other databases one left set ends with the transaction it lives in.
///     </para>
///     <para>
///         ⚠️ A rollback to the savepoint undoes rows, not the unit of work's tracking: an entity saved inside it stays tracked as unchanged,
///         as on EF Core.
///     </para>
/// </remarks>
internal sealed class SqlSavepointTransaction(SqlSession session, string savepoint) : ITransaction
{
    private bool _completed;

    public Guid TransactionId { get; } = Guid.NewGuid();

    public Task CommitAsync(CancellationToken ct = default)
    {
        _completed = true;
        return Task.CompletedTask;
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        // Twice is a no-op, as for the outer transaction: an invoker rolls back on a failed result and again on an exception.
        if (_completed)
            return;

        await session.RollbackToSavepointAsync(savepoint, ct).ConfigureAwait(false);
        _completed = true;
    }

    /// <summary>Leaves the outer transaction open: it is the outer invocation's to end.</summary>
    public void Dispose()
    {
    }

    /// <summary>Leaves the outer transaction open: it is the outer invocation's to end.</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
