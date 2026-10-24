using System.Collections;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Linq.Expressions;
using System.Text;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The async terminals of a runtime chain, on <see cref="IQueryable{T}" />: a chain typed as the
///     interface binds to these (the typed <c>SqlQuery&lt;T&gt;</c> has its own, intercepted at compile time).
/// </summary>
public static class QueryableExtensions
{
    public static ValueTask<List<T>> ToListAsync<T>(this IQueryable<T> source, CancellationToken ct = default) => Dynamic(source).ToListAsync(ct);

    public static async ValueTask<T[]> ToArrayAsync<T>(this IQueryable<T> source, CancellationToken ct = default) => (await Dynamic(source).ToListAsync(ct).ConfigureAwait(false)).ToArray();

    public static ValueTask<T?> FirstOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken ct = default) => Dynamic(source).FirstOrDefaultAsync(null, ct);

    public static ValueTask<T?> FirstOrDefaultAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken ct = default) => Dynamic(source).FirstOrDefaultAsync(predicate, ct);

    public static ValueTask<T> FirstAsync<T>(this IQueryable<T> source, CancellationToken ct = default) => Dynamic(source).FirstAsync(ct);

    public static ValueTask<T> SingleAsync<T>(this IQueryable<T> source, CancellationToken ct = default) => Dynamic(source).SingleAsync(ct);

    public static ValueTask<T?> SingleOrDefaultAsync<T>(this IQueryable<T> source, CancellationToken ct = default) => Dynamic(source).SingleOrDefaultAsync(ct);

    public static ValueTask<int> CountAsync<T>(this IQueryable<T> source, CancellationToken ct = default) => Dynamic(source).CountAsync(null, ct);

    public static ValueTask<int> CountAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken ct = default) => Dynamic(source).CountAsync(predicate, ct);

    public static ValueTask<bool> AnyAsync<T>(this IQueryable<T> source, CancellationToken ct = default) => Dynamic(source).AnyAsync(null, ct);

    public static ValueTask<bool> AnyAsync<T>(this IQueryable<T> source, Expression<Func<T, bool>> predicate, CancellationToken ct = default) => Dynamic(source).AnyAsync(predicate, ct);

    public static ValueTask<TValue> SumAsync<T, TValue>(this IQueryable<T> source, Expression<Func<T, TValue>> value, CancellationToken ct = default) => Dynamic(source).ScalarAsync<TValue>("SumAsync", value, ct);

    public static ValueTask<TValue> MinAsync<T, TValue>(this IQueryable<T> source, Expression<Func<T, TValue>> value, CancellationToken ct = default) => Dynamic(source).ScalarAsync<TValue>("MinAsync", value, ct);

    public static ValueTask<TValue> MaxAsync<T, TValue>(this IQueryable<T> source, Expression<Func<T, TValue>> value, CancellationToken ct = default) => Dynamic(source).ScalarAsync<TValue>("MaxAsync", value, ct);

    public static ValueTask<double> AverageAsync<T, TValue>(this IQueryable<T> source, Expression<Func<T, TValue>> value, CancellationToken ct = default) => Dynamic(source).ScalarAsync<double>("AverageAsync", value, ct);

    public static async ValueTask<Dictionary<TKey, T>> ToDictionaryAsync<T, TKey>(this IQueryable<T> source, Func<T, TKey> key, CancellationToken ct = default)
        where TKey : notnull
    {
        var rows = await Dynamic(source).ToListAsync(ct).ConfigureAwait(false);
        var dictionary = new Dictionary<TKey, T>(rows.Count);
        foreach (var row in rows)
            dictionary.Add(key(row), row);
        return dictionary;
    }

    /// <summary>
    ///     The declared projection on a runtime chain: the DTO marked <c>[MapFrom&lt;T&gt;]</c> gives the columns and the
    ///     reader, so no lambda is written and no tree with an object initializer is built (that tree is not trim-safe:
    ///     <c>Expression.Bind</c>, IL2026). The AOT-clean way to project on the runtime path.
    /// </summary>
    public static IQueryable<TResult> Project<T, TResult>(this IQueryable<T> source)
    {
        // The method's own MethodInfo, from a delegate: no MakeGenericMethod, so Native AOT has the instantiation at hand.
        var method = ((Func<IQueryable<T>, IQueryable<TResult>>)Project<T, TResult>).Method;
        return source.Provider.CreateQuery<TResult>(Expression.Call(null, method, source.Expression));
    }

    /// <summary>
    ///     Include on a runtime chain: a member path of to-one navigations (<c>l => l.Order!.Customer</c>: joins, hydrated from
    ///     the registered maps), a collection (<c>o => o.Lines</c>: a second query on the keys read), and <see cref="SqlPath" /> <c>Then</c>
    ///     steps below either (<c>c => c.Orders.Then(o => o.Lines)</c>), as on the typed chain. The path is only read, never compiled.
    /// </summary>
    public static IQueryable<T> Include<T, TNavigation>(this IQueryable<T> source, Expression<Func<T, TNavigation>> path)
    {
        var method = ((Func<IQueryable<T>, Expression<Func<T, TNavigation>>, IQueryable<T>>)Include<T, TNavigation>).Method;
        return source.Provider.CreateQuery<T>(Expression.Call(null, method, source.Expression, path));
    }

    /// <summary>Keyset pagination on a runtime chain: the rows after the cursor (the last row's ordering keys, a tuple for several) in the chain's ordering, as on the typed chain.</summary>
    public static IQueryable<T> After<T, TCursor>(this IQueryable<T> source, TCursor cursor)
    {
        var method = ((Func<IQueryable<T>, TCursor, IQueryable<T>>)After<T, TCursor>).Method;
        return source.Provider.CreateQuery<T>(Expression.Call(null, method, source.Expression, Expression.Constant(cursor, typeof(TCursor))));
    }

    /// <summary>The page before a cursor on a runtime chain: the rows nearest before it, in the chain's ordering, as on the typed chain.</summary>
    public static IQueryable<T> Before<T, TCursor>(this IQueryable<T> source, TCursor cursor)
    {
        var method = ((Func<IQueryable<T>, TCursor, IQueryable<T>>)Before<T, TCursor>).Method;
        return source.Provider.CreateQuery<T>(Expression.Call(null, method, source.Expression, Expression.Constant(cursor, typeof(TCursor))));
    }

    /// <summary>Streams the rows of a runtime chain as they are read (no list); not with a collection Include.</summary>
    public static IAsyncEnumerable<T> AsAsyncEnumerable<T>(this IQueryable<T> source, CancellationToken ct = default) => Dynamic(source).Enumerate(ct);

    /// <summary>The SQL a runtime chain would run, for inspection.</summary>
    public static string ToSql<T>(this IQueryable<T> source) => Dynamic(source).ToSql();

    private static DynamicQuery<T> Dynamic<T>(IQueryable<T> source) => source switch
    {
        DynamicQuery<T> dynamic => dynamic,
        ISqlRoot root => new DynamicQuery<T>(root.Session, source.Expression),
        _ => throw new NotSupportedException($"The queryable is a {source.GetType().Name}, not a Pragmatic query: its provider is not this runtime's."),
    };
}
