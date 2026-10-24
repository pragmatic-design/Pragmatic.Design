using System.Collections.Concurrent;
using System.Data.Common;
using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>A key the database assigns (<c>[Identity]</c>): how a row binds without it, and how the value comes back into the object.</summary>
public sealed class SqlIdentity<T>(Action<T, DbParameterCollection, int, SqlDialect> bindWithoutKey, Action<T, DbDataReader, SqlDialect> readKey)
{
    public Action<T, DbParameterCollection, int, SqlDialect> BindWithoutKey { get; } = bindWithoutKey;

    public Action<T, DbDataReader, SqlDialect> ReadKey { get; } = readKey;
}
