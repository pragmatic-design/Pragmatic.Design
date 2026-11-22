using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pragmatic.Persistence.EFCore.Interceptors;

/// <summary>
///     The transactions of <see cref="CommitObserverInterceptor" />'s saves: their entries reported at the commit, forgotten at a rollback or a
///     failure, trimmed at a rollback to a savepoint.
/// </summary>
internal sealed class CommitObserverTransactionInterceptor(CommitObserverInterceptor saves) : DbTransactionInterceptor
{
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) => saves.TransactionCommitted(transaction);

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        saves.TransactionCommitted(transaction);
        return Task.CompletedTask;
    }

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => saves.TransactionEnded(transaction);

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        saves.TransactionEnded(transaction);
        return Task.CompletedTask;
    }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData) => saves.TransactionEnded(transaction);

    public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        saves.TransactionEnded(transaction);
        return Task.CompletedTask;
    }

    public override void CreatedSavepoint(DbTransaction transaction, TransactionEventData eventData) => saves.SavepointCreated(transaction);

    public override Task CreatedSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
    {
        saves.SavepointCreated(transaction);
        return Task.CompletedTask;
    }

    public override void ReleasedSavepoint(DbTransaction transaction, TransactionEventData eventData) => saves.SavepointReleased(transaction);

    public override Task ReleasedSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
    {
        saves.SavepointReleased(transaction);
        return Task.CompletedTask;
    }

    public override void RolledBackToSavepoint(DbTransaction transaction, TransactionEventData eventData) => saves.RolledBackToSavepoint(transaction);

    public override Task RolledBackToSavepointAsync(DbTransaction transaction, TransactionEventData eventData, CancellationToken cancellationToken = default)
    {
        saves.RolledBackToSavepoint(transaction);
        return Task.CompletedTask;
    }
}
