namespace Pragmatic.Persistence.Lifecycle;

/// <summary>
///     Ambient scope that <b>suppresses</b> the commit of every mutation invoked inside it, so several
///     can be persisted by one <c>SaveChanges</c> — performed by whoever opened the scope, not by this
///     type.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>It does not commit anything.</b> While active, <c>MutationInvoker</c> accumulates the
///         entity and defers its events instead of saving; on dispose the scope simply ends. Open one by
///         hand with nothing around it and three mutations will report success and write nothing —
///         measured, and pinned by <c>CommitCountTests</c>. The earlier summary here read "the whole
///         batch is persisted in a single SaveChanges", which is true only of the callers that do save.
///     </para>
///     <para>
///         The two that do: the generated <c>CompositeInvoker</c> of a <c>[CompositeAction]</c>, which
///         commits once after its steps, and the invoker of any action whose body opens a batch — its
///         own commit closes the scope's work. Outside a pipeline that commits, use the unit of work
///         directly instead.
///     </para>
///     <para>
///         <see cref="BulkOperationOptions.ChunkSize" /> is reserved and not yet honored.
///     </para>
/// </remarks>
public sealed class BatchContext : IDisposable
{
    private static readonly AsyncLocal<BatchContext?> CurrentContext = new();

    private readonly List<object> _deferredEvents = [];
    private readonly List<Func<IEnumerable<object>>> _pendingConstructions = [];
    private readonly List<object> _accumulatedEntities = [];
    private readonly object _lock = new();
    private readonly BatchContext? _previous;
    private bool _disposed;

    /// <summary>
    ///     Gets the current batch context, or null if not in a batch.
    /// </summary>
    public static BatchContext? Current => CurrentContext.Value;

    /// <summary>
    ///     Detaches the ambient batch for the life of the returned handle.
    /// </summary>
    /// <remarks>
    ///     For work that happens <em>after</em> the batch has been committed and must not be folded back
    ///     into it — post-commit event dispatch, above all. A handler that writes through the same unit
    ///     of work would otherwise be read as another step of an operation that has already finished:
    ///     staged, never saved, reported as succeeded.
    /// </remarks>
    public static AmbientSuspension SuspendAmbient()
    {
        var previous = CurrentContext.Value;
        CurrentContext.Value = null;

        return new AmbientSuspension(previous);
    }

    /// <summary>Restores the ambient batch that was current before <see cref="SuspendAmbient" />.</summary>
    public readonly struct AmbientSuspension(BatchContext? previous) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose() => CurrentContext.Value = previous;
    }

    /// <summary>
    ///     Options controlling chunk size and transaction behavior.
    /// </summary>
    public BulkOperationOptions Options { get; }

    /// <summary>
    ///     All deferred events accumulated during this batch.
    /// </summary>
    /// <remarks>
    ///     Reading this is what performs whatever <see cref="DeferEventConstruction" /> put off, once.
    /// </remarks>
    public IReadOnlyList<object> DeferredEvents
    {
        get
        {
            MaterializePendingConstructions();

            lock (_lock)
                return [.._deferredEvents];
        }
    }

    /// <summary>
    ///     All entities accumulated during this batch.
    /// </summary>
    public IReadOnlyList<object> AccumulatedEntities => _accumulatedEntities;

    /// <summary>
    ///     The unit of work whose commits this scope defers, or <c>null</c> for every one of them.
    /// </summary>
    /// <remarks>
    ///     Naming it is what keeps the suppression inside a boundary the opener can actually save. A
    ///     scope with no unit of work suppresses across boundaries, and the callee's <c>DbContext</c>
    ///     then has nobody to commit it — measured, three mutations reporting success and writing
    ///     nothing. It stays possible because the caller may genuinely own several, but it is a choice
    ///     now rather than the only shape available.
    /// </remarks>
    public Repository.IUnitOfWork? UnitOfWork { get; }

    /// <summary>
    ///     Whether this scope holds back the saves, or only the events.
    /// </summary>
    /// <remarks>
    ///     The two were the same thing until an explicit transaction needed them apart: there the steps
    ///     have to save — that is the whole reason for the transaction, so each can read the previous
    ///     one's writes — while their events must still wait for the commit, because a handler acting on
    ///     a change that then rolls back is a defect and not a trade-off.
    /// </remarks>
    public bool DefersSave { get; } = true;

    /// <summary>
    ///     Creates and activates a new batch context scope.
    /// </summary>
    /// <param name="unitOfWork">
    ///     The unit of work whose commits to defer. Pass the one you will save yourself.
    /// </param>
    /// <param name="options">Chunking options. Reserved — see the remarks on this type.</param>
    /// <param name="defersSave">
    ///     Whether to hold back the saves as well as the events. <c>false</c> inside an explicit
    ///     transaction: there the steps must save, so that each can read what the previous one wrote,
    ///     and only the dispatch has to wait for the commit.
    /// </param>
    public BatchContext(
        Repository.IUnitOfWork? unitOfWork = null,
        BulkOperationOptions? options = null,
        bool defersSave = true)
    {
        DefersSave = defersSave;
        UnitOfWork = unitOfWork;
        Options = options ?? new BulkOperationOptions();
        _previous = CurrentContext.Value;
        CurrentContext.Value = this;
    }

    /// <summary>Creates and activates a scope that defers every unit of work.</summary>
    /// <param name="options">Chunking options. Reserved — see the remarks on this type.</param>
    public BatchContext(BulkOperationOptions? options)
        : this(null, options)
    {
    }

    /// <summary>
    ///     Defers an event for later publishing when the batch completes.
    /// </summary>
    public void DeferEvent(object @event)
    {
        lock (_lock)
            _deferredEvents.Add(@event);
    }

    /// <summary>
    ///     Defers the <b>construction</b> of events, not the events: it runs when they are first read.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         For a payload that does not exist yet. While this scope holds the save back, a property
    ///         the save fills — a <c>[GeneratedValue("…{SEQ}…")]</c> number above all — is still empty
    ///         on the entity when the mutation returns, so an event built there carries the empty value
    ///         and the handler has no way to tell, because by the time it runs the row does have its
    ///         number. Putting off the construction moves it past the owner's commit.
    ///     </para>
    ///     <para>
    ///         Performed once, at the first read, and the results are kept: two readers must see the
    ///         same instances, or an event identified by a fresh <c>EventId</c> per read would stop
    ///         being recognisable as its own redelivery.
    ///     </para>
    /// </remarks>
    /// <param name="construct">Builds the events. Called once, after the commit.</param>
    public void DeferEventConstruction(Func<IEnumerable<object>> construct)
    {
        lock (_lock)
            _pendingConstructions.Add(construct);
    }

    /// <summary>
    ///     Runs what <see cref="DeferEventConstruction" /> put off, and empties the pending list.
    /// </summary>
    /// <remarks>
    ///     The callbacks run <b>outside</b> the lock — they are the caller's code, and one that read
    ///     the events back would deadlock on a lock we were still holding. Taking the pending list
    ///     under the lock is what keeps a concurrent reader from running them twice.
    /// </remarks>
    private void MaterializePendingConstructions()
    {
        List<Func<IEnumerable<object>>> pending;
        lock (_lock)
        {
            if (_pendingConstructions.Count == 0)
                return;

            pending = [.._pendingConstructions];
            _pendingConstructions.Clear();
        }

        var built = new List<object>();
        foreach (var construct in pending)
            built.AddRange(construct());

        lock (_lock)
            _deferredEvents.AddRange(built);
    }

    /// <summary>
    ///     Accumulates an entity for chunked saving.
    /// </summary>
    public void AccumulateEntity(object entity)
    {
        lock (_lock)
            _accumulatedEntities.Add(entity);
    }

    /// <summary>
    ///     Gets deferred events grouped by their runtime type.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>GetType()</c> is used here intentionally: this is the dispatch boundary that
    ///         maps already-materialized event objects to their concrete CLR type so the caller
    ///         can resolve the matching handler set. The events arrive as <see cref="object" />
    ///         (heterogeneous, produced across many call sites), so there is no generic dispatch
    ///         or source-generated discriminator available at this point — the concrete type is
    ///         only knowable at runtime. This is type-token grouping, not reflection over members,
    ///         so it does not violate the zero-reflection rule.
    ///     </para>
    ///     <para>
    ///         Allocations are minimized by grouping in a single pass into a pre-typed dictionary
    ///         of <see cref="List{T}" /> rather than going through LINQ <c>GroupBy</c>/<c>ToList</c>
    ///         (which allocates an intermediate grouping per key plus an enumerator chain).
    ///     </para>
    /// </remarks>
    public IReadOnlyDictionary<Type, IReadOnlyList<object>> GetEventsByType()
    {
        MaterializePendingConstructions();

        List<object> snapshot;
        lock (_lock)
            snapshot = [.._deferredEvents];

        var grouped = new Dictionary<Type, List<object>>(capacity: snapshot.Count);
        foreach (var @event in snapshot)
        {
            var type = @event.GetType();
            if (!grouped.TryGetValue(type, out var bucket))
            {
                bucket = [];
                grouped[type] = bucket;
            }

            bucket.Add(@event);
        }

        var result = new Dictionary<Type, IReadOnlyList<object>>(capacity: grouped.Count);
        foreach (var (type, bucket) in grouped)
            result[type] = bucket;

        return result;
    }

    /// <summary>
    ///     Restores the previous context scope.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        CurrentContext.Value = _previous;
    }
}
