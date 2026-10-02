using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Pragmatic.Actions.Commit;
using Pragmatic.Events;
using Pragmatic.Result.EntityFrameworkCore;
using Pragmatic.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Persistence.EFCore.Diagnostics;
using Pragmatic.Identity;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Repository;
using Pragmatic.Telemetry;
using Pragmatic.Telemetry.Conventions;

namespace Pragmatic.Persistence.EFCore.UnitOfWork;

/// <summary>
///     EF Core implementation of <see cref="IUnitOfWork"/>.
///     Wraps a <see cref="DbContext"/> — does NOT own its lifecycle (DI manages the DbContext).
/// </summary>
public sealed partial class EfCoreUnitOfWork : IUnitOfWork
{
    private readonly DbContext _db;
    private readonly ILogger<EfCoreUnitOfWork> _logger;

    // Optional: a write that goes straight through a repository has no invoker to dispatch for it, and
    // this is who does it then. Scoped like this unit of work, so a handler runs with the tenant and the
    // user of the request that caused the write — which an interceptor could not offer, because it
    // would dispatch from a scope of its own.
    private readonly IDomainEventDispatcher? _dispatcher;

    // Tracks the lifecycle of the transaction started by this unit of work. Mutated by the
    // EfCoreTransaction this UoW hands out (via the commit/rollback callbacks below) so that
    // State/IsCommitted reflect reality without reaching into EF internals.
    private TransactionState _state = TransactionState.None;

    // [GeneratedValue] properties, one binding per property, empty in an application that declares
    // none. Applied here rather than in a SaveChanges interceptor: a sequence-backed format asks the
    // database for its next value, and by the time an interceptor runs the context's connection
    // belongs to the save in flight.
    private readonly IReadOnlyList<IGeneratedValueBinding> _generatedValues;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser? _currentUser;

    /// <param name="db">The DbContext managed by DI.</param>
    /// <param name="logger">Optional logger. When null, logging is suppressed.</param>
    /// <param name="dispatcher">
    ///     Optional. Dispatches the domain events of a write that no invoker is orchestrating; without
    ///     one those events are dropped, which is the same answer an application that registered no
    ///     dispatcher would get anywhere else.
    /// </param>
    /// <param name="generatedValues">
    ///     The [GeneratedValue] bindings the generator registered, filled before the save. Empty in an
    ///     application that declares none.
    /// </param>
    /// <param name="timeProvider">Clock handed to generators through the lifecycle context.</param>
    /// <param name="currentUser">Optional, for the lifecycle context's user id.</param>
    public EfCoreUnitOfWork(
        DbContext db,
        ILogger<EfCoreUnitOfWork>? logger = null,
        IDomainEventDispatcher? dispatcher = null,
        IEnumerable<IGeneratedValueBinding>? generatedValues = null,
        TimeProvider? timeProvider = null,
        ICurrentUser? currentUser = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? NullLogger<EfCoreUnitOfWork>.Instance;
        _dispatcher = dispatcher;
        _generatedValues = generatedValues is null
            ? []
            : generatedValues as IReadOnlyList<IGeneratedValueBinding> ?? [.. generatedValues];
        _timeProvider = timeProvider ?? TimeProvider.System;
        _currentUser = currentUser;
    }

    /// <summary>
    ///     Fills every empty <c>[GeneratedValue]</c> property on the rows about to be inserted.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Before EF is handed the save, so the connection is free: a <c>{SEQ}</c> format asks the
    ///         database for the next value, and doing that from inside the save is what hangs it.
    ///     </para>
    ///     <para>
    ///         A value already present is never overwritten and its generator is never called — which
    ///         matters beyond the value itself, because a sequence consumes a number every time it is
    ///         asked, and re-saving a row would leave a gap for nothing.
    ///     </para>
    /// </remarks>
    private async Task FillGeneratedValuesAsync(CancellationToken ct)
    {
        if (_generatedValues.Count == 0)
            return;

        var context = new LifecycleContext
        {
            Now = _timeProvider.GetUtcNow(),
            UserId = _currentUser?.IdOrNull()
        };

        foreach (var entry in _db.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added)
                continue;

            foreach (var binding in _generatedValues)
                await binding.TryFillAsync(entry.Entity, context, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Removes the pending events from every tracked entity holding any, and returns them.
    /// </summary>
    /// <remarks>
    ///     Called once, after a successful save, and it is the only place domain events leave an entity.
    ///     A second collector — an interceptor reading the same entries inside
    ///     <c>SavedChangesAsync</c> — would not agree with this one, because the lifecycle events are
    ///     raised in between.
    /// </remarks>
    private List<IDomainEvent> TakeDomainEvents()
    {
        var taken = new List<IDomainEvent>();

        foreach (var entry in _db.ChangeTracker.Entries<IHasDomainEvents>())
        {
            if (entry.Entity.DomainEvents.Count == 0)
                continue;

            taken.AddRange(entry.Entity.DomainEvents);
            entry.Entity.ClearDomainEvents();
        }

        return taken;
    }

    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        using var activity = PersistenceDiagnostics.ActivitySource.StartActivity("Persistence.SaveChanges");
        LogSaveChangesStarting();

        await FillGeneratedValuesAsync(ct).ConfigureAwait(false);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            int rowCount;
            try
            {
                // Inside the strategy, because an interceptor may open a transaction in SavingChanges — the
                // roll-up does, to apply its delta — and that is before EF's own strategy starts, where a
                // retrying one refuses it. Inside a unit already running (ExecuteAsync) this passes through.
                rowCount = await _db.Database.CreateExecutionStrategy()
                    .ExecuteAsync(token => _db.SaveChangesAsync(token), ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Not classified, on purpose. A stale row is two writers meeting, not a rule the
                // schema enforces, and the repository contract already answers it with
                // ConcurrencyError — which it can only do while this is still the exception EF threw.
                // Classifying it would land it on DbConflictError alongside a duplicate key, and
                // nothing downstream could tell a lost update from a violated unique index.
                throw;
            }
            catch (DbUpdateException ex)
            {
                // A unique index, a foreign key, a null or length constraint are rules the application
                // declared — [LogicKey], a relation, [Required] — and the database is only where they
                // are enforced. Letting the provider exception out makes a declared rule look like a
                // crash: a 500 with a stack trace, for something the application knew could happen.
                // The exception travels underneath, so nothing is hidden, only reframed.
                throw new PersistenceRuleViolationException(RuleViolationClassifier.Classify(_db, ex), ex);
            }

            // Taken only now, and only on success — a save that failed wrote nothing, and an event
            // announcing a write that did not happen is worse than one never sent.
            //
            // AFTER the save, not before, and that ordering is the whole point: LifecycleEventsInterceptor
            // raises the [Raises<T>] events during SavingChanges, so collecting them first found an empty
            // list every time and left them on the entity — to be dispatched by an interceptor from a
            // scope of its own, where no tenant is resolved and every fail-closed filter hides everything.
            var batch = CommitScope.CoveringBatch(this);

            // Nowhere to put them — no owner, and no dispatcher registered — so they are left where they
            // are. Taking them would clear the entity and drop them without a word, which is the one
            // outcome worse than not delivering: an application that has not asked for domain events
            // should not have them quietly destroyed on its behalf.
            var taken = batch is not null || _dispatcher is not null ? TakeDomainEvents() : [];

            if (batch is not null)
            {
                // Someone owns this commit: they flush after it, outside their own claim, so a handler
                // that writes is an operation in its own right. See CommitScope.Suspend.
                foreach (var @event in taken)
                    batch.DeferEvent(@event);
            }
            else if (taken.Count > 0 && _dispatcher is not null)
            {
                // Nobody is orchestrating this write — a save with no invoker above it. The events are
                // dispatched here, in this scope, which is the request's.
                //
                // Outside every claim, for the same reason the invokers step outside theirs: the commit
                // has happened, so a handler that writes through this same unit of work is its own
                // operation. Left inside, it reads as one more nested step, stages its writes and never
                // saves them. See CommitScope.Suspend.
                using var outsideTheCommit = CommitScope.Suspend();

                await _dispatcher.DispatchAsync(taken, ct).ConfigureAwait(false);
            }

            stopwatch.Stop();

            PersistenceDiagnostics.SaveChangesDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            if (rowCount > 0)
                PersistenceDiagnostics.RowsAffected.Add(rowCount);

            activity?.SetTag(DbTags.RowsAffected, rowCount);
            activity?.SetSuccess();

            LogSaveChangesCompleted(rowCount);
            return rowCount;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            stopwatch.Stop();
            PersistenceDiagnostics.SaveChangesDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            activity?.RecordException(ex);

            LogSaveChangesFailed(ex);

            throw;
        }
    }

    /// <inheritdoc />
    public void Add(object entity) => _db.Add(entity);

    /// <inheritdoc />
    public void Detach(object entity) => _db.Entry(entity).State = EntityState.Detached;

    /// <inheritdoc />
    public void DiscardChanges() => _db.ChangeTracker.Clear();

    /// <inheritdoc />
    /// <remarks>
    ///     Runs in the context's execution strategy. A retry starts from a cleared change tracker: the failed
    ///     attempt's entities are forgotten, as its transaction was rolled back, and the operation loads again
    ///     what it needs. The first attempt keeps what the caller had tracked before calling.
    /// </remarks>
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var attempts = 0;
        return _db.Database.CreateExecutionStrategy().ExecuteAsync(token =>
        {
            if (attempts++ > 0)
            {
                _db.ChangeTracker.Clear();
                _state = TransactionState.None;
            }

            return operation(token);
        }, ct);
    }

    /// <inheritdoc />
    public async Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        LogBeginTransactionStarting();
        var transaction = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        LogTransactionStarted(transaction.TransactionId);
        _state = TransactionState.Active;
        return new EfCoreTransaction(
            transaction,
            onCommitted: () => _state = TransactionState.Committed,
            onRolledBack: () => _state = TransactionState.RolledBack);
    }

    /// <inheritdoc />
    public TransactionState State => _state;

    /// <inheritdoc />
    public async Task SavepointAsync(string name, CancellationToken ct = default)
    {
        var current = _db.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Cannot create a savepoint: no active transaction. Call BeginTransactionAsync first.");

        await current.CreateSavepointAsync(name, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RollbackToSavepointAsync(string name, CancellationToken ct = default)
    {
        var current = _db.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "Cannot roll back to a savepoint: no active transaction.");

        await current.RollbackToSavepointAsync(name, ct).ConfigureAwait(false);
    }

    /// <summary>
    ///     No-op — DbContext lifecycle is managed by the DI container.
    /// </summary>
    public void Dispose()
    {
        // DbContext owns its lifecycle via DI
    }

    /// <summary>
    ///     No-op — DbContext lifecycle is managed by the DI container.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        // DbContext owns its lifecycle via DI
        return ValueTask.CompletedTask;
    }
}
