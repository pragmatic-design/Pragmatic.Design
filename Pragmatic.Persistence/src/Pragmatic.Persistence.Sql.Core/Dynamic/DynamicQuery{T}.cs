using System.Collections;
using System.Linq.Expressions;

namespace Pragmatic.Persistence.Sql.Dynamic;

/// <summary>
///     A query composed at runtime on <see cref="IQueryable{T}" />: System.Linq's operators build the tree
///     (a grid filter choosing operators from its options, a query assembled from conditions), this
///     provider keeps it, and the async terminal translates it with the rules the generator uses at
///     compile time, through the entity maps the generator registered. The translation is cached by the
///     tree's shape (<see cref="DynamicPlanner" />), so the second call with the same shape reads its
///     captured values and binds them into a prepared command like a compiled plan. Nothing is compiled
///     from the tree: it is only read.
/// </summary>
public sealed class DynamicQuery<T> : IOrderedQueryable<T>, IQueryProvider
{
    private readonly SqlSession _session;
    private readonly Expression _expression;

    internal DynamicQuery(SqlSession session, Expression expression)
    {
        _session = session;
        _expression = expression;
    }

    public SqlSession Session => _session;

    Type IQueryable.ElementType => typeof(T);

    Expression IQueryable.Expression => _expression;

    IQueryProvider IQueryable.Provider => this;

    IQueryable IQueryProvider.CreateQuery(Expression expression) => throw new NotSupportedException("The non-generic CreateQuery is not used by System.Linq's operators; build the chain with the generic ones.");

    IQueryable<TElement> IQueryProvider.CreateQuery<TElement>(Expression expression) => new DynamicQuery<TElement>(_session, expression);

    object? IQueryProvider.Execute(Expression expression) => throw Sync();

    TResult IQueryProvider.Execute<TResult>(Expression expression) => throw Sync();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => throw Sync();

    IEnumerator IEnumerable.GetEnumerator() => throw Sync();

    /// <summary>The SQL this chain runs on the session's dialect, for inspection.</summary>
    public string ToSql() => DynamicPlanner.Plan<T>(_session, _expression, "ToListAsync", null).Plan.RunnableSql(_session);

    internal ValueTask<List<T>> ToListAsync(CancellationToken ct) => Run("ToListAsync", null, (plan, query) => plan.ToListAsync(query, ct));

    internal ValueTask<T?> FirstOrDefaultAsync(LambdaExpression? predicate, CancellationToken ct) => Run("FirstOrDefaultAsync", predicate, (plan, query) => plan.FirstOrDefaultAsync(query, ct));

    internal ValueTask<T> FirstAsync(CancellationToken ct) => Run("FirstAsync", null, (plan, query) => plan.FirstAsync(query, ct));

    internal ValueTask<T> SingleAsync(CancellationToken ct) => Run("SingleAsync", null, (plan, query) => plan.SingleAsync(query, ct));

    internal ValueTask<T?> SingleOrDefaultAsync(CancellationToken ct) => Run("SingleOrDefaultAsync", null, (plan, query) => plan.SingleOrDefaultAsync(query, ct));

    internal ValueTask<int> CountAsync(LambdaExpression? predicate, CancellationToken ct) => Run("CountAsync", predicate, (plan, query) => plan.CountAsync(query, ct));

    internal ValueTask<bool> AnyAsync(LambdaExpression? predicate, CancellationToken ct) => Run("AnyAsync", predicate, (plan, query) => plan.AnyAsync(query, ct));

    internal ValueTask<TValue> ScalarAsync<TValue>(string terminal, LambdaExpression selector, CancellationToken ct)
    {
        var (plan, query) = DynamicPlanner.Plan<TValue>(_session, _expression, terminal, selector).Bind(_session);
        return plan.ScalarAsync(query, ct);
    }

    /// <summary>The rows streamed from the reader; the plan refuses a collection Include here (its loader needs the rows together).</summary>
    internal IAsyncEnumerable<T> Enumerate(CancellationToken ct)
    {
        var (plan, query) = DynamicPlanner.Plan<T>(_session, _expression, "AsAsyncEnumerable", null).Bind(_session);
        return plan.Enumerate(query, ct);
    }

    private ValueTask<TResult> Run<TResult>(string terminal, LambdaExpression? argument, Func<SqlPlan<T>, SqlQuery<T>, ValueTask<TResult>> execute)
    {
        var (plan, query) = DynamicPlanner.Plan<T>(_session, _expression, terminal, argument).Bind(_session);
        return execute(plan, query);
    }

    private static NotSupportedException Sync() => new($"A query on IQueryable<{typeof(T).Name}> runs with the async terminals (ToListAsync, CountAsync, …); it is never enumerated in memory.");
}
