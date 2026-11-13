using System.Data;
using System.Data.Common;

namespace Pragmatic.Persistence.Sql.Planning;

/// <summary>
///     Widens a string parameter whose value is longer than the size it was typed with, before a write runs, so the value reaches the
///     database whole (#717); and a decimal one whose value has more decimals than its scale (#731).
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
            if (FitDecimal(parameter))
                continue;
            if (parameter.DbType != DbType.String || parameter.Size <= 0 || parameter.Value is not string text || text.Length <= parameter.Size)
                continue;
            parameter.Size = dialect == SqlDialect.Oracle ? text.Length : -1;
        }
    }

    /// <summary>
    ///     A decimal parameter carries the scale of its value when it has more decimals than the parameter was typed with, and goes back to
    ///     the typed (18,2) when a later value fits it (#731). True when <paramref name="parameter" /> is a decimal one.
    /// </summary>
    /// <remarks>
    ///     A scale is also what SqlClient rounds a value to before sending it: a rate of 1.2345 written to a <c>decimal(18,4)</c> column
    ///     arrived as 1.23, and a filter on it compared 7.06 for 7.0625. The precision widens to the most a decimal column has, so the integer
    ///     digits the narrower type held still fit beside the extra decimals; the column itself rounds to what it declares, as it would
    ///     for any statement.
    /// </remarks>
    private static bool FitDecimal(DbParameter parameter)
    {
        if (parameter.DbType != DbType.Decimal)
            return false;
        if (parameter.Value is decimal value && value.Scale > TypedDecimalScale)
        {
            parameter.Precision = WidenedDecimalPrecision;
            parameter.Scale = (byte)value.Scale;
        }
        else if (parameter.Scale != TypedDecimalScale)
        {
            parameter.Precision = TypedDecimalPrecision;
            parameter.Scale = TypedDecimalScale;
        }

        return true;
    }

    /// <summary>The precision and scale <see cref="SqlValue.Configure" /> types a decimal parameter with.</summary>
    private const byte TypedDecimalPrecision = 18;

    private const byte TypedDecimalScale = 2;

    private const byte WidenedDecimalPrecision = 38;

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
            if (FitDecimal(parameter))
                continue;
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
