using Pragmatic.Persistence.Repository;

namespace Pragmatic.Persistence.Documents.Pragmatic;

/// <summary>The unit of work's transaction: the session's, reporting its end back so <see cref="DocUnitOfWork.State" /> follows it.</summary>
internal sealed class DocUnitOfWorkTransaction(DocTransaction transaction, Action<bool> ended) : ITransaction
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

    /// <summary>Not committed: aborted, on the driver's synchronous calls (what a <c>using</c> rather than an <c>await using</c> leaves).</summary>
    public void Dispose() => transaction.Dispose();

    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
