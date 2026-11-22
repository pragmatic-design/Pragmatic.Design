using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Pragmatic.Persistence.Repository;

namespace Pragmatic.Persistence.EFCore.Interceptors;

/// <summary>
///     The application's <see cref="ICommitObserver" />s on EF Core: the entries a save writes, before it writes them (<c>SavingChanges</c>) and
///     once they are committed — at <c>SavedChanges</c> when the save ran in a transaction of its own, at the commit of the caller's transaction
///     otherwise (<see cref="CommitObserverTransactionInterceptor" />), never after a rollback, a rollback to a savepoint forgetting the entries
///     written after the latest savepoint still set (EF Core's events do not name it).
/// </summary>
/// <remarks>
///     Registered last among a boundary's interceptors, so the entries are as the other interceptors left them: a <c>[SoftDelete]</c> row's
///     delete is already its update. Owned types and shared-type entities (a many-to-many join) are parts of an entity, not entities, and are
///     not reported.
/// </remarks>
public sealed class CommitObserverInterceptor : SaveChangesInterceptor
{
    private readonly ICommitObserver[] _observers;
    private readonly Lock _gate = new();
    private readonly ConditionalWeakTable<DbContext, List<CommitEntry>> _saving = new();
    private readonly ConditionalWeakTable<DbTransaction, Pending> _pending = new();

    private CommitObserverInterceptor(ICommitObserver[] observers) => _observers = observers;

    /// <summary>The interceptors that report to <paramref name="observers" />; none when there are none, so a host without one pays nothing.</summary>
    public static IInterceptor[] For(IEnumerable<ICommitObserver> observers)
    {
        ArgumentNullException.ThrowIfNull(observers);
        var all = observers.ToArray();
        if (all.Length == 0)
            return [];
        var saves = new CommitObserverInterceptor(all);
        return [saves, new CommitObserverTransactionInterceptor(saves)];
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Saving(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Saving(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Saved(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Saved(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    private void Saving(DbContext? context)
    {
        if (context is null)
            return;
        var entries = new List<CommitEntry>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Metadata.IsOwned() || entry.Metadata.HasSharedClrType)
                continue;
            switch (entry.State)
            {
                case EntityState.Added:
                    entries.Add(new CommitEntry(entry.Entity, CommitEntryState.Added));
                    break;
                case EntityState.Modified:
                    entries.Add(new CommitEntry(entry.Entity, CommitEntryState.Modified));
                    break;
                case EntityState.Deleted:
                    entries.Add(new CommitEntry(entry.Entity, CommitEntryState.Deleted));
                    break;
            }
        }

        if (entries.Count == 0)
            return;
        foreach (var observer in _observers)
            observer.Writing(entries); // throwing here refuses the save before any statement runs
        lock (_gate)
            _saving.AddOrUpdate(context, entries);
    }

    private void Saved(DbContext? context)
    {
        if (context is null)
            return;
        List<CommitEntry>? entries;
        lock (_gate)
        {
            if (!_saving.TryGetValue(context, out entries))
                return;
            _saving.Remove(context);
            // In the caller's transaction: reported at its commit. Without one, EF Core's own transaction has committed already.
            if (context.Database.CurrentTransaction?.GetDbTransaction() is { } transaction)
            {
                _pending.GetOrCreateValue(transaction).Entries.AddRange(entries);
                return;
            }
        }

        Committed(entries);
    }

    private void Forget(DbContext? context)
    {
        if (context is null)
            return;
        lock (_gate)
            _saving.Remove(context);
    }

    private void Committed(List<CommitEntry> entries)
    {
        foreach (var observer in _observers)
            observer.Committed(entries);
    }

    /// <summary>The transaction committed: the entries its saves wrote, reported.</summary>
    internal void TransactionCommitted(DbTransaction transaction)
    {
        Pending? pending;
        lock (_gate)
        {
            if (!_pending.TryGetValue(transaction, out pending))
                return;
            _pending.Remove(transaction);
        }

        if (pending.Entries.Count > 0)
            Committed(pending.Entries);
    }

    /// <summary>The transaction rolled back or failed: its entries forgotten.</summary>
    internal void TransactionEnded(DbTransaction transaction)
    {
        lock (_gate)
            _pending.Remove(transaction);
    }

    // EF Core's transaction events do not name the savepoint, so the savepoints are kept as a stack: a rollback goes back to the latest one
    // still set, a release drops it. That is the order a unit of work nests them in.

    /// <summary>A savepoint set: how many entries the transaction had written by then.</summary>
    internal void SavepointCreated(DbTransaction transaction)
    {
        lock (_gate)
        {
            var pending = _pending.GetOrCreateValue(transaction);
            pending.Marks.Push(pending.Entries.Count);
        }
    }

    /// <summary>The latest savepoint released: its mark dropped, the entries kept.</summary>
    internal void SavepointReleased(DbTransaction transaction)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(transaction, out var pending))
                pending.Marks.TryPop(out _);
        }
    }

    /// <summary>A rollback to the latest savepoint, which stays set: the entries written after it forgotten.</summary>
    internal void RolledBackToSavepoint(DbTransaction transaction)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(transaction, out var pending) && pending.Marks.TryPeek(out var mark) && pending.Entries.Count > mark)
                pending.Entries.RemoveRange(mark, pending.Entries.Count - mark);
        }
    }

    private sealed class Pending
    {
        public List<CommitEntry> Entries { get; } = [];

        public Stack<int> Marks { get; } = new();
    }
}
