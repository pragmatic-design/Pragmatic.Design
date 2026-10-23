using System.Data.Common;
using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql;

/// <summary>
///     The columns the database fills (<c>[DatabaseGenerated]</c>): left out of the writer's columns, so no insert, update or upsert writes
///     them, and read back by key into the object (after an insert; after an update too when one of them changes on updates).
/// </summary>
public sealed class SqlGenerated<T>(string[] columns, bool afterUpdate, Action<T, DbDataReader, SqlDialect> read)
{
    public string[] Columns { get; } = columns;

    /// <summary>True when a column changes on updates (<c>OnUpdate</c>): an update reads them back too.</summary>
    public bool AfterUpdate { get; } = afterUpdate;

    /// <summary>Assigns the row's generated values from the reader's current row, the columns in order from ordinal 0.</summary>
    public Action<T, DbDataReader, SqlDialect> Read { get; } = read;
}

public sealed partial class SqlWriter<T>
{
    private readonly SqlGenerated<T>? _generated;

    /// <summary>
    ///     A table per type: the derived table's writer (the key and the derived type's own columns) beside this one (the root's table). A write
    ///     batch adds its statement after this one's (before it for a delete); the direct writes of such a row go through a batch.
    /// </summary>
    public SqlWriter<T>? Part { get; }

    ISqlWriter? ISqlWriter.Part => Part;

    /// <summary>The rows one read-back SELECT takes: 100 keys, well inside every provider's parameter limit.</summary>
    private const int ReadBackLot = 100;

    /// <summary>
    ///     Reads the generated columns of the rows back (in the caller's transaction when there is one): with a single-column key one SELECT
    ///     per lot of <see cref="ReadBackLot" /> rows, each answer tagged with its row's position; with a composite key, or one row, a SELECT by key.
    /// </summary>
    private async Task ReadGeneratedAsync(SqlSession session, IReadOnlyList<T> rows, int start, int count, CancellationToken ct)
    {
        if (_keyIndexes.Length == 1 && count > 1)
        {
            for (var at = start; at < start + count; at += ReadBackLot)
                await ReadGeneratedLotAsync(session, rows, at, Math.Min(ReadBackLot, start + count - at), ct).ConfigureAwait(false);
            return;
        }

        var plan = Plan(session.Dialect, "generated", Generated, KeyKinds());
        var command = session.PreparedCommand(plan.Id, plan.Sql, plan.Kinds);
        for (var i = start; i < start + count; i++)
        {
            _bindKeys(rows[i], command.Parameters, 0, session.Dialect);
            using var reader = await session.ReadAsync(command, Origin("read back"), ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false))
                _generated!.Read(rows[i], reader, session.Dialect);
        }
    }

    private Task ReadGeneratedAsync(SqlSession session, T row, CancellationToken ct) => ReadGeneratedAsync(session, [row], 0, 1, ct);

    /// <summary>One SELECT for <paramref name="size" /> rows: <c>… , CASE key WHEN @p0 THEN 0 … END FROM t WHERE key IN (@p0, …)</c>; the position says whose row it is.</summary>
    private async Task ReadGeneratedLotAsync(SqlSession session, IReadOnlyList<T> rows, int at, int size, CancellationToken ct)
    {
        var plan = Plan(session.Dialect, "generated:" + size.ToString(CultureInfo.InvariantCulture), d => GeneratedLot(d, size), Array.ConvertAll(new int[size], _ => _kinds[_keyIndex]));
        var command = session.PreparedCommand(plan.Id, plan.Sql, plan.Kinds);
        for (var i = 0; i < size; i++)
            _bindKeys(rows[at + i], command.Parameters, i, session.Dialect);
        using var reader = await session.ReadAsync(command, Origin("read back"), ct).ConfigureAwait(false);
        var position = _generated!.Columns.Length;
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            _generated.Read(rows[at + SqlValue.Count(reader.GetValue(position))], reader, session.Dialect);
    }

    private string GeneratedLot(SqlDialect dialect, int size)
    {
        var key = SqlDialects.Quote(dialect, _columns[_keyIndex]);
        var sql = new StringBuilder("SELECT ");
        foreach (var column in _generated!.Columns)
            sql.Append(SqlDialects.Quote(dialect, column)).Append(", ");
        sql.Append("CASE ").Append(key);
        for (var i = 0; i < size; i++)
            sql.Append(" WHEN @p").Append(i.ToString(CultureInfo.InvariantCulture)).Append(" THEN ").Append(i.ToString(CultureInfo.InvariantCulture));
        sql.Append(" END FROM ").Append(SqlDialects.Quote(dialect, _table)).Append(" WHERE ").Append(key).Append(" IN (");
        for (var i = 0; i < size; i++)
            sql.Append(i > 0 ? ", @p" : "@p").Append(i.ToString(CultureInfo.InvariantCulture));
        return sql.Append(')').ToString();
    }

    private string Generated(SqlDialect dialect)
    {
        var sql = new StringBuilder("SELECT ");
        for (var c = 0; c < _generated!.Columns.Length; c++)
            sql.Append(c > 0 ? ", " : "").Append(SqlDialects.Quote(dialect, _generated.Columns[c]));
        sql.Append(" FROM ").Append(SqlDialects.Quote(dialect, _table)).Append(" WHERE ");
        for (var k = 0; k < _keyIndexes.Length; k++)
            sql.Append(k > 0 ? " AND " : "").Append(SqlDialects.Quote(dialect, _columns[_keyIndexes[k]])).Append(" = @p").Append(k.ToString(CultureInfo.InvariantCulture));
        return sql.ToString();
    }

    private SqlParamKind[] KeyKinds() => Array.ConvertAll(_keyIndexes, k => _kinds[k]);
}
