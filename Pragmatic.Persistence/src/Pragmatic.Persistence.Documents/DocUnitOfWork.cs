using System.Net;
using Pragmatic.Events;
using Pragmatic.Identity;
using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Documents.Cosmos;
using Pragmatic.Persistence.Documents.Mongo;
using Pragmatic.Persistence.Engines;
using Pragmatic.Persistence.Entity;
using Pragmatic.Persistence.Lifecycle;
using Pragmatic.Persistence.Repository;

namespace Pragmatic.Persistence.Documents;

/// <summary>
///     Pragmatic's <see cref="IUnitOfWork" /> on the documents engine, over one <see cref="DocSession" />, as <c>SqlUnitOfWork</c> is on the Sql
///     engine: an identity map by <see cref="IEntity.PersistenceId" /> and a snapshot of each document's fields, the same phases in the same order
///     (<see cref="SavePhases" />, then the registered <see cref="IDocSavePhase" />s), the domain events after the commit. What the stores allow
///     differs (persistence/docs/write-surface.md): on MongoDB a save is one transaction where the server runs them (a replica set) and a
///     sequence of writes on a standalone server; on Cosmos DB it is a sequence of writes, never atomic across documents. There are no savepoints.
/// </summary>
public sealed partial class DocUnitOfWork : IUnitOfWork
{
    private const int Attempts = 3; // as the Sql engine's SqlTransactionOptions.Default
    private static readonly TimeSpan FirstDelay = TimeSpan.FromMilliseconds(50);
    private readonly List<DocEntry> _entries = [];
    private readonly Dictionary<(Type Type, Guid Key), DocEntry> _byKey = [];
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser? _currentUser;
    private readonly ITenantContext? _tenant;
    private readonly bool _requireTenant;
    private readonly IReadOnlyList<IGeneratedValueBinding> _generatedValues;
    private readonly IDomainEventDispatcher? _dispatcher;
    private readonly IReadOnlyList<IDocSavePhase> _phases;
    private readonly global::Pragmatic.Persistence.Scopes.DataScopeRule[] _scopeRules;
    private TransactionState _state;

    /// <param name="session">The session the unit of work reads and writes through; its filters are the caller's.</param>
    /// <param name="timeProvider">The clock of the stamps and the lifecycle context.</param>
    /// <param name="currentUser">Who the save is attributed to: the audit, the owner and the scope of new documents; none leaves them alone.</param>
    /// <param name="tenant">The tenant new documents belong to; none, or unresolved, leaves them as given.</param>
    /// <param name="requireTenant">Refuses a write of a tenant's entity with no tenant resolved (<c>MultiTenancyOptions.RequireTenant</c>).</param>
    /// <param name="generatedValues">The <c>[GeneratedValue]</c> bindings, filled on the added documents before the save.</param>
    /// <param name="dispatcher">Dispatches the domain events of a save no invoker orchestrates; without one they stay on the entities.</param>
    /// <param name="phases">The modules' phases (the audit log, the outboxes), after the unit of work's own.</param>
    /// <param name="scopeRules">The data scope rules registered (<c>AddDataScopeRule</c>): the Materialized and Hybrid ones are written on the saved documents.</param>
    public DocUnitOfWork(DocSession session, TimeProvider? timeProvider = null, ICurrentUser? currentUser = null, ITenantContext? tenant = null, bool requireTenant = false,
        IEnumerable<IGeneratedValueBinding>? generatedValues = null, IDomainEventDispatcher? dispatcher = null, IEnumerable<IDocSavePhase>? phases = null,
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

    public DocSession Session { get; }

    /// <summary>The entities tracked, in the order they were tracked.</summary>
    public IReadOnlyList<DocEntry> Entries => _entries;

    /// <summary>The entry of a tracked entity; null when it is not tracked.</summary>
    public DocEntry? EntryOf(object entity) => entity is IEntity keyed && _byKey.TryGetValue((entity.GetType(), keyed.PersistenceId), out var entry) && ReferenceEquals(entry.Entity, entity) ? entry : null;

    /// <summary>
    ///     A document read through the unit of work: tracked as unchanged with its snapshot, or, when an entity with its key is tracked already,
    ///     that one (the identity map: the values in memory win over the ones read, as on EF Core).
    /// </summary>
    public T Attach<T>(T entity) where T : class, IEntity
    {
        if (_byKey.TryGetValue((entity.GetType(), entity.PersistenceId), out var tracked))
            return (T)tracked.Entity;
        Track(new DocEntry(entity, DocEntryState.Unchanged));
        return entity;
    }

    /// <summary>The documents of a read, each attached as one is.</summary>
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
            if (ReferenceEquals(tracked.Entity, entity) && tracked.State == DocEntryState.Deleted)
            {
                tracked.State = DocEntryState.Unchanged; // removed then added back: the document stays, as it is now
                return;
            }

            throw new InvalidOperationException($"An entity {entity.GetType().Name} with key {keyed.PersistenceId} is tracked already: it is not new.");
        }

        Track(new DocEntry(entity, DocEntryState.Added));
    }

    /// <summary>
    ///     An entity to write whole (<c>IRepository.Update</c>): tracked already, nothing to do; not tracked, it is attached with every field as
    ///     changed, and the save first checks that the caller may see its document (<paramref name="visible" />).
    /// </summary>
    public void Update(object entity, Func<CancellationToken, Task<bool>> visible)
    {
        if (EntryOf(entity) is not null)
            return;
        var entry = Untracked(entity, visible);
        entry.ForgetSnapshot();
        entry.State = DocEntryState.Modified;
    }

    /// <summary>
    ///     An entity to delete (<c>IRepository.Remove</c>): a <see cref="ISoftDelete" /> one is marked (now, by the caller) and saved as a change of
    ///     its flags; any other is deleted. One that is not tracked is attached first, and the save checks the caller may see its document. Inside
    ///     <see cref="SoftDeleteScope.Suspend" /> a <see cref="ISoftDelete" /> one is deleted too: an erasure, as the save then writes it (EF Core's
    ///     <c>DbContext.Remove</c> under the same scope); without that, marking it here left nothing for the scope to change.
    /// </summary>
    public void Remove(object entity, Func<CancellationToken, Task<bool>> visible)
    {
        var entry = EntryOf(entity) ?? Untracked(entity, visible);
        if (entry.State == DocEntryState.Added)
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

        entry.State = DocEntryState.Deleted;
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

    private DocEntry Untracked(object entity, Func<CancellationToken, Task<bool>> visible)
    {
        var keyed = Keyed(entity);
        if (_byKey.TryGetValue((entity.GetType(), keyed.PersistenceId), out var other))
            throw new InvalidOperationException($"Another instance of {entity.GetType().Name} with key {keyed.PersistenceId} is tracked: change that one ({other.State}).");
        var entry = new DocEntry(entity, DocEntryState.Unchanged) { Visible = visible };
        Track(entry);
        return entry;
    }

    private void Track(DocEntry entry)
    {
        _entries.Add(entry);
        _byKey[(entry.Entity.GetType(), ((IEntity)entry.Entity).PersistenceId)] = entry;
    }

    private void Forget(DocEntry entry)
    {
        _entries.Remove(entry);
        _byKey.Remove((entry.Entity.GetType(), ((IEntity)entry.Entity).PersistenceId));
    }

    private static IEntity Keyed(object entity)
        => entity as IEntity ?? throw new ArgumentException($"{entity.GetType().Name} is not a Pragmatic entity (IEntity): the unit of work tracks documents by PersistenceId.", nameof(entity));

    /// <inheritdoc />
    public TransactionState State => _state;

    /// <summary>MongoDB where the server runs transactions (a replica set); refused on a standalone server and on Cosmos DB, with the reason.</summary>
    public async Task<ITransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        if (Session.Cosmos is not null)
            throw new NotSupportedException("Cosmos DB has no transaction across documents (a transactional batch in one partition is not translated by this version): each document is written on its own.");
        if (!await Session.SupportsTransactionsAsync(ct).ConfigureAwait(false))
            throw new NotSupportedException("This MongoDB server is standalone: it runs no transactions (a replica set does).");
        var transaction = Session.BeginTransaction();
        _state = TransactionState.Active;
        return new DocUnitOfWorkTransaction(transaction, committed => _state = committed ? TransactionState.Committed : TransactionState.RolledBack);
    }

    /// <inheritdoc />
    public Task SavepointAsync(string name, CancellationToken ct = default) => throw new NotSupportedException("The documents engine has no savepoints: MongoDB's transactions have none, Cosmos DB has no transaction.");

    /// <inheritdoc />
    public Task RollbackToSavepointAsync(string name, CancellationToken ct = default) => throw new NotSupportedException("The documents engine has no savepoints: MongoDB's transactions have none, Cosmos DB has no transaction.");

    /// <summary>
    ///     Runs the operation, again on a transient failure (MongoDB's <c>TransientTransactionError</c> label, Cosmos DB's 429 and 449), from a
    ///     cleared unit of work, as <c>SqlUnitOfWork.ExecuteAsync</c> does: three attempts, the delay doubling from 50 ms.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var delay = FirstDelay;
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
            catch (Exception e) when (attempt < Attempts && IsTransient(e))
            {
                // The operation's transaction is aborted by its own dispose, before this runs.
            }

            await Task.Delay(delay, ct).ConfigureAwait(false);
            delay += delay;
        }
    }

    /// <summary>A failure the store says may pass on another attempt.</summary>
    public static bool IsTransient(Exception e) => e switch
    {
        MongoCommandException command => command.HasLabel("TransientTransactionError"),
        CosmosException cosmos => cosmos.Status == HttpStatusCode.TooManyRequests || (int)cosmos.Status == 449,
        _ => false,
    };

    public void Dispose()
    {
        // The session is the caller's.
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
