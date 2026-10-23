using System.Collections;
using System.Data.Common;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace Pragmatic.Persistence.Sql;

/// <summary>The arguments a query chain received, one per operator that took one (<c>Where((o, arg) => …, arg)</c>), in chain order; read by the plan when it binds the parameters.</summary>
[InlineArray(SqlQuery.MaxSlots)]
public struct SqlSlots
{
    private object? _element0;
}

public static class SqlQuery
{
    /// <summary>How many argument-taking operators one chain may have; the generator reports a longer chain.</summary>
    public const int MaxSlots = 8;
}

/// <summary>The SET list of <see cref="SqlQuery{T}.ExecuteUpdateAsync" />, as the developer writes it: read by the generator, never run.</summary>
public sealed class SqlSet<T>
{
    /// <summary>The column takes a value (a parameter).</summary>
    public SqlSet<T> SetProperty<TValue>(Func<T, TValue> property, TValue value) => this;

    /// <summary>The column takes an expression over the row (<c>o => o.Total * 2</c>), translated; no captured values in it.</summary>
    public SqlSet<T> SetProperty<TValue>(Func<T, TValue> property, Func<T, TValue> expression) => this;
}

/// <summary>
///     A query chain as the developer writes it, translated at compile time. The struct carries only what
///     the generated plan cannot know until the call runs: the session, the arguments the operators received
///     (the values the predicates compare with) and the paging values. Everything else (the SQL per dialect,
///     the reader, the parameter types) is in the <see cref="SqlPlan{T}" /> the generator emits for each
///     terminal call site. A value type: the chain allocates nothing, and a base query that branches into a
///     count and a page stays untouched by construction.
///     The operators below are the shape LINQ binds to; each call site is intercepted, so a body reached
///     at runtime is a call site the generator did not translate, and says so. Every lambda is a plain
///     delegate, read as source by the generator and never invoked: no expression tree is built per call.
///     A predicate compares columns with constants, or with the members of the argument the two-parameter
///     form takes (<c>Where((o, id) => o.Id == id, id)</c>; several values travel as a tuple); a lambda that
///     captures a local of the enclosing method is reported at compile time with that remedy.
/// </summary>
public readonly partial struct SqlQuery<T> : IOrderedQueryable<T>, IQueryProvider, ISqlRoot
{
    private readonly SqlSession _session;
    private readonly SqlSlots _slots;
    private readonly int _skip; // -1: none
    private readonly int _take; // -1: none
    private readonly SqlRaw? _raw; // a hand-written statement as the root (session.Sql<T>), its values bound first

    internal SqlQuery(SqlSession session, SqlRaw? raw = null)
    {
        _session = session;
        _slots = default;
        _skip = -1;
        _take = -1;
        _raw = raw;
    }

    private SqlQuery(SqlSession session, in SqlSlots slots, int skip, int take, SqlRaw? raw)
    {
        _session = session;
        _slots = slots;
        _skip = skip;
        _take = take;
        _raw = raw;
    }

    public SqlSession Session => _session;

    /// <summary>The hand-written root statement and its values, when the chain starts at <c>session.Sql&lt;T&gt;</c>; null on a table.</summary>
    public SqlRaw? Raw => _raw;

    public SqlDialect Dialect => _session.Dialect;

    /// <summary>The OFFSET the chain asked for, or -1.</summary>
    public int SkipCount => _skip;

    /// <summary>The LIMIT the chain asked for, or -1.</summary>
    public int TakeCount => _take;

    /// <summary>An argument an operator received, by its position among the chain's argument-taking operators.</summary>
    public object? Slot(int index) => _slots[index];

    // The LINQ the developer writes: intercepted call sites. Skip and Take need no translation and run here.

    /// <summary>Filters on columns and constants: <c>o => o.Status == OrderStatus.Open &amp;&amp; !o.IsDeleted</c>. A value of the caller goes in the two-parameter form.</summary>
    public SqlQuery<T> Where(Func<T, bool> predicate) => throw NotTranslated(nameof(Where));

    /// <summary>
    ///     Filters with the caller's values as the argument: <c>Where((o, id) => o.Id == id, id)</c>, or several as a tuple,
    ///     <c>Where((o, a) => o.CustomerId == a.CustomerId &amp;&amp; o.CreatedAt > a.Since, (CustomerId: customerId, Since: since))</c>.
    ///     The argument is stored in the chain; the generated plan binds its members straight into the prepared command.
    /// </summary>
    public SqlQuery<T> Where<TArg>(Func<T, TArg, bool> predicate, TArg arg) => throw NotTranslated(nameof(Where));

    /// <summary>
    ///     Filters on a predicate composed at runtime from compiled leaves (<see cref="SqlPredicate{T}" />: a specification's, a declared
    ///     query's filters): the plan is compiled, the predicate's text is assembled from the leaves when the statement runs.
    /// </summary>
    public SqlQuery<T> Matching(SqlPredicate<T> predicate) => throw NotTranslated(nameof(Matching));

    /// <summary>
    ///     Orders by keys chosen at runtime from compiled columns (<see cref="SqlOrdering{T}" />: a declared query's sorts): the plan is compiled,
    ///     its ORDER BY is assembled from the keys when the statement runs.
    /// </summary>
    public SqlQuery<T> OrderedBy(SqlOrdering<T> ordering) => throw NotTranslated(nameof(OrderedBy));

    public SqlQuery<T> OrderBy<TKey>(Func<T, TKey> key) => throw NotTranslated(nameof(OrderBy));

    public SqlQuery<T> OrderByDescending<TKey>(Func<T, TKey> key) => throw NotTranslated(nameof(OrderByDescending));

    public SqlQuery<T> ThenBy<TKey>(Func<T, TKey> key) => throw NotTranslated(nameof(ThenBy));

    public SqlQuery<T> ThenByDescending<TKey>(Func<T, TKey> key) => throw NotTranslated(nameof(ThenByDescending));

    /// <summary>An ordering with the caller's value as the argument (<c>OrderBy((e, q) => Sql.CosineDistance(e.Vector, q), query)</c>: a k-NN): the value is a parameter of the ORDER BY, the plan stays one.</summary>
    public SqlQuery<T> OrderBy<TKey, TArg>(Func<T, TArg, TKey> key, TArg arg) => throw NotTranslated(nameof(OrderBy));

    public SqlQuery<T> OrderByDescending<TKey, TArg>(Func<T, TArg, TKey> key, TArg arg) => throw NotTranslated(nameof(OrderByDescending));

    public SqlQuery<T> ThenBy<TKey, TArg>(Func<T, TArg, TKey> key, TArg arg) => throw NotTranslated(nameof(ThenBy));

    public SqlQuery<T> ThenByDescending<TKey, TArg>(Func<T, TArg, TKey> key, TArg arg) => throw NotTranslated(nameof(ThenByDescending));

    /// <summary>
    ///     Keyset pagination: the rows after <paramref name="cursor" /> in the chain's ordering, where the cursor is the last row's
    ///     values of the ordering's keys, in order (<c>OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).After((last.CreatedAt, last.Id))</c>;
    ///     one key, the value itself). A row-value comparison (<c>(k1, k2) &gt; (@a, @b)</c>) where the dialect has it and the
    ///     directions agree, the expanded form (<c>k1 &gt; @a OR (k1 = @a AND k2 &gt; @b)</c>) otherwise. The ordering must be total
    ///     (end with the entity's key), or a cursor may skip or repeat rows: the generator warns (PRAG3009).
    /// </summary>
    public SqlQuery<T> After<TCursor>(TCursor cursor) => throw NotTranslated(nameof(After));

    /// <summary>
    ///     The page before a cursor: the rows nearest before <paramref name="cursor" /> in the chain's ordering, in that ordering
    ///     (<c>OrderBy(o => o.CreatedAt).ThenBy(o => o.Id).Before((first.CreatedAt, first.Id)).Take(20)</c> is the previous page of 20).
    ///     The statement compares the other way and orders in reverse, so the LIMIT takes the rows next to the cursor; the rows
    ///     are put back in the chain's order once read. <c>FirstOrDefaultAsync</c> after it is the row just before the cursor.
    ///     The cursor's shape and the ordering's rules are <see cref="After{TCursor}" />'s.
    /// </summary>
    public SqlQuery<T> Before<TCursor>(TCursor cursor) => throw NotTranslated(nameof(Before));

    public SqlQuery<TResult> Select<TResult>(Func<T, TResult> projection) => throw NotTranslated(nameof(Select));

    /// <summary>
    ///     Groups by a column; the Select that follows reads <c>g.Key</c> and the aggregates (<c>g.Count()</c>,
    ///     <c>g.Sum(x => x.Col)</c>, Min, Max, Average) into a DTO or an anonymous object: one GROUP BY statement.
    /// </summary>
    public SqlQuery<IGrouping<TKey, T>> GroupBy<TKey>(Func<T, TKey> key) => throw NotTranslated(nameof(GroupBy));

    /// <summary>A declared projection: the DTO's properties name the columns (or carry <see cref="MapPropertyAttribute" />); no lambda, no tree.</summary>
    public SqlQuery<TResult> Project<TResult>() => throw NotTranslated(nameof(Project));

    /// <summary>SELECT DISTINCT: the rows (or the projected values) each once. A Count after it counts the distinct rows.</summary>
    public SqlQuery<T> Distinct() => throw NotTranslated(nameof(Distinct));

    /// <summary>
    ///     One row per <paramref name="key" />, the first by <paramref name="order" /> (<c>Sql.Desc</c>, <c>Sql.By</c> for several): <c>DISTINCT ON</c> on
    ///     PostgreSQL, <c>ROW_NUMBER() OVER (PARTITION BY key ORDER BY order) = 1</c> elsewhere, as a derived table the rest of the chain
    ///     reads. The filters before it pick the candidates (the entity's visibility rules among them); it goes before the joins,
    ///     the includes and the projection.
    /// </summary>
    public SqlQuery<T> DistinctOn<TKey, TOrder>(Func<T, TKey> key, Func<T, TOrder> order) => throw NotTranslated(nameof(DistinctOn));

    /// <summary>
    ///     The rows of one derived type of a hierarchy in one table (<c>[Inheritance]</c> on <typeparamref name="T" />), read as that type:
    ///     the derived type's predicate on the discriminator, the same statement <c>Query&lt;TDerived&gt;()</c> builds. Before the joins.
    /// </summary>
    public SqlQuery<TDerived> OfType<TDerived>() where TDerived : T => throw NotTranslated(nameof(OfType));

    /// <summary>
    ///     UNION with another chain on the same entity (or the same projection of it): the rows of both, each once. The other chain
    ///     is written in place (<c>a.Union(session.Query&lt;Order&gt;().Where(…))</c>), its arguments travel in this chain's slots. Neither
    ///     member orders or pages; OrderBy, Skip, Take and Distinct after the operation apply to the result.
    /// </summary>
    public SqlQuery<T> Union(SqlQuery<T> other) => throw NotTranslated(nameof(Union));

    /// <summary>UNION ALL: the rows of both chains, duplicates kept.</summary>
    public SqlQuery<T> Concat(SqlQuery<T> other) => throw NotTranslated(nameof(Concat));

    /// <summary>EXCEPT: this chain's rows that are not in the other's.</summary>
    public SqlQuery<T> Except(SqlQuery<T> other) => throw NotTranslated(nameof(Except));

    /// <summary>INTERSECT: the rows in both chains.</summary>
    public SqlQuery<T> Intersect(SqlQuery<T> other) => throw NotTranslated(nameof(Intersect));

    /// <summary>
    ///     An explicit INNER JOIN on another entity's chain, where no navigation exists: <c>orders.Join(session.Query&lt;Customer&gt;(), o => o.CustomerId, c => c.Id, (o, c) => new { o.Number, c.Name })</c>.
    ///     The keys are columns (several as an anonymous object or a tuple); the result selector projects columns of both rows and
    ///     ends the chain like a Select (Skip, Take, Distinct and the terminals follow). The inner chain filters its rows with Where
    ///     and nothing else; its arguments travel in this chain's slots. One join per chain: the next one hangs off a navigation, or
    ///     the join's result is a <c>[Query]</c> the caller composes.
    /// </summary>
    public SqlQuery<TResult> Join<TInner, TKey, TResult>(SqlQuery<TInner> inner, Func<T, TKey> outerKey, Func<TInner, TKey> innerKey, Func<T, TInner, TResult> result) => throw NotTranslated(nameof(Join));

    /// <summary>A LEFT JOIN: the rows without a match keep their place, the inner's columns read as null (declare the projected members nullable).</summary>
    public SqlQuery<TResult> LeftJoin<TInner, TKey, TResult>(SqlQuery<TInner> inner, Func<T, TKey> outerKey, Func<TInner, TKey> innerKey, Func<T, TInner, TResult> result) => throw NotTranslated(nameof(LeftJoin));

    /// <summary>
    ///     LINQ's GroupJoin, as the first half of a LEFT JOIN: the group is not a value, the <c>SelectMany</c> that follows
    ///     (<c>x => x.Group.DefaultIfEmpty()</c>: LEFT; <c>x => x.Group</c>: INNER) joins the inner row and names the result.
    /// </summary>
    public SqlQuery<TResult> GroupJoin<TInner, TKey, TResult>(SqlQuery<TInner> inner, Func<T, TKey> outerKey, Func<TInner, TKey> innerKey, Func<T, IEnumerable<TInner>, TResult> result) => throw NotTranslated(nameof(GroupJoin));

    /// <summary>
    ///     A row per element of a collection of the row: a GroupJoin's group (its join), a collection navigation (<c>o => o.Lines</c>: a JOIN
    ///     on the foreign key), or a chain correlated to the row (<c>o => session.Query&lt;OrderLine&gt;().Where(l => l.OrderId == o.Id).OrderBy(…).Take(3)</c>:
    ///     LATERAL / APPLY, emulated with ROW_NUMBER where the dialect has none). <c>.DefaultIfEmpty()</c> on the collection keeps the rows without elements (LEFT).
    /// </summary>
    public SqlQuery<TResult> SelectMany<TCollection, TResult>(Func<T, IEnumerable<TCollection>> collection, Func<T, TCollection, TResult> result) => throw NotTranslated(nameof(SelectMany));

    // Subqueries: a chain written inside another chain's lambda (a predicate, a projection), ending in one of these. The generator
    // renders it as a correlated subquery of the one statement (EXISTS, COUNT, MIN…, IN); the inner lambdas may read the outer row.
    // Never called: a chain reaching one of these at runtime was not translated.

    /// <summary>Inside a lambda: EXISTS (…), with or without a predicate on the inner row.</summary>
    public bool Any() => throw NotTranslated(nameof(Any));

    public bool Any(Func<T, bool> predicate) => throw NotTranslated(nameof(Any));

    /// <summary>Inside a lambda: (SELECT COUNT(*) …).</summary>
    public int Count() => throw NotTranslated(nameof(Count));

    public int Count(Func<T, bool> predicate) => throw NotTranslated(nameof(Count));

    /// <summary>Inside a lambda: (SELECT MIN(value) …); null when no row matches, as SQL says (read it into a nullable member).</summary>
    public TValue Min<TValue>(Func<T, TValue> value) => throw NotTranslated(nameof(Min));

    public TValue Max<TValue>(Func<T, TValue> value) => throw NotTranslated(nameof(Max));

    public TValue Sum<TValue>(Func<T, TValue> value) => throw NotTranslated(nameof(Sum));

    public double Average<TValue>(Func<T, TValue> value) => throw NotTranslated(nameof(Average));

    /// <summary>Inside a lambda, after a Select of one column: <c>value IN (SELECT column …)</c>.</summary>
    public bool Contains(T value) => throw NotTranslated(nameof(Contains));

    /// <summary>
    ///     Loads a navigation with the rows: a to-one through the JOIN the plan already has, a collection with a second query
    ///     on the keys read. A member path goes deeper through to-one navigations (<c>l => l.Order!.Customer</c>: two joins);
    ///     <see cref="SqlPath" /> <c>Then</c> goes on below a collection or a navigation (<c>c => c.Orders.Then(o => o.Lines)</c>,
    ///     <c>o => o.Customer.Then(c => c.Orders)</c>), to any depth: the chain is a struct, so this stands where EF Core has ThenInclude.
    /// </summary>
    public SqlQuery<T> Include<TNavigation>(Func<T, TNavigation> navigation) => throw NotTranslated(nameof(Include));

    /// <summary>Reads past the entity's <see cref="VisibleWhenAttribute{TRule}" /> rules: the plan leaves their predicates out.</summary>
    public SqlQuery<T> WithoutFilters() => this;

    /// <summary>Reads past one of the entity's rules (<typeparamref name="TRule" />, a <see cref="VisibleWhenAttribute{TRule}" /> of it): the others stay.</summary>
    public SqlQuery<T> WithoutFilter<TRule>() where TRule : class => this;

    /// <summary>A [SoftDelete] entity's deleted rows too (the rules stay); the chain's includes still read the live children.</summary>
    public SqlQuery<T> IncludeDeleted() => this;

    /// <summary>
    ///     The attachments of an owner type (the repository's <c>[PolymorphicAttachment]</c>: <c>OwnerType</c> holds the owner's full type name):
    ///     <c>WHERE t."OwnerType" = 'Namespace.Owner'</c>, the name written by the generator from <typeparamref name="TOwner" />.
    /// </summary>
    public SqlQuery<T> ForOwner<TOwner>() where TOwner : class => this;

    /// <summary>
    ///     Locks the rows the statement reads, for the transaction: <c>FOR UPDATE</c> on PostgreSQL, <c>WITH (UPDLOCK, ROWLOCK)</c> on SQL
    ///     Server; <see cref="SqlLockMode.SkipLocked" /> leaves the rows another transaction holds to it (a work queue), <see cref="SqlLockMode.NoWait" />
    ///     fails instead of waiting. SQLite locks the database as a whole: nothing to add there (reported as an emulation, PRAG3012).
    ///     Before the terminal, on a chain that returns rows (not an aggregate, a group or a set operation). SQL Server locks every row
    ///     it reads to order a page: a queue there orders by the key or an indexed column, or it would hold every candidate row.
    /// </summary>
    public SqlQuery<T> ForUpdate(SqlLockMode mode = SqlLockMode.Wait) => this;

    /// <summary>Nothing tracks here; accepted so code written against EF Core keeps compiling.</summary>
    public SqlQuery<T> AsNoTracking() => this;

    /// <summary>
    ///     The rows as they were at <paramref name="utcInstant" />: SQL Server's system-versioned table (<c>FOR SYSTEM_TIME AS OF</c>, the period in
    ///     UTC datetime2). PostgreSQL and SQLite have no system-versioned tables: the current rows are read there, and the generator says so (PRAG3012).
    /// </summary>
    public SqlQuery<T> AsOf(DateTime utcInstant) => throw NotTranslated(nameof(AsOf));

    public SqlQuery<T> Skip(int count) => new(_session, _slots, (_skip < 0 ? 0 : _skip) + count, _take, _raw);

    public SqlQuery<T> Take(int count) => new(_session, _slots, _skip, _take < 0 ? count : Math.Min(_take, count), _raw);

    // What the generated interceptors call.

    /// <summary>The same chain with an operator's argument stored at its slot (a value type is boxed here, once per call).</summary>
    public SqlQuery<T> WithSlot(int index, object? arg)
    {
        var slots = _slots;
        slots[index] = arg;
        return new(_session, slots, _skip, _take, _raw);
    }

    /// <summary>The same chain with another chain's first <paramref name="count" /> slots copied in from <paramref name="offset" />: a set operation's member or a join's inner chain brings its arguments along.</summary>
    public SqlQuery<T> WithSlots<TOther>(in SqlQuery<TOther> other, int offset, int count)
    {
        var slots = _slots;
        for (var i = 0; i < count; i++)
            slots[offset + i] = other._slots[i];
        return new(_session, slots, _skip, _take, _raw);
    }

    /// <summary>The same chain seen as a projection: the plan's reader shapes the rows, the slots, paging and root stay.</summary>
    public SqlQuery<TResult> As<TResult>() => SqlQuery<TResult>.Of(_session, _slots, _skip, _take, _raw);

    internal static SqlQuery<T> Of(SqlSession session, in SqlSlots slots, int skip, int take, SqlRaw? raw) => new(session, slots, skip, take, raw);

    // IQueryable: the runtime path. A chain typed as IQueryable<T> binds to System.Linq's operators, which build a tree
    // through this provider; its async terminals (QueryableExtensions) translate the tree at runtime (DynamicQuery<T>).

    Type ISqlRoot.EntityType => typeof(T);

    SqlRaw? ISqlRoot.Raw => _raw;

    Type IQueryable.ElementType => typeof(T);

    Expression IQueryable.Expression => Expression.Constant(this, typeof(IQueryable<T>)); // typed as the interface: System.Linq's operators validate the argument against it

    IQueryProvider IQueryable.Provider => this;

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => throw NotTranslated("enumerating the query synchronously");

    IEnumerator IEnumerable.GetEnumerator() => throw NotTranslated("enumerating the query synchronously");

    IQueryable IQueryProvider.CreateQuery(Expression expression) => throw NotTranslated(Describe(expression));

    IQueryable<TElement> IQueryProvider.CreateQuery<TElement>(Expression expression) => new DynamicQuery<TElement>(_session, expression);

    object? IQueryProvider.Execute(Expression expression) => throw NotTranslated(Describe(expression) + " synchronously; use the async terminals");

    TResult IQueryProvider.Execute<TResult>(Expression expression) => throw NotTranslated(Describe(expression) + " synchronously; use the async terminals");

    private static string Describe(Expression expression) => expression is MethodCallExpression call ? call.Method.Name : expression.NodeType.ToString();

    private static NotSupportedException NotTranslated(string what)
        => new($"The call site '{what}' on a SqlQuery<{typeof(T).Name}> was not translated at compile time: it is outside the translated subset (the generator reports PRAGC diagnostics for those), or the generator did not run on this assembly. Nothing runs in memory.");
}
