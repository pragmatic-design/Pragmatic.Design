using System.Collections.Concurrent;
using System.Data.Common;

namespace Pragmatic.Persistence.Sql.Planning;

public sealed class SqlProjectionMap<TSource, TResult> : SqlProjectionMap
{
    public SqlProjectionMap(string[] columns, string[] joins, Func<DbDataReader, SqlDialect, TResult> read)
        : base(typeof(TSource), typeof(TResult), columns, joins, (r, d) => read(r, d))
    {
        Read = read;
    }

    public Func<DbDataReader, SqlDialect, TResult> Read { get; }

    public static void Register(SqlProjectionMap<TSource, TResult> map) => Add(map);
}
