using System.Data.Common;
using System.Runtime.CompilerServices;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     The plan read off a reader another command opened: a <see cref="SqlBatch" /> runs several plans' statements in one
///     command and hands each plan the reader positioned on its first result set. The same shaping as the terminals,
///     without the command; the collection includes' result sets are consumed whether or not any row came back, so the
///     next item of the batch finds its own.
/// </summary>
public sealed partial class SqlPlan<T>
{
    /// <summary>The plan's prepared command with this call's values bound: a batch copies them into its own parameters.</summary>
    internal DbCommand BoundCommand(in SqlQuery<T> query) => Command(query);

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<List<T>> ReadListAsync(SqlQuery<T> query, DbDataReader reader, SqlDialect dialect, CancellationToken ct)
    {
        var list = new List<T>(query.TakeCount > 0 ? query.TakeCount : 16);
        var read = Read!;
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            list.Add(read(reader, dialect));
        if (Load is not null)
            await Load(reader, dialect, list, ct).ConfigureAwait(false); // the companions' result sets follow, rows or none
        return list;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<T[]> ReadArrayAsync(SqlQuery<T> query, DbDataReader reader, SqlDialect dialect, CancellationToken ct) => (await ReadListAsync(query, reader, dialect, ct).ConfigureAwait(false)).ToArray();

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<T?> ReadFirstOrDefaultAsync(SqlQuery<T> query, DbDataReader reader, SqlDialect dialect, CancellationToken ct)
    {
        var rows = await ReadListAsync(query, reader, dialect, ct).ConfigureAwait(false);
        return rows.Count > 0 ? rows[0] : default;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<T> ReadFirstAsync(SqlQuery<T> query, DbDataReader reader, SqlDialect dialect, CancellationToken ct)
        => await ReadFirstOrDefaultAsync(query, reader, dialect, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("The query returned no rows.");

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<T> ReadSingleAsync(SqlQuery<T> query, DbDataReader reader, SqlDialect dialect, CancellationToken ct)
    {
        var rows = await ReadListAsync(query, reader, dialect, ct).ConfigureAwait(false);
        return rows.Count switch
        {
            1 => rows[0],
            0 => throw new InvalidOperationException("The query returned no rows."),
            _ => throw new InvalidOperationException("The query returned more than one row."),
        };
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<T?> ReadSingleOrDefaultAsync(SqlQuery<T> query, DbDataReader reader, SqlDialect dialect, CancellationToken ct)
    {
        var rows = await ReadListAsync(query, reader, dialect, ct).ConfigureAwait(false);
        return rows.Count switch
        {
            1 => rows[0],
            0 => default,
            _ => throw new InvalidOperationException("The query returned more than one row."),
        };
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> ReadCountAsync(SqlQuery<T> query, DbDataReader reader, SqlDialect dialect, CancellationToken ct)
        => await reader.ReadAsync(ct).ConfigureAwait(false) ? SqlValue.Count(reader.GetValue(0)) : 0;

    /// <summary>A command's rows touched, off its counted form in the batch: the count itself on SQL Server, MySQL and Oracle; one row per row touched (<c>RETURNING 1</c>) elsewhere.</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<int> ReadCommandAsync(SqlQuery<T> query, DbDataReader reader, SqlDialect dialect, CancellationToken ct)
    {
        if (dialect is SqlDialect.SqlServer or SqlDialect.MySql or SqlDialect.Oracle)
            return await reader.ReadAsync(ct).ConfigureAwait(false) ? SqlValue.Count(reader.GetValue(0)) : 0;
        var touched = 0;
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            touched++;
        return touched;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<bool> ReadAnyAsync(SqlQuery<T> query, DbDataReader reader, SqlDialect dialect, CancellationToken ct)
        => await reader.ReadAsync(ct).ConfigureAwait(false);

    /// <summary>An aggregate's value, default when SQL answered NULL (no rows).</summary>
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<T> ReadScalarAsync(SqlQuery<T> query, DbDataReader reader, SqlDialect dialect, CancellationToken ct)
        => await reader.ReadAsync(ct).ConfigureAwait(false) && !reader.IsDBNull(0) ? Read!(reader, dialect) : default!;

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Dictionary<TKey, T>> ReadDictionaryAsync<TKey>(SqlQuery<T> query, Func<T, TKey> key, DbDataReader reader, SqlDialect dialect, CancellationToken ct)
        where TKey : notnull
    {
        var rows = await ReadListAsync(query, reader, dialect, ct).ConfigureAwait(false);
        var dictionary = new Dictionary<TKey, T>(rows.Count);
        foreach (var row in rows)
            dictionary.Add(key(row), row);
        return dictionary;
    }
}
