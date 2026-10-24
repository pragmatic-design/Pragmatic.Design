using System.Data.Common;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>
///     One statement of a write batch: its text per dialect (without the count form, which the batch adds), its parameter kinds and
///     binder, how many rows it must touch (-1 when any number will do), whether its own rows come back (an identity insert, read
///     by <see cref="Row" />), what to do once the batch succeeded, and the exception a short count means.
/// </summary>
internal sealed class SqlWriteItem(string table, int shape, SqlParamKind[] kinds, Func<SqlDialect, string> sql, Action<DbParameterCollection, int, SqlDialect> bind, int expected)
{
    public string Table { get; } = table;

    /// <summary>The statement's prepared-plan id, for the batch's shape key; -1 when the text varies per call (the command is not kept).</summary>
    public int Shape { get; } = shape;

    public SqlParamKind[] Kinds { get; } = kinds;

    public int Expected { get; } = expected;

    public bool ReturnsRows { get; init; }

    public Action<DbDataReader, SqlDialect>? Row { get; init; }

    public Action? Completed { get; init; }

    public Func<int, SqlConcurrencyException>? Conflict { get; init; }

    /// <summary>The writer that reads the row's database-filled columns (<c>[DatabaseGenerated]</c>) back after the batch's command; null when there are none to read.</summary>
    public ISqlWriter? ReadBackBy { get; init; }

    /// <summary>The row <see cref="ReadBackBy" /> reads back into.</summary>
    public object? ReadBackRow { get; init; }

    public string Sql(SqlDialect dialect) => sql(dialect);

    public void Bind(DbParameterCollection parameters, int offset, SqlDialect dialect) => bind(parameters, offset, dialect);
}
