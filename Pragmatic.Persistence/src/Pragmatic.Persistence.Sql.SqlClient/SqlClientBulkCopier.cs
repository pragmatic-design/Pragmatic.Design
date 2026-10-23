using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Pragmatic.Persistence.Sql.SqlClient;

/// <summary>
///     SQL Server's bulk load: <see cref="SqlBulkCopy" /> over the rows as a <see cref="DataTable" /> whose columns carry the kinds' .NET types,
///     mapped by name to the table's columns. Inside the session's transaction when one is open (the copy is enlisted in it); the
///     batch goes as one TDS bulk-load stream.
/// </summary>
public sealed class SqlClientBulkCopier : ISqlBulkCopier
{
    public async Task<int> CopyAsync(DbConnection connection, DbTransaction? transaction, string? schema, string table, string[] columns, SqlParamKind[] kinds, IReadOnlyList<object?[]> rows, CancellationToken ct)
    {
        var sqlConnection = connection as SqlConnection ?? throw new ArgumentException("SqlClientBulkCopier copies through a SqlConnection.", nameof(connection));
        using var data = new DataTable(table);
        for (var c = 0; c < columns.Length; c++)
            data.Columns.Add(columns[c], TypeOf(kinds[c]));
        foreach (var row in rows)
        {
            var values = new object[row.Length];
            for (var c = 0; c < row.Length; c++)
                values[c] = row[c] ?? DBNull.Value;
            data.Rows.Add(values);
        }

        using var copy = new SqlBulkCopy(sqlConnection, SqlBulkCopyOptions.Default, (SqlTransaction?)transaction) { DestinationTableName = SqlTenantSchema.Table(SqlDialect.SqlServer, schema, table), BatchSize = 0, BulkCopyTimeout = 0 };
        foreach (var column in columns)
            copy.ColumnMappings.Add(column, column);
        await copy.WriteToServerAsync(data, ct).ConfigureAwait(false);
        return rows.Count; // SqlBulkCopy reports no count of its own (RowsCopied is an event counter); the stream either completes or throws
    }

    /// <summary>The .NET type a kind's values arrive as (converted by the session as its parameters are).</summary>
    private static Type TypeOf(SqlParamKind kind) => kind switch
    {
        SqlParamKind.Guid => typeof(Guid),
        SqlParamKind.String => typeof(string),
        SqlParamKind.Int32 => typeof(int),
        SqlParamKind.Int64 => typeof(long),
        SqlParamKind.Decimal => typeof(decimal),
        SqlParamKind.Double => typeof(double),
        SqlParamKind.Boolean => typeof(bool),
        SqlParamKind.DateTimeOffset => typeof(DateTimeOffset),
        SqlParamKind.DateTime => typeof(DateTime),
        SqlParamKind.Bytes => typeof(byte[]),
        SqlParamKind.DateOnly => typeof(DateOnly),
        SqlParamKind.TimeOnly => typeof(TimeOnly),
        _ => throw new NotSupportedException($"A column of kind {kind} is not copied in bulk: insert these rows below the session's BulkThreshold."),
    };
}
