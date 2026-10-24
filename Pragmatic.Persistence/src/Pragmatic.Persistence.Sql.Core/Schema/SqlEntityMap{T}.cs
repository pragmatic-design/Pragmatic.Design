using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq.Expressions;

namespace Pragmatic.Persistence.Sql.Schema;

/// <summary>The typed map: its reader shapes a whole row into the entity.</summary>
public sealed class SqlEntityMap<T> : SqlEntityMap
{
    private static SqlEntityMap<T>? s_current;

    public SqlEntityMap(string table, string[] keyColumns, SqlColumn[] columns, SqlNavigation[] navigations, SqlCollection[] collections, Func<LambdaExpression>[] rules, Func<DbDataReader, SqlDialect, T> read, Func<T, object?> key, string? fixedPredicate = null)
        : base(typeof(T), table, keyColumns, columns, navigations, collections, rules, (r, d) => read(r, d)!, e => key((T)e), fixedPredicate)
    {
        Read = read;
    }

    public Func<DbDataReader, SqlDialect, T> Read { get; }

    /// <summary>The entity's generated writes; null when the entity's assembly was not translated.</summary>
    public SqlWriter<T>? Writer { get; init; }

    internal override ISqlWriter? WriterBoxed => Writer;

    public static SqlEntityMap<T> Current => s_current ?? throw new InvalidOperationException($"No map is registered for {typeof(T).Name}: it is not an [Entity], or the generator did not run on the assembly that declares it.");

    public static void Register(SqlEntityMap<T> map)
    {
        s_current = map;
        Add(map);
    }
}
