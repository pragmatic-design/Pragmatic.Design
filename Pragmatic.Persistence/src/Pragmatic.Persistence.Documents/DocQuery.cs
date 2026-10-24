namespace Pragmatic.Persistence.Documents;

/// <summary>
///     A query chain on a document collection, translated at compile time: every operator and terminal written on it is intercepted by the
///     generator (Pragmatic.Persistence.Documents.Generator), which turns the chain into the backend's query (a MongoDB <c>find</c>,
///     <c>count</c> or <c>aggregate</c>). The lambdas are read as source and never run, except a <c>Select</c>'s, which projects each document
///     read; a value of the caller goes in the two-parameter form (<c>Where((o, id) =&gt; o.CustomerId == id, id)</c>) and travels in a slot of
///     the chain. A call the generator did not translate throws <see cref="NotSupportedException" /> with the reason it reported at build.
/// </summary>
public readonly struct DocQuery<T>
{
    private readonly object?[]? _slots;

    internal DocQuery(DocSession session)
    {
        Session = session;
        _slots = null;
        SkipCount = -1;
        TakeCount = -1;
    }

    private DocQuery(DocSession session, object?[]? slots, int skip, int take)
    {
        Session = session;
        _slots = slots;
        SkipCount = skip;
        TakeCount = take;
    }

    public DocSession Session { get; }

    /// <summary>The documents to skip, or -1.</summary>
    public int SkipCount { get; }

    /// <summary>The documents to take, or -1.</summary>
    public int TakeCount { get; }

    /// <summary>The values the chain's operators stored, by slot (the generated plan reads them).</summary>
    public ReadOnlySpan<object?> Slots => _slots;

    /// <summary>The slots themselves, for the execution's async paths (a span cannot cross an await): never written after the chain built them (WithSlot copies).</summary>
    internal object?[] SlotArray => _slots ?? [];

    public object? Slot(int index) => _slots is not null && index < _slots.Length ? _slots[index] : null;

    /// <summary>The chain with <paramref name="value" /> in slot <paramref name="index" /> (generated code: an operator's argument).</summary>
    public DocQuery<T> WithSlot(int index, object? value)
    {
        var slots = new object?[Math.Max(index + 1, _slots?.Length ?? 0)];
        _slots?.CopyTo(slots, 0);
        slots[index] = value;
        return new DocQuery<T>(Session, slots, SkipCount, TakeCount);
    }

    /// <summary>The same chain over another element type (generated code: after a Select).</summary>
    public DocQuery<TOther> As<TOther>() => new(Session, _slots, SkipCount, TakeCount);

    /// <summary>The same chain without its Skip and Take (a join pages the joined rows, not the outer documents).</summary>
    internal DocQuery<TOther> Unpaged<TOther>() => new(Session, _slots, -1, -1);

    // Run as they are: the plan writes them as the query's skip and limit.

    public DocQuery<T> Skip(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new DocQuery<T>(Session, _slots, count, TakeCount);
    }

    public DocQuery<T> Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new DocQuery<T>(Session, _slots, SkipCount, TakeCount < 0 ? count : Math.Min(count, TakeCount));
    }

    // Intercepted: the generator replaces each call site.

    public DocQuery<T> Where(Func<T, bool> predicate) => throw NotTranslated(nameof(Where));

    /// <summary>
    ///     Each document the chain selects with each document of <paramref name="inner" /> (<c>session.Query&lt;TInner&gt;()</c> and its Wheres) whose
    ///     key matches, as <paramref name="result" /> builds them: then Skip, Take and ToListAsync. MongoDB joins on the server (<c>$lookup</c>);
    ///     Cosmos DB, which has no join between containers, reads the inner documents of the outer keys in a second query and joins them here.
    /// </summary>
    public DocQuery<TResult> Join<TInner, TKey, TResult>(DocQuery<TInner> inner, Func<T, TKey> outerKey, Func<TInner, TKey> innerKey, Func<T, TInner, TResult> result) => throw NotTranslated(nameof(Join));

    /// <summary>
    ///     The elements of an embedded list of each document the chain selects (<c>o => o.Lines</c>), in the documents' order: then Skip, Take
    ///     and ToListAsync. MongoDB unwinds the list in an aggregate, Cosmos DB joins it in the query.
    /// </summary>
    public DocQuery<TElement> SelectMany<TElement>(Func<T, IEnumerable<TElement>> selector) => throw NotTranslated(nameof(SelectMany));

    public DocQuery<T> Where<TArg>(Func<T, TArg, bool> predicate, TArg arg) => throw NotTranslated(nameof(Where));

    /// <summary>The documents a condition composed at runtime selects (a specification's compiled leaves): stored in the chain, joined to its conditions.</summary>
    public DocQuery<T> Matching(DocPredicate<T> predicate) => throw NotTranslated(nameof(Matching));

    /// <summary>The documents in an ordering composed at runtime (a declared query's sorts, a grid's sort): stored in the chain, in place of an OrderBy.</summary>
    public DocQuery<T> OrderedBy(DocOrdering<T> ordering) => throw NotTranslated(nameof(OrderedBy));

    public DocQuery<T> OrderBy<TKey>(Func<T, TKey> key) => throw NotTranslated(nameof(OrderBy));

    public DocQuery<T> OrderByDescending<TKey>(Func<T, TKey> key) => throw NotTranslated(nameof(OrderByDescending));

    public DocQuery<T> ThenBy<TKey>(Func<T, TKey> key) => throw NotTranslated(nameof(ThenBy));

    public DocQuery<T> ThenByDescending<TKey>(Func<T, TKey> key) => throw NotTranslated(nameof(ThenByDescending));

    /// <summary>
    ///     Keyset pagination: the documents after <paramref name="cursor" /> in the chain's ordering, the cursor being the last document's values
    ///     of the ordering's keys (one key: the value; more: a tuple, in order). The ordering should end with the key, or a page may skip or repeat.
    /// </summary>
    public DocQuery<T> After<TCursor>(TCursor cursor) => throw NotTranslated(nameof(After));

    /// <summary>
    ///     The documents grouped by a field, for a Select of the groups' key and aggregates (<c>g.Key</c>, <c>g.Count()</c>,
    ///     <c>g.Sum/Min/Max/Average(o => o.Field)</c>) into a new object: one MongoDB <c>$group</c> stage after the filter. Not on Cosmos DB.
    /// </summary>
    public DocQuery<IGrouping<TKey, T>> GroupBy<TKey>(Func<T, TKey> key) => throw NotTranslated(nameof(GroupBy));

    /// <summary>Each document read, projected by <paramref name="selector" /> (run in .NET); the document is read with only the fields it names.</summary>
    public DocQuery<TResult> Select<TResult>(Func<T, TResult> selector) => throw NotTranslated(nameof(Select));

    public ValueTask<List<T>> ToListAsync(CancellationToken ct = default) => throw NotTranslated(nameof(ToListAsync));

    public ValueTask<T?> FirstOrDefaultAsync(CancellationToken ct = default) => throw NotTranslated(nameof(FirstOrDefaultAsync));

    public ValueTask<T?> FirstOrDefaultAsync(Func<T, bool> predicate, CancellationToken ct = default) => throw NotTranslated(nameof(FirstOrDefaultAsync));

    public ValueTask<T?> FirstOrDefaultAsync<TArg>(Func<T, TArg, bool> predicate, TArg arg, CancellationToken ct = default) => throw NotTranslated(nameof(FirstOrDefaultAsync));

    public ValueTask<long> CountAsync(CancellationToken ct = default) => throw NotTranslated(nameof(CountAsync));

    public ValueTask<long> CountAsync(Func<T, bool> predicate, CancellationToken ct = default) => throw NotTranslated(nameof(CountAsync));

    public ValueTask<long> CountAsync<TArg>(Func<T, TArg, bool> predicate, TArg arg, CancellationToken ct = default) => throw NotTranslated(nameof(CountAsync));

    public ValueTask<bool> AnyAsync(CancellationToken ct = default) => throw NotTranslated(nameof(AnyAsync));

    public ValueTask<bool> AnyAsync(Func<T, bool> predicate, CancellationToken ct = default) => throw NotTranslated(nameof(AnyAsync));

    public ValueTask<bool> AnyAsync<TArg>(Func<T, TArg, bool> predicate, TArg arg, CancellationToken ct = default) => throw NotTranslated(nameof(AnyAsync));

    /// <summary>The largest value of a field among the documents (an <c>aggregate</c> with <c>$max</c>); none: <see cref="InvalidOperationException" />, as LINQ.</summary>
    public ValueTask<TValue> MaxAsync<TValue>(Func<T, TValue> selector, CancellationToken ct = default) => throw NotTranslated(nameof(MaxAsync));

    public ValueTask<TValue> MinAsync<TValue>(Func<T, TValue> selector, CancellationToken ct = default) => throw NotTranslated(nameof(MinAsync));

    /// <summary>The sum of a field among the documents (<c>$sum</c>); none: zero.</summary>
    public ValueTask<TValue> SumAsync<TValue>(Func<T, TValue> selector, CancellationToken ct = default) => throw NotTranslated(nameof(SumAsync));

    /// <summary>Deletes the documents the chain filters, in one command (MongoDB's <c>delete</c>, no limit): the count deleted. Not on Cosmos DB, which deletes by id only.</summary>
    public ValueTask<long> ExecuteDeleteAsync(CancellationToken ct = default) => throw NotTranslated(nameof(ExecuteDeleteAsync));

    /// <summary>
    ///     Sets fields of the documents the chain filters, in one command (MongoDB's <c>update</c> with <c>$set</c>, <c>multi</c>; a versioned
    ///     document's version incremented, so a copy read before is stale): the count matched. <c>s => s.Set(o => o.Status, Status.Paid)</c>.
    /// </summary>
    public ValueTask<long> ExecuteUpdateAsync(Action<DocSetters<T>> set, CancellationToken ct = default) => throw NotTranslated(nameof(ExecuteUpdateAsync));

    public ValueTask<long> ExecuteUpdateAsync<TArg>(Action<DocSetters<T>, TArg> set, TArg arg, CancellationToken ct = default) => throw NotTranslated(nameof(ExecuteUpdateAsync));

    private static NotSupportedException NotTranslated(string method)
        => new($"DocQuery.{method} was not translated at compile time: the generator reported why at the call site (PRAGD diagnostics). Is Pragmatic.Persistence.Documents.Generator referenced, and InterceptorsNamespaces set to Pragmatic.Persistence.Documents.Generated?");
}
