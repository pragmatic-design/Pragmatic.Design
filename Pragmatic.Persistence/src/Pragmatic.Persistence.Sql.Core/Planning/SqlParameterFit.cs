using System.Data;
using System.Data.Common;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     Widens a string parameter whose value is longer than the size it was typed with, before a write runs, so the value reaches the
///     database whole (#717).
/// </summary>
/// <remarks>
///     <para>
///         <see cref="SqlValue.Configure" /> types a string parameter with a size of 4000, which is what a prepared plan needs and what SQL
///         Server's <c>CONTAINS</c> takes (it refuses an <c>nvarchar(max)</c> argument). But a size is also the most SqlClient, Npgsql,
///         Microsoft.Data.Sqlite and ODP.NET send of a value, with no error: a 5000-character text written to an <c>nvarchar(max)</c>
///         column read back as its first 4000.
///     </para>
///     <para>
///         So the size stays, and only a value longer than it widens its parameter: to unbounded (<c>-1</c>), and on Oracle to the value's
///         length, where a <c>VARCHAR2(4000 CHAR)</c> column then refuses the value instead of storing its prefix. Run on the statements the
///         session executes itself (a writer's, a batch's), not on a plan's reads.
///     </para>
/// </remarks>
internal static class SqlParameterFit
{
    /// <summary>Widens each string parameter of <paramref name="parameters" /> whose value does not fit its size.</summary>
    public static void Widen(DbParameterCollection parameters, SqlDialect dialect)
    {
        foreach (DbParameter parameter in parameters)
        {
            if (parameter.DbType != DbType.String || parameter.Size <= 0 || parameter.Value is not string text || text.Length <= parameter.Size)
                continue;
            parameter.Size = dialect == SqlDialect.Oracle ? text.Length : -1;
        }
    }

    /// <summary>
    ///     Fits each string parameter of a plan's command, which the session keeps and reuses, to the value bound this call: widened when the
    ///     value is longer than the size it was typed with, and back to that size when a later value fits it (#712).
    /// </summary>
    /// <remarks>
    ///     A plan's reads bound a value past 4000 characters at the typed size and compared its prefix, so a filter on a long text found
    ///     nothing on SQL Server, PostgreSQL and SQLite. Widening alone would leave the kept command unbounded for every later call, and SQL
    ///     Server's <c>CONTAINS</c> refuses an <c>nvarchar(max)</c> argument: so the size goes back as soon as the value fits.
    /// </remarks>
    public static void FitReused(DbParameterCollection parameters, SqlDialect dialect)
    {
        foreach (DbParameter parameter in parameters)
        {
            if (parameter.DbType != DbType.String || parameter.Value is not string text)
                continue;
            if (text.Length > TypedStringSize)
                parameter.Size = dialect == SqlDialect.Oracle ? text.Length : -1;
            else if (parameter.Size is -1 or > TypedStringSize)
                parameter.Size = TypedStringSize;
        }
    }

    /// <summary>The size <see cref="SqlValue.Configure" /> types a string parameter with.</summary>
    private const int TypedStringSize = 4000;

    /// <summary>The same for every command of a provider batch.</summary>
    public static void Widen(DbBatch batch, SqlDialect dialect)
    {
        foreach (DbBatchCommand command in batch.BatchCommands)
            Widen(command.Parameters, dialect);
    }
}
