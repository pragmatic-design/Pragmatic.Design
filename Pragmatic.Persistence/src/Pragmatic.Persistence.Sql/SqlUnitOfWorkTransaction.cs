using Pragmatic.Persistence.Repository;

namespace Pragmatic.Persistence.Sql;

/// <summary>The unit of work's transaction: the session's, reporting its end back so <see cref="SqlUnitOfWork.State" /> follows it.</summary>
internal sealed class SqlUnitOfWorkTransaction(System.Data.Common.DbTransaction transaction, Action<bool> ended) : ITransaction
{
    public Guid TransactionId { get; } = Guid.NewGuid();

    public async Task CommitAsync(CancellationToken ct = default)
    {
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        ended(true);
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        await transaction.RollbackAsync(ct).ConfigureAwait(false);
        ended(false);
    }

    public void Dispose() => transaction.Dispose();

    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
