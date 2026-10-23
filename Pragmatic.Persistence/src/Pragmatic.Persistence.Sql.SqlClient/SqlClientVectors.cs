using Microsoft.Data.SqlTypes;

namespace Pragmatic.Persistence.Sql.SqlClient;

/// <summary>
///     SQL Server 2025's <c>VECTOR</c> columns come back from SqlClient as <see cref="SqlVector{T}" />, a type the core assembly does not
///     reference: <see cref="Register" /> once at startup, and the generated readers get the floats through it.
/// </summary>
public static class SqlClientVectors
{
    public static void Register() => SqlVectors.RegisterReader(SqlDialect.SqlServer, static (reader, ordinal) => reader.GetFieldValue<SqlVector<float>>(ordinal).Memory.ToArray());
}
