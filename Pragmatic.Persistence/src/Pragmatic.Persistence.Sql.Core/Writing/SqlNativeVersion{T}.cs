using System.Data.Common;

namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>
///     A <c>[ConcurrencyAware]</c> row's version where the database keeps it (PostgreSQL's <c>xmin</c>, SQL Server's <c>rowversion</c>,
///     <see cref="SqlDialects.NativeRowVersion" />): an insert binds every column but it (<see cref="BindInsert" />), no write sets it, every write
///     compares it and reads the new one back (RETURNING, OUTPUT). On the other dialects the version is the BIGINT column the writes advance.
/// </summary>
public sealed class SqlNativeVersion<T>(Action<T, DbParameterCollection, int, SqlDialect> bindInsert)
{
    /// <summary>The row's columns but the version, in order from an offset.</summary>
    public Action<T, DbParameterCollection, int, SqlDialect> BindInsert { get; } = bindInsert;
}
