namespace Pragmatic.Persistence.Repository;

/// <summary>
///     Unit of Work pattern interface for managing transactions.
/// </summary>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(global::Pragmatic.Composition.Attributes.Lifetime.Scoped)]
public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    /// <summary>
    ///     Saves all changes made in this unit of work to the database.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    ///     Adds an entity to the unit of work for insertion.
    ///     Used by preset providers to add child entities alongside the parent.
    /// </summary>
    /// <param name="entity">The entity to add.</param>
    /// <remarks>
    ///     The default implementation throws <see cref="NotSupportedException"/> rather than
    ///     silently no-op'ing: an implementation that does not support adding entities must say so
    ///     loudly. Implementations that participate in preset/child-entity insertion <b>must</b>
    ///     override this method.
    /// </remarks>
    /// <exception cref="NotSupportedException">
    ///     Thrown when the unit-of-work implementation does not override this method.
    /// </exception>
    void Add(object entity)
        => throw new NotSupportedException(
            $"{GetType().Name} does not support Add(object). " +
            "Override IUnitOfWork.Add to enable preset/child-entity insertion.");

    /// <summary>
    ///     Stops tracking an entity, so a save that failed does not leave it for the next one to retry.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A change tracker keeps what it was given whether the save succeeded or not. After a
    ///         failure the entity is still pending, so the <b>next</b> save — the caller's own, a later
    ///         step's, anything sharing this unit of work — tries to write it again and fails again.
    ///         An import committing row by row cannot report one bad row and carry on, because the bad
    ///         row comes back at every subsequent commit.
    ///     </para>
    ///     <para>
    ///         Measured, not reasoned about: a list of members with one repeated key committed the rows
    ///         before it and then answered 500, because the action's closing save met the duplicate for
    ///         a second time.
    ///     </para>
    ///     <para>
    ///         The default does nothing. A unit of work with no change tracker has nothing to forget,
    ///         and unlike <see cref="Add" /> a silent no-op here is correct rather than a hidden gap.
    ///     </para>
    /// </remarks>
    /// <param name="entity">The entity to stop tracking.</param>
    void Detach(object entity)
    {
        // Nothing to forget.
    }

    /// <summary>
    ///     Stops tracking everything, so an operation run again starts from what the store holds.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For a retry: a failed attempt leaves in the change tracker the rows it added and the changes
    ///         it made, and <see cref="Detach" /> forgets one entity. An attempt that ran again against
    ///         them would write the first attempt's children a second time, and apply its changes over a
    ///         row the first attempt had already changed. A mutation under <c>[ResiliencePolicy]</c> calls
    ///         this before every attempt after the first.
    ///     </para>
    ///     <para>
    ///         ⚠️ Everything, not only what the failed attempt tracked: an entity the caller read before
    ///         the operation is forgotten too, and is read again if it is needed.
    ///     </para>
    ///     <para>
    ///         The default does nothing, for the same reason as <see cref="Detach" />'s: a unit of work with
    ///         no change tracker has nothing to forget.
    ///     </para>
    /// </remarks>
    void DiscardChanges()
    {
        // Nothing to forget.
    }

    /// <summary>
    ///     Runs <paramref name="operation" /> as one retriable unit: the place a transaction is opened.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A database whose connection can drop — a restart, a failover, an idle timeout — is configured
    ///         to retry a transient failure, and a retrying strategy refuses a transaction opened outside it.
    ///         So whoever calls <see cref="BeginTransactionAsync" /> does it inside this.
    ///     </para>
    ///     <para>
    ///         <b>On a transient failure the whole operation runs again</b>, from a unit of work that has
    ///         forgotten everything the failed attempt tracked. What it wrote inside its transaction was rolled
    ///         back with it. What it did <b>outside</b> — an HTTP call, a file, a message sent directly rather
    ///         than through the outbox — happens again. An operation body must not have effects outside its
    ///         transaction.
    ///     </para>
    ///     <para>
    ///         Nested calls run inside the outermost one, once: a retry belongs to the unit that opened the
    ///         transaction. The default runs the operation once — a unit of work over a store with no
    ///         transient failures has nothing to retry.
    ///     </para>
    /// </remarks>
    /// <param name="operation">The unit: opens its transaction, does its work, commits.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <typeparam name="T">What the unit returns.</typeparam>
    /// <returns>What the last, successful run of <paramref name="operation" /> returned.</returns>
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        => operation(ct);

    /// <summary>
    ///     Begins a new database transaction.
    /// </summary>
    /// <remarks>
    ///     Inside <see cref="ExecuteAsync{T}" />: a retrying execution strategy refuses a transaction opened
    ///     outside it.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A disposable transaction object.</returns>
    Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>
    ///     Current lifecycle state of the ambient transaction. Lets callers branch (e.g. skip a
    ///     redundant commit, or decide whether a savepoint is meaningful) without catching
    ///     exceptions.
    /// </summary>
    /// <remarks>
    ///     The default implementation returns <see cref="TransactionState.None"/>; implementations
    ///     that own a transaction should override this to report the real state.
    /// </remarks>
    TransactionState State => TransactionState.None;

    /// <summary>
    ///     Convenience flag — <c>true</c> when <see cref="State"/> is
    ///     <see cref="TransactionState.Committed"/>.
    /// </summary>
    bool IsCommitted => State == TransactionState.Committed;

    /// <summary>
    ///     Creates a named savepoint within the current transaction so that a later
    ///     <see cref="RollbackToSavepointAsync"/> can undo work back to this point without aborting
    ///     the whole transaction.
    /// </summary>
    /// <param name="name">Savepoint name (unique within the transaction).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    ///     The default implementation throws <see cref="NotSupportedException"/> so stub/in-memory
    ///     units of work stay source-compatible. Implementations over a real, savepoint-capable
    ///     transaction (e.g. EF Core) must override this.
    /// </remarks>
    /// <exception cref="NotSupportedException">Thrown when savepoints are not supported.</exception>
    /// <exception cref="InvalidOperationException">May be thrown when no transaction is active.</exception>
    Task SavepointAsync(string name, CancellationToken ct = default)
        => throw new NotSupportedException(
            $"{GetType().Name} does not support savepoints. " +
            "Override IUnitOfWork.SavepointAsync on a transaction-capable implementation.");

    /// <summary>
    ///     Rolls the current transaction back to a previously created savepoint, discarding work
    ///     done after it while keeping the transaction open.
    /// </summary>
    /// <param name="name">Name of a savepoint created via <see cref="SavepointAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    ///     The default implementation throws <see cref="NotSupportedException"/>. Implementations
    ///     over a savepoint-capable transaction must override this.
    /// </remarks>
    /// <exception cref="NotSupportedException">Thrown when savepoints are not supported.</exception>
    /// <exception cref="InvalidOperationException">May be thrown when no transaction is active.</exception>
    Task RollbackToSavepointAsync(string name, CancellationToken ct = default)
        => throw new NotSupportedException(
            $"{GetType().Name} does not support savepoints. " +
            "Override IUnitOfWork.RollbackToSavepointAsync on a transaction-capable implementation.");
}
