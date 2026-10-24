using System.Data.Common;

namespace Pragmatic.Persistence.Sql;

/// <summary>The chain's terminals: the reads, the scalars, the commands. Each call site is intercepted; a body reached at runtime was not translated.</summary>
public readonly partial struct SqlQuery<T>
{
    public ValueTask<List<T>> ToListAsync(CancellationToken ct = default) => throw NotTranslated(nameof(ToListAsync));

    /// <summary>
    ///     The rows, recorded on <paramref name="batch" /> instead of read now: several typed chains in one command (<c>batch.ExecuteAsync()</c>),
    ///     each answered by its item's <c>Result</c>. The same plan as <see cref="ToListAsync" />; FirstOrDefault, Count and Any likewise.
    /// </summary>
    public SqlBatchItem<List<T>> ToList(SqlBatch batch) => throw NotTranslated(nameof(ToList));

    public SqlBatchItem<T?> FirstOrDefault(SqlBatch batch) => throw NotTranslated(nameof(FirstOrDefault));

    public SqlBatchItem<int> Count(SqlBatch batch) => throw NotTranslated(nameof(Count));

    public SqlBatchItem<bool> Any(SqlBatch batch) => throw NotTranslated(nameof(Any));

    public ValueTask<T[]> ToArrayAsync(CancellationToken ct = default) => throw NotTranslated(nameof(ToArrayAsync));

    /// <summary>
    ///     The rows as a client asks for them (<see cref="SqlShape" />: a filter, a sort, a page, a subset of the members, the total): the
    ///     chain decides what the client may see, the shape how it looks at it. The shape's fields are the row type's members (the entity's
    ///     columns, or the projection's members); anything else is a <see cref="SqlShapeException" />. One command, the count included.
    /// </summary>
    public ValueTask<SqlShapeResult<T>> ShapeAsync(SqlShape shape, CancellationToken ct = default) => throw NotTranslated(nameof(ShapeAsync));

    /// <summary>
    ///     Streams the rows as they are read: no list, one row in memory at a time, whatever the count. The session runs
    ///     nothing else until the enumeration ends. Not with a collection Include, whose loader needs the rows together.
    /// </summary>
    public IAsyncEnumerable<T> AsAsyncEnumerable(CancellationToken ct = default) => throw NotTranslated(nameof(AsAsyncEnumerable));

    public ValueTask<T?> FirstOrDefaultAsync(CancellationToken ct = default) => throw NotTranslated(nameof(FirstOrDefaultAsync));

    public ValueTask<T?> FirstOrDefaultAsync(Func<T, bool> predicate, CancellationToken ct = default) => throw NotTranslated(nameof(FirstOrDefaultAsync));

    public ValueTask<T?> FirstOrDefaultAsync<TArg>(Func<T, TArg, bool> predicate, TArg arg, CancellationToken ct = default) => throw NotTranslated(nameof(FirstOrDefaultAsync));

    public ValueTask<T> FirstAsync(CancellationToken ct = default) => throw NotTranslated(nameof(FirstAsync));

    public ValueTask<T> SingleAsync(CancellationToken ct = default) => throw NotTranslated(nameof(SingleAsync));

    public ValueTask<T?> SingleOrDefaultAsync(CancellationToken ct = default) => throw NotTranslated(nameof(SingleOrDefaultAsync));

    public ValueTask<int> CountAsync(CancellationToken ct = default) => throw NotTranslated(nameof(CountAsync));

    public ValueTask<int> CountAsync(Func<T, bool> predicate, CancellationToken ct = default) => throw NotTranslated(nameof(CountAsync));

    public ValueTask<int> CountAsync<TArg>(Func<T, TArg, bool> predicate, TArg arg, CancellationToken ct = default) => throw NotTranslated(nameof(CountAsync));

    public ValueTask<bool> AnyAsync(CancellationToken ct = default) => throw NotTranslated(nameof(AnyAsync));

    public ValueTask<bool> AnyAsync(Func<T, bool> predicate, CancellationToken ct = default) => throw NotTranslated(nameof(AnyAsync));

    public ValueTask<bool> AnyAsync<TArg>(Func<T, TArg, bool> predicate, TArg arg, CancellationToken ct = default) => throw NotTranslated(nameof(AnyAsync));

    /// <summary>The rows keyed by a member: read as a list, keyed in memory.</summary>
    public ValueTask<Dictionary<TKey, T>> ToDictionaryAsync<TKey>(Func<T, TKey> key, CancellationToken ct = default) where TKey : notnull => throw NotTranslated(nameof(ToDictionaryAsync));

    public ValueTask<TValue> SumAsync<TValue>(Func<T, TValue> value, CancellationToken ct = default) => throw NotTranslated(nameof(SumAsync));

    /// <summary>The smallest value, or default when no row matches (SQL answers NULL).</summary>
    public ValueTask<TValue> MinAsync<TValue>(Func<T, TValue> value, CancellationToken ct = default) => throw NotTranslated(nameof(MinAsync));

    /// <summary>The largest value, or default when no row matches (SQL answers NULL).</summary>
    public ValueTask<TValue> MaxAsync<TValue>(Func<T, TValue> value, CancellationToken ct = default) => throw NotTranslated(nameof(MaxAsync));

    /// <summary>The average as a double, or 0 when no row matches.</summary>
    public ValueTask<double> AverageAsync<TValue>(Func<T, TValue> value, CancellationToken ct = default) => throw NotTranslated(nameof(AverageAsync));

    /// <summary>
    ///     Updates the rows the chain selects, in one statement: <c>s => s.SetProperty(o => o.Status, OrderStatus.Closed).SetProperty(o => o.Total, o => o.Total * 2)</c>.
    ///     A constant is a parameter; an expression over the row is translated. Returns the rows affected. No ordering, paging or navigation in the predicate.
    /// </summary>
    public ValueTask<int> ExecuteUpdateAsync(Func<SqlSet<T>, SqlSet<T>> set, CancellationToken ct = default) => throw NotTranslated(nameof(ExecuteUpdateAsync));

    /// <summary>The update with the caller's values as the argument: <c>ExecuteUpdateAsync((s, a) => s.SetProperty(o => o.Status, a.Status), (Status: status))</c>.</summary>
    public ValueTask<int> ExecuteUpdateAsync<TArg>(Func<SqlSet<T>, TArg, SqlSet<T>> set, TArg arg, CancellationToken ct = default) => throw NotTranslated(nameof(ExecuteUpdateAsync));

    /// <summary>Deletes the rows the chain selects, in one statement; returns the rows affected.</summary>
    public ValueTask<int> ExecuteDeleteAsync(CancellationToken ct = default) => throw NotTranslated(nameof(ExecuteDeleteAsync));

    /// <summary>
    ///     The update with the rows it changed read back in the same statement (<c>RETURNING</c>; <c>OUTPUT INSERTED</c> on SQL Server):
    ///     <paramref name="returning" /> is the row itself (<c>o => o</c>) or a DTO over its columns. A navigation in the predicate joins
    ///     the table (<c>UPDATE … FROM</c>), and a SET may read the joined row.
    /// </summary>
    public ValueTask<List<TResult>> ExecuteUpdateAsync<TResult>(Func<SqlSet<T>, SqlSet<T>> set, Func<T, TResult> returning, CancellationToken ct = default) => throw NotTranslated(nameof(ExecuteUpdateAsync));

    /// <summary>The delete with the rows it removed read back (<c>RETURNING</c>; <c>OUTPUT DELETED</c> on SQL Server).</summary>
    public ValueTask<List<TResult>> ExecuteDeleteAsync<TResult>(Func<T, TResult> returning, CancellationToken ct = default) => throw NotTranslated(nameof(ExecuteDeleteAsync));

    /// <summary>
    ///     <c>INSERT INTO target SELECT …</c>: a row of <typeparamref name="TTarget" /> (an entity) built from each row the chain selects, with an
    ///     object initializer over the row's columns and constants (<c>o => new Note { OrderId = o.Id, Text = "copied" }</c>); a key the database
    ///     assigns is left to it. The rows affected.
    /// </summary>
    public ValueTask<int> ExecuteInsertAsync<TTarget>(Func<T, TTarget> rows, CancellationToken ct = default) => throw NotTranslated(nameof(ExecuteInsertAsync));

    /// <summary>
    ///     Makes the rows the chain selects equal to <paramref name="rows" />, in one statement: a row with a known key is updated, a new
    ///     one inserted, a selected row not among them deleted (<c>MERGE … WHEN MATCHED / WHEN NOT MATCHED / WHEN NOT MATCHED BY SOURCE</c>;
    ///     an upsert and a delete on SQLite, which has no MERGE). The chain's predicates say which rows are the set (<c>Where((l, id) =>
    ///     l.OrderId == id, orderId).SyncAsync(lines)</c>: that order's lines). The rows affected.
    /// </summary>
    public ValueTask<int> SyncAsync(IReadOnlyList<T> rows, CancellationToken ct = default) => throw NotTranslated(nameof(SyncAsync));

    /// <summary>The SELECT statement this chain runs on its session's dialect.</summary>
    public string ToSql() => throw NotTranslated(nameof(ToSql));

    /// <summary>The command as it would run, parameters bound, not executed and not cached: for inspection and measurement.</summary>
    public DbCommand ToCommand() => throw NotTranslated(nameof(ToCommand));
}
