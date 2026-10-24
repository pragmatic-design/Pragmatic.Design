using System.Data.Common;
using System.Text;
using global::Npgsql;
using NpgsqlTypes;
using Pragmatic.Persistence.Sql.Planning;
using Pragmatic.Persistence.Sql.Schema;
using Pragmatic.Persistence.Sql.Writing;

namespace Pragmatic.Persistence.Sql.Npgsql;

/// <summary>
///     PostgreSQL's bulk load: <c>COPY "Table" ("Col", …) FROM STDIN (FORMAT BINARY)</c> through Npgsql's binary importer, each value
///     written with the PostgreSQL type its kind maps to (the same mapping the parameters use). One stream, one round trip to
///     complete; inside the session's transaction when one is open (the connection's current transaction is the importer's).
/// </summary>
public sealed class NpgsqlBulkCopier : ISqlBulkCopier
{
    public async Task<int> CopyAsync(DbConnection connection, DbTransaction? transaction, string? schema, string table, string[] columns, SqlParamKind[] kinds, IReadOnlyList<object?[]> rows, CancellationToken ct)
    {
        var npgsql = connection as NpgsqlConnection ?? throw new ArgumentException("NpgsqlBulkCopier copies through an NpgsqlConnection.", nameof(connection));
        var sql = new StringBuilder("COPY ").Append(SqlTenantSchema.Table(SqlDialect.PostgreSql, schema, table)).Append(" (");
        for (var c = 0; c < columns.Length; c++)
            sql.Append(c > 0 ? ", \"" : "\"").Append(columns[c]).Append('"');
        sql.Append(") FROM STDIN (FORMAT BINARY)");
        var types = Array.ConvertAll(kinds, TypeOf);

        var importer = await npgsql.BeginBinaryImportAsync(sql.ToString(), ct).ConfigureAwait(false);
        await using (importer.ConfigureAwait(false))
        {
            foreach (var row in rows)
            {
                await importer.StartRowAsync(ct).ConfigureAwait(false);
                for (var c = 0; c < row.Length; c++)
                {
                    var value = row[c];
                    if (value is null || value is DBNull)
                        await importer.WriteNullAsync(ct).ConfigureAwait(false);
                    else
                        await importer.WriteAsync(value, types[c], ct).ConfigureAwait(false);
                }
            }

            var completed = await importer.CompleteAsync(ct).ConfigureAwait(false);
            return (int)completed;
        }
    }

    /// <summary>The PostgreSQL type of a kind, as the session binds its parameters (a TimeSpan travels as its ticks; a DateTime as timestamp).</summary>
    private static NpgsqlDbType TypeOf(SqlParamKind kind) => kind switch
    {
        SqlParamKind.Guid => NpgsqlDbType.Uuid,
        SqlParamKind.String => NpgsqlDbType.Text,
        SqlParamKind.Int32 => NpgsqlDbType.Integer,
        SqlParamKind.Int64 => NpgsqlDbType.Bigint,
        SqlParamKind.Decimal => NpgsqlDbType.Numeric,
        SqlParamKind.Double => NpgsqlDbType.Double,
        SqlParamKind.Boolean => NpgsqlDbType.Boolean,
        SqlParamKind.DateTimeOffset => NpgsqlDbType.TimestampTz,
        SqlParamKind.DateTime => NpgsqlDbType.Timestamp,
        SqlParamKind.Bytes => NpgsqlDbType.Bytea,
        SqlParamKind.DateOnly => NpgsqlDbType.Date,
        SqlParamKind.TimeOnly => NpgsqlDbType.Time,
        _ => throw new NotSupportedException($"A column of kind {kind} is not copied in bulk: insert these rows below the session's BulkThreshold."),
    };
}
