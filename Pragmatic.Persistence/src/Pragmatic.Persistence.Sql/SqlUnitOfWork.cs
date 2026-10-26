using Pragmatic.Events;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Engines;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Repository;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     Pragmatic's <see cref="IUnitOfWork" /> on the Sql engine, over one <see cref="SqlSession" />. It tracks what it is given and what the
///     boundary's repositories read through it (an identity map by <see cref="IEntity.PersistenceId" />, a snapshot of each row's columns), so
///     code written for EF Core's change tracker saves the same way: load, change any property, save. <see cref="SaveChangesAsync" /> runs the
///     phases of Pragmatic's EF Core interceptors in their order (<see cref="SavePhases" />, then the registered <see cref="ISqlSavePhase" />s)
///     and writes every change in one batch, in one transaction; the domain events go out after the commit, as <c>EfCoreUnitOfWork</c>'s do.
/// </summary>
public sealed partial class SqlUnitOfWork : IUnitOfWork
{
    private readonly List<SqlEntry> _entries = [];
    private readonly Dictionary<(Type Type, Guid Key), SqlEntry> _byKey = [];
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser? _currentUser;
    private readonly ITenantContext? _tenant;
    private readonly bool _requireTenant;
    private readonly IReadOnlyList<IGeneratedValueBinding> _generatedValues;
    private readonly IDomainEventDispatcher? _dispatcher;
    private readonly IReadOnlyList<ISqlSavePhase> _phases;
    private readonly global::Pragmatic.Persistence.Scopes.DataScopeRule[] _scopeRules;
    private TransactionState _state;

    /// <param name="session">The session the unit of work reads and writes through; its filters are the caller's.</param>
    /// <param name="timeProvider">The clock of the stamps and the lifecycle context.</param>
    /// <param name="currentUser">Who the save is attributed to: the audit, the owner and the scope of new rows; none leaves them alone.</param>
    /// <param name="tenant">The tenant new rows belong to; none, or unresolved, leaves them as given.</param>
    /// <param name="requireTenant">Refuses a write of a tenant's entity with no tenant resolved (<c>MultiTenancyOptions.RequireTenant</c>).</param>
    /// <param name="generatedValues">The <c>[GeneratedValue]</c> bindings, filled on the added rows before the save.</param>
    /// <param name="dispatcher">Dispatches the domain events of a save no invoker orchestrates; without one they stay on the entities.</param>
    /// <param name="phases">The modules' phases (the audit log, the outboxes), after the unit of work's own.</param>
    /// <param name="scopeRules">The data scope rules registered (<c>AddDataScopeRule</c>): the Materialized and Hybrid ones are written on the saved rows.</param>
    public SqlUnitOfWork(SqlSession session, TimeProvider? timeProvider = null, ICurrentUser? currentUser = null, ITenantContext? tenant = null, bool requireTenant = false,
        IEnumerable<IGeneratedValueBinding>? generatedValues = null, IDomainEventDispatcher? dispatcher = null, IEnumerable<ISqlSavePhase>? phases = null,
        IEnumerable<global::Pragmatic.Persistence.Scopes.DataScopeRule>? scopeRules = null)
    {
        _scopeRules = scopeRules?.ToArray() ?? [];
        Session = session;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _currentUser = currentUser;
        _tenant = tenant;
        _requireTenant = requireTenant;
        _generatedValues = generatedValues?.ToArray() ?? [];
        _dispatcher = dispatcher;
        _phases = phases?.ToArray() ?? [];
    }

    public SqlSession Session { get; }

    /// <summary>The entities tracked, in the order they were tracked.</summary>
    public IReadOnlyList<SqlEntry> Entries => _entries;

    /// <summary>The entry of a tracked entity; null when it is not tracked.</summary>
    public SqlEntry? EntryOf(object entity) => entity is IEntity keyed && _byKey.TryGetValue((entity.GetType(), keyed.PersistenceId), out var entry) && ReferenceEquals(entry.Entity, entity) ? entry : null;

    /// <summary>
    ///     A row read through the unit of work: tracked as unchanged with its snapshot, or, when an entity with its key is tracked already, that
    ///     one (the identity map: the values in memory win over the ones read, as on EF Core). The children its collections were read with are
    ///     tracked with it (<c>SqlUnitOfWork.Graph.cs</c>).
    /// </summary>
    public T Attach<T>(T entity) where T : class, IEntity => (T)AttachRow(entity).Entity;

    /// <summary>The rows of a read, each attached as one row is.</summary>
    public List<T> AttachAll<T>(List<T> entities) where T : class, IEntity
    {
        for (var i = 0; i < entities.Count; i++)
            entities[i] = Attach(entities[i]);
        return entities;
    }

    /// <inheritdoc />
    public void Add(object entity)
    {
        var keyed = Keyed(entity);
        if (_byKey.TryGetValue((entity.GetType(), keyed.PersistenceId), out var tracked))
        {
            if (ReferenceEquals(tracked.Entity, entity) && tracked.State == SqlEntryState.Deleted)
            {
                tracked.State = SqlEntryState.Unchanged; // removed then added back: the row stays, as it is now
                return;
            }

            throw new InvalidOperationException($"An entity {entity.GetType().Name} with key {keyed.PersistenceId} is tracked already: it is not new.");
        }

        Track(new SqlEntry(entity, SqlEntryState.Added, Session.Dialect));
    }

    /// <summary>
    ///     An entity to write whole (<c>IRepository.Update</c>): tracked already, nothing to do (its changes are seen at the save); not tracked, it
    ///     is attached with every column as changed, and the save first checks that the caller may see its row (<paramref name="visible" />).
    /// </summary>
    public void Update(object entity, Func<CancellationToken, Task<bool>> visible)
    {
        if (EntryOf(entity) is not null)
            return;
        var entry = Untracked(entity, visible);
        entry.ForgetSnapshot();
        entry.State = SqlEntryState.Modified;
    }

    /// <summary>
    ///     An entity to delete (<c>IRepository.Remove</c>). A <see cref="ISoftDelete" /> one is marked, as Pragmatic's repository marks it (now, by
    ///     the caller), and saved as an update of its flags; any other is deleted. One that is not tracked is attached first, as it is now, and
    ///     the save checks that the caller may see its row (<paramref name="visible" />). Inside <see cref="SoftDeleteScope.Suspend" /> a
    ///     <see cref="ISoftDelete" /> one is deleted too: an erasure, as the save then writes it (EF Core's <c>DbContext.Remove</c> under the same
    ///     scope); without that, marking it here left nothing for the scope to change.
    /// </summary>
    public void Remove(object entity, Func<CancellationToken, Task<bool>> visible)
    {
        var entry = EntryOf(entity) ?? Untracked(entity, visible);
        if (entry.State == SqlEntryState.Added)
        {
            Forget(entry); // never written: nothing to delete
            return;
        }

        if (entity is ISoftDelete soft && !SoftDeleteScope.IsSuspended)
        {
            soft.IsDeleted = true;
            soft.DeletedAt = _timeProvider.GetUtcNow();
            soft.DeletedBy = _currentUser?.IdOrNull();
            return; // a modified entity, as the save's snapshot sees it
        }

        entry.State = SqlEntryState.Deleted;
    }

    /// <inheritdoc />
    public void Detach(object entity)
    {
        if (EntryOf(entity) is { } entry)
            Forget(entry);
    }

    /// <inheritdoc />
    public void DiscardChanges()
    {
        _entries.Clear();
        _byKey.Clear();
    }

    private SqlEntry Untracked(object entity, Func<CancellationToken, Task<bool>> visible)
    {
        var keyed = Keyed(entity);
        if (_byKey.TryGetValue((entity.GetType(), keyed.PersistenceId), out var other))
            throw new InvalidOperationException($"Another instance of {entity.GetType().Name} with key {keyed.PersistenceId} is tracked: change that one ({other.State}).");
        var entry = new SqlEntry(entity, SqlEntryState.Unchanged, Session.Dialect) { Visible = visible };
        Track(entry);
        return entry;
    }

    private void Track(SqlEntry entry)
    {
        _entries.Add(entry);
        _byKey[(entry.Entity.GetType(), ((IEntity)entry.Entity).PersistenceId)] = entry;
    }

    private void Forget(SqlEntry entry)
    {
        _entries.Remove(entry);
        _byKey.Remove((entry.Entity.GetType(), ((IEntity)entry.Entity).PersistenceId));
    }

    private static IEntity Keyed(object entity)
        => entity as IEntity ?? throw new ArgumentException($"{entity.GetType().Name} is not a Pragmatic entity (IEntity): the unit of work tracks rows by PersistenceId.", nameof(entity));

    /// <inheritdoc />
    public TransactionState State => _state;

    /// <inheritdoc />
    public Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        var transaction = Session.BeginTransaction();
        _state = TransactionState.Active;
        return Task.FromResult<ITransaction>(new SqlUnitOfWorkTransaction(transaction, committed => _state = committed ? TransactionState.Committed : TransactionState.RolledBack));
    }

    /// <inheritdoc />
    public Task SavepointAsync(string name, CancellationToken ct = default) => Session.SavepointAsync(name, ct);

    /// <inheritdoc />
    public Task RollbackToSavepointAsync(string name, CancellationToken ct = default) => Session.RollbackToSavepointAsync(name, ct);

    /// <summary>
    ///     Runs the operation, again on a transient failure (<see cref="SqlTransactionOptions" />' attempts and test), from a cleared unit of work:
    ///     the failed attempt's transaction rolled back, its entities forgotten, as EF Core's execution strategy does. The operation opens its
    ///     transaction itself, inside.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var options = SqlTransactionOptions.Default;
        var delay = options.Delay;
        for (var attempt = 1; ; attempt++)
        {
            if (attempt > 1)
            {
                DiscardChanges();
                _state = TransactionState.None;
            }

            try
            {
                return await operation(ct).ConfigureAwait(false);
            }
            catch (System.Data.Common.DbException e) when (attempt < options.Attempts && options.IsTransient(e))
            {
                // The operation's transaction is rolled back by its own dispose (ExecuteInTransactionAsync's using), before this runs.
            }

            await Task.Delay(delay, ct).ConfigureAwait(false);
            delay += delay;
        }
    }

    public void Dispose()
    {
        // The session is the caller's, as the DbContext is DI's for EfCoreUnitOfWork.
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
