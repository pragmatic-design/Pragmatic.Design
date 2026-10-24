using System.Globalization;
using System.Text;

namespace Pragmatic.Persistence.Sql.Writing;

/// <summary>The statements of a writer, built once per dialect: insert (one or many rows), update, upsert (ON CONFLICT or MERGE), delete.</summary>
public sealed partial class SqlWriter<T>
{
    private string Insert(SqlDialect dialect, int rows)
    {
        if (_identity is not null && dialect == SqlDialect.Oracle)
        {
            // One INSERT per row, each RETURNING the key the database assigned, then the keys in order (SqlOracle's block keeps and answers them).
            var block = new StringBuilder();
            for (var r = 0; r < rows; r++)
            {
                var one = InsertRows(dialect, 1, r);
                block.Append(one).Append(" RETURNING ").Append(SqlDialects.Quote(dialect, _columns[_keyIndex])).Append("; ");
            }

            return block.Append("SELECT RETURNED").ToString();
        }

        return InsertRows(dialect, rows, 0);
    }

    /// <summary>The INSERT of <paramref name="rows" /> rows, its parameters numbered from row <paramref name="firstRow" />'s.</summary>
    private string InsertRows(SqlDialect dialect, int rows, int firstRow)
    {
        var sql = new StringBuilder();
        var native = Native(dialect); // the database's version: not a column of the insert, read back
        var columns = InsertColumns(dialect);
        sql.Append("INSERT INTO ").Append(SqlDialects.Quote(dialect, _table)).Append(" (");
        for (var c = 0; c < columns.Length; c++)
            sql.Append(c > 0 ? ", " : "").Append(SqlDialects.Quote(dialect, columns[c]));
        sql.Append(')');
        if (_identity is not null && dialect == SqlDialect.SqlServer)
            sql.Append(" OUTPUT INSERTED.").Append(SqlDialects.Quote(dialect, _columns[_keyIndex]));
        if (native && dialect == SqlDialect.SqlServer)
            sql.Append(VersionOutput(dialect));
        sql.Append(" VALUES ");
        var skip = _identity is not null ? _keyIndex : native ? _versionIndex : -1;
        for (var r = firstRow; r < firstRow + rows; r++)
        {
            sql.Append(r > firstRow ? ", (" : "(");
            for (int c = 0, column = 0; column < _columns.Length; column++)
            {
                if (column == skip)
                    continue;
                sql.Append(c > 0 ? ", " : "").Append(Parameter(dialect, column, r * columns.Length + c));
                c++;
            }

            sql.Append(')');
        }

        if (native && dialect != SqlDialect.SqlServer)
            sql.Append(VersionOutput(dialect));
        if (dialect == SqlDialect.Oracle)
            return sql.ToString(); // the identity's RETURNING is the caller's (one INSERT per row)
        if (_identity is not null && dialect == SqlDialect.MySql)
        {
            // No RETURNING: the first key of the statement from LAST_INSERT_ID(), the others consecutive (one simple multi-row INSERT takes consecutive values in every innodb_autoinc_lock_mode).
            if (rows == 1)
                sql.Append("; SELECT LAST_INSERT_ID()");
            else
                sql.Append("; WITH RECURSIVE ids AS (SELECT LAST_INSERT_ID() AS id, 1 AS n UNION ALL SELECT id + 1, n + 1 FROM ids WHERE n < ").Append(rows.ToString(CultureInfo.InvariantCulture)).Append(") SELECT id FROM ids");
        }
        else if (_identity is not null && dialect != SqlDialect.SqlServer)
            sql.Append(" RETURNING ").Append(SqlDialects.Quote(dialect, _columns[_keyIndex]));
        return sql.ToString();
    }

    private string Update(SqlDialect dialect)
    {
        var sql = new StringBuilder();
        sql.Append("UPDATE ").Append(SqlDialects.Quote(dialect, _table)).Append(" SET ");
        var first = true;
        for (var c = 0; c < _columns.Length; c++)
        {
            if (IsKey(c))
                continue;
            sql.Append(first ? "" : ", ").Append(SqlDialects.Quote(dialect, _columns[c])).Append(" = ");
            if (c == _versionIndex)
                sql.Append(SqlDialects.Quote(dialect, _columns[c])).Append(" + 1");
            else if (IsInsertOnly(c))
                sql.Append("COALESCE(").Append(SqlDialects.Quote(dialect, _columns[c])).Append(", ").Append(Parameter(dialect, c, c)).Append(')'); // CreatedAt/By: as stored
            else
                sql.Append(Parameter(dialect, c, c));
            first = false;
        }

        sql.Append(" WHERE ");
        for (var k = 0; k < _keyIndexes.Length; k++)
            sql.Append(k > 0 ? " AND " : "").Append(SqlDialects.Quote(dialect, _columns[_keyIndexes[k]])).Append(" = @p").Append(_keyIndexes[k].ToString(CultureInfo.InvariantCulture));
        if (_versionIndex >= 0)
            sql.Append(" AND ").Append(SqlDialects.Quote(dialect, _columns[_versionIndex])).Append(" = @p").Append(_versionIndex.ToString(CultureInfo.InvariantCulture));
        return sql.ToString();
    }

    private bool IsKey(int column) => Array.IndexOf(_keyIndexes, column) >= 0;

    /// <summary>The parameter of a column in a statement: <c>@pN</c>, cast to the column's type on PostgreSQL when the column asks for it (a jsonb column does not take text as is).</summary>
    private string Parameter(SqlDialect dialect, int column, int index)
    {
        var name = "@p" + index.ToString(CultureInfo.InvariantCulture);
        return dialect == SqlDialect.PostgreSql && _casts?[column] is { } cast ? $"CAST({name} AS {cast})" : name;
    }

    private string Upsert(SqlDialect dialect) => dialect switch { SqlDialect.SqlServer => Merge(), SqlDialect.MySql => OnDuplicateKey(), SqlDialect.Oracle => OracleMerge(), _ => OnConflict(dialect) };

    // Oracle: MERGE on the key from a one-row source (the parameters FROM dual); with a version column the update applies only at the
    // object's version (MERGE's UPDATE … WHERE), and the version is read back when a row was merged (the block's count: 0 when stale).
    private string OracleMerge()
    {
        const SqlDialect dialect = SqlDialect.Oracle;
        var table = SqlDialects.Quote(dialect, _table);
        var sql = new StringBuilder();
        sql.Append("MERGE INTO ").Append(table).Append(" target USING (SELECT ");
        for (var c = 0; c < _columns.Length; c++)
            sql.Append(c > 0 ? ", @p" : "@p").Append(c.ToString(CultureInfo.InvariantCulture)).Append(" AS ").Append(SqlDialects.Quote(dialect, _columns[c]));
        sql.Append(" FROM dual) source ON (").Append(string.Join(" AND ", _keyIndexes.Select(k => $"target.{SqlDialects.Quote(dialect, _columns[k])} = source.{SqlDialects.Quote(dialect, _columns[k])}"))).Append(") WHEN MATCHED THEN UPDATE SET ");
        var first = true;
        for (var c = 0; c < _columns.Length; c++)
        {
            if (IsKey(c))
                continue;
            var column = SqlDialects.Quote(dialect, _columns[c]);
            sql.Append(first ? "" : ", ").Append("target.").Append(column).Append(" = ").Append(c == _versionIndex ? "target." + column + " + 1" : IsInsertOnly(c) ? $"COALESCE(target.{column}, source.{column})" : "source." + column);
            first = false;
        }

        string? version = null;
        if (_versionIndex >= 0)
        {
            version = SqlDialects.Quote(dialect, _columns[_versionIndex]);
            sql.Append(" WHERE target.").Append(version).Append(" = source.").Append(version);
        }

        sql.Append(" WHEN NOT MATCHED THEN INSERT (").Append(string.Join(", ", _columns.Select(c => SqlDialects.Quote(dialect, c)))).Append(") VALUES (").Append(string.Join(", ", _columns.Select(c => "source." + SqlDialects.Quote(dialect, c)))).Append(')');
        if (version is not null)
        {
            sql.Append("; SELECT ").Append(version).Append(" FROM ").Append(table).Append(" WHERE ");
            for (var k = 0; k < _keyIndexes.Length; k++)
                sql.Append(k > 0 ? " AND " : "").Append(SqlDialects.Quote(dialect, _columns[_keyIndexes[k]])).Append(" = @p").Append(_keyIndexes[k].ToString(CultureInfo.InvariantCulture));
            sql.Append(" AND ROW_COUNT() > 0");
        }

        return sql.ToString();
    }

    // MySQL: INSERT … ON DUPLICATE KEY UPDATE (any unique key's conflict, not the primary key's alone), the proposed row aliased `new`.
    // With a version column every assignment is guarded by the version's match (the version itself last: assignments see the ones
    // before), so a stale row changes nothing; the version is then read back in the same command, filtered by what the outcome must
    // be (ROW_COUNT() 2: updated, so the version after; otherwise the version proposed, which an inserted row has and a stale one has not).
    private string OnDuplicateKey()
    {
        const SqlDialect dialect = SqlDialect.MySql;
        var table = SqlDialects.Quote(dialect, _table);
        var sql = new StringBuilder(Insert(dialect, 1));
        sql.Append(" AS new ON DUPLICATE KEY UPDATE ");
        var version = _versionIndex >= 0 ? SqlDialects.Quote(dialect, _columns[_versionIndex]) : null;
        var first = true;
        for (var c = 0; c < _columns.Length; c++)
        {
            if (IsKey(c) || c == _versionIndex)
                continue;
            var column = SqlDialects.Quote(dialect, _columns[c]);
            sql.Append(first ? "" : ", ").Append(column).Append(" = ");
            if (IsInsertOnly(c))
                sql.Append("COALESCE(").Append(table).Append('.').Append(column).Append(", new.").Append(column).Append(')'); // CreatedAt/By: as stored
            else if (version is null)
                sql.Append("new.").Append(column);
            else
                sql.Append("IF(").Append(table).Append('.').Append(version).Append(" = new.").Append(version).Append(", new.").Append(column).Append(", ").Append(table).Append('.').Append(column).Append(')');
            first = false;
        }

        if (version is not null)
        {
            sql.Append(first ? "" : ", ").Append(version).Append(" = IF(").Append(table).Append('.').Append(version).Append(" = new.").Append(version).Append(", ").Append(table).Append('.').Append(version).Append(" + 1, ").Append(table).Append('.').Append(version).Append(')');
            var proposed = "@p" + _versionIndex.ToString(CultureInfo.InvariantCulture);
            sql.Append("; SELECT ").Append(version).Append(" FROM ").Append(table).Append(" WHERE ");
            for (var k = 0; k < _keyIndexes.Length; k++)
                sql.Append(k > 0 ? " AND " : "").Append(SqlDialects.Quote(dialect, _columns[_keyIndexes[k]])).Append(" = @p").Append(_keyIndexes[k].ToString(CultureInfo.InvariantCulture));
            sql.Append(" AND ").Append(version).Append(" = IF(ROW_COUNT() = 2, ").Append(proposed).Append(" + 1, ").Append(proposed).Append(')');
        }

        return sql.ToString();
    }

    // SQLite and PostgreSQL: the insert, and on the key's conflict the update of the other columns from the proposed row (`excluded`).
    private string OnConflict(SqlDialect dialect)
    {
        var table = SqlDialects.Quote(dialect, _table);
        var sql = new StringBuilder(Insert(dialect, 1));
        sql.Append(" ON CONFLICT (").Append(string.Join(", ", _keyIndexes.Select(k => SqlDialects.Quote(dialect, _columns[k])))).Append(") DO UPDATE SET ");
        var first = true;
        for (var c = 0; c < _columns.Length; c++)
        {
            if (IsKey(c))
                continue;
            var column = SqlDialects.Quote(dialect, _columns[c]);
            sql.Append(first ? "" : ", ").Append(column).Append(" = ");
            if (c == _versionIndex)
                sql.Append(table).Append('.').Append(column).Append(" + 1");
            else if (IsInsertOnly(c))
                sql.Append("COALESCE(").Append(table).Append('.').Append(column).Append(", excluded.").Append(column).Append(')'); // CreatedAt/By: as stored
            else
                sql.Append("excluded.").Append(column);
            first = false;
        }

        if (_versionIndex >= 0)
        {
            var version = SqlDialects.Quote(dialect, _columns[_versionIndex]);
            sql.Append(" WHERE ").Append(table).Append('.').Append(version).Append(" = excluded.").Append(version);
            sql.Append(" RETURNING ").Append(version);
        }

        return sql.ToString();
    }

    // SQL Server: MERGE on the key; the proposed row is the typed parameters as a one-row source. HOLDLOCK keeps two concurrent upserts of a new key from both inserting.
    private string Merge()
    {
        const SqlDialect dialect = SqlDialect.SqlServer;
        var table = SqlDialects.Quote(dialect, _table);
        var sql = new StringBuilder();
        sql.Append("MERGE ").Append(table).Append(" WITH (HOLDLOCK) AS target USING (SELECT ");
        for (var c = 0; c < _columns.Length; c++)
            sql.Append(c > 0 ? ", @p" : "@p").Append(c.ToString(CultureInfo.InvariantCulture)).Append(" AS ").Append(SqlDialects.Quote(dialect, _columns[c]));
        sql.Append(") AS source ON ").Append(string.Join(" AND ", _keyIndexes.Select(k => $"target.{SqlDialects.Quote(dialect, _columns[k])} = source.{SqlDialects.Quote(dialect, _columns[k])}")));
        sql.Append(" WHEN MATCHED");
        if (_versionIndex >= 0)
        {
            var version = SqlDialects.Quote(dialect, _columns[_versionIndex]);
            sql.Append(" AND target.").Append(version).Append(" = source.").Append(version);
        }

        sql.Append(" THEN UPDATE SET ");
        var first = true;
        for (var c = 0; c < _columns.Length; c++)
        {
            if (IsKey(c))
                continue;
            var column = SqlDialects.Quote(dialect, _columns[c]);
            sql.Append(first ? "" : ", ").Append("target.").Append(column).Append(" = ");
            if (c == _versionIndex)
                sql.Append("target.").Append(column).Append(" + 1");
            else if (IsInsertOnly(c))
                sql.Append("COALESCE(target.").Append(column).Append(", source.").Append(column).Append(')'); // CreatedAt/By: as stored
            else
                sql.Append("source.").Append(column);
            first = false;
        }

        sql.Append(" WHEN NOT MATCHED THEN INSERT (");
        for (var c = 0; c < _columns.Length; c++)
            sql.Append(c > 0 ? ", " : "").Append(SqlDialects.Quote(dialect, _columns[c]));
        sql.Append(") VALUES (");
        for (var c = 0; c < _columns.Length; c++)
            sql.Append(c > 0 ? ", source." : "source.").Append(SqlDialects.Quote(dialect, _columns[c]));
        sql.Append(')');
        if (_versionIndex >= 0)
            sql.Append(" OUTPUT inserted.").Append(SqlDialects.Quote(dialect, _columns[_versionIndex]));
        sql.Append(';');
        return sql.ToString();
    }

    private string Delete(SqlDialect dialect)
    {
        var sql = new StringBuilder("DELETE FROM ").Append(SqlDialects.Quote(dialect, _table)).Append(" WHERE ");
        for (var k = 0; k < _keyIndexes.Length; k++)
            sql.Append(k > 0 ? " AND " : "").Append(SqlDialects.Quote(dialect, _columns[_keyIndexes[k]])).Append(" = @p").Append(k.ToString(CultureInfo.InvariantCulture));
        if (_versionIndex >= 0)
            sql.Append(" AND ").Append(VersionCondition(dialect)).Append(" = @p").Append(_keyIndexes.Length.ToString(CultureInfo.InvariantCulture));
        return sql.ToString();
    }
}
