using System.Data.Common;

namespace Pragmatic.Persistence.Sql.Writing;

public sealed partial class SqlWriter<T>
{
    private string[]? _nativeInsertColumns;
    private SqlParamKind[]? _nativeInsertKinds;

    /// <summary>Set for a <c>[ConcurrencyAware]</c> Pragmatic entity: its version is the database's on PostgreSQL and SQL Server.</summary>
    public SqlNativeVersion<T>? NativeVersion { get; init; }

    /// <summary>True when the database keeps the version on this dialect: never written, compared as a bigint, read back after every write.</summary>
    private bool Native(SqlDialect dialect)
    {
        if (NativeVersion is null || !SqlDialects.NativeRowVersion(dialect))
            return false;
        if (_identity is not null)
            throw new NotSupportedException($"\"{_table}\" has an [Identity] key and a native row version: one statement reads back one of them.");
        return true;
    }

    /// <summary>The direct writes (not a write batch's items) do not read a native version back: refused there, a unit of work writes such a row.</summary>
    private void RefuseNative(SqlDialect dialect)
    {
        if (Native(dialect))
            throw new NotSupportedException($"\"{_table}\" is [ConcurrencyAware]: on {dialect} the database keeps its version, which a write reads back. Write it through a unit of work (SqlUnitOfWork) or a write batch's InsertAsIs, UpdateColumns and DeleteRow.");
    }

    /// <summary>The version in a WHERE: the database's own as a bigint, or the column.</summary>
    private string VersionCondition(SqlDialect dialect)
        => Native(dialect) ? SqlDialects.RowVersion(dialect, null, _columns[_versionIndex]) : SqlDialects.Quote(dialect, _columns[_versionIndex]);

    /// <summary>The clause that reads the new native version back: SQL Server's OUTPUT (before VALUES or WHERE), RETURNING elsewhere (at the end).</summary>
    private string VersionOutput(SqlDialect dialect)
        => dialect == SqlDialect.SqlServer ? " OUTPUT CAST(INSERTED." + SqlDialects.Quote(dialect, _columns[_versionIndex]) + " AS bigint)" : " RETURNING " + VersionCondition(dialect);

    private string[] InsertColumns(SqlDialect dialect) => Native(dialect) ? _nativeInsertColumns ??= Without(_insertColumns, _versionIndex) : _insertColumns;

    private SqlParamKind[] InsertKinds(SqlDialect dialect) => Native(dialect) ? _nativeInsertKinds ??= Without(_insertKinds, _versionIndex) : _insertKinds;

    private Action<T, DbParameterCollection, int, SqlDialect> InsertBinder(SqlDialect dialect) => Native(dialect) ? NativeVersion!.BindInsert : _bindInsert;

    /// <summary>The version alone at a parameter index, as its column binds it (a converted one stored as its store type: a row version as a bigint).</summary>
    private void BindVersion(T row, DbParameterCollection parameters, int index, SqlDialect dialect)
    {
        if (_bindColumn is not null)
            _bindColumn(row, _versionIndex, parameters, index, dialect);
        else
            parameters[index].Value = SqlValue.ToDatabase(dialect, _version!(row));
    }
}
