using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Pragmatic.Persistence.Sql.Conversion;

/// <summary>
///     A <c>[ConcurrencyAware]</c> row's version (the shape's <c>ulong RowVersion</c>) as the bigint the databases compare it as: PostgreSQL's
///     <c>xmin</c> and SQL Server's <c>rowversion</c> cast to one, the BIGINT column elsewhere. The providers bind no unsigned 64-bit value.
/// </summary>
public sealed class SqlRowVersion : ISqlConverter<ulong, long>
{
    public static long ToStore(ulong value) => unchecked((long)value);

    public static ulong FromStore(long value) => unchecked((ulong)value);
}
