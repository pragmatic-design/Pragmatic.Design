using System.Data.Common;
using System.Text.Json;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Introspection;

/// <summary>
///     Reads and writes the migration audit table (default: __PragmaticSchema).
/// </summary>
public static class SchemaAuditStore
{
    /// <summary>
    ///     Ensures the audit table and index exist. Idempotent.
    /// </summary>
    /// <param name="connection">Open connection to the database being migrated.</param>
    /// <param name="generator">Provider-specific generator that renders the DDL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="auditTableName">
    ///     Must be the same name <see cref="RecordMigrationAsync" /> writes to — i.e.
    ///     <c>MigrationOptions.AuditTableName</c>. Creating one table and inserting into another
    ///     is the failure this parameter exists to prevent.
    /// </param>
    public static async Task EnsureTableAsync(
        DbConnection connection,
        ISqlMigrationGenerator generator,
        CancellationToken ct = default,
        string auditTableName = MigrationConstants.AuditTableName)
    {
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.CommandText = generator.GenerateAuditTableDdl(auditTableName);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Records a migration result in the audit table.
    /// </summary>
    /// <remarks>
    ///     The provider name says how the connection's parameters are written (Oracle's are <c>:name</c>, the others'
    ///     <c>@name</c>). It is required, so that no caller falls back to a spelling its driver rejects.
    /// </remarks>
    public static async Task RecordMigrationAsync(
        DbConnection connection,
        SchemaVersion schema,
        string sqlScript,
        int changeCount,
        long durationMs,
        string providerName,
        string? appliedBy = null,
        string auditTableName = MigrationConstants.AuditTableName,
        CancellationToken ct = default)
    {
        var schemaJson = JsonSerializer.Serialize(schema, SchemaJsonContext.Default.SchemaVersion);
        string P(string name) => SqlParameterSyntax.Placeholder(providerName, name);

        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            // Identifiers cannot be parameterised; the table name comes from host configuration,
            // so double any embedded quote rather than letting it terminate the quoted identifier.
            cmd.CommandText = $"""
                INSERT INTO "{auditTableName.Replace("\"", "\"\"")}" ("Hash", "SchemaJson", "DurationMs", "ChangeCount", "SqlScript", "AppliedBy")
                VALUES ({P("hash")}, {P("json")}, {P("duration")}, {P("changes")}, {P("sql")}, {P("appliedBy")})
                """;

            AddParameter(cmd, SqlParameterSyntax.ParameterName(providerName, "hash"), schema.Hash);
            AddParameter(cmd, SqlParameterSyntax.ParameterName(providerName, "json"), schemaJson);
            AddParameter(cmd, SqlParameterSyntax.ParameterName(providerName, "duration"), durationMs);
            AddParameter(cmd, SqlParameterSyntax.ParameterName(providerName, "changes"), changeCount);
            AddParameter(cmd, SqlParameterSyntax.ParameterName(providerName, "sql"), sqlScript);
            AddParameter(cmd, SqlParameterSyntax.ParameterName(providerName, "appliedBy"), appliedBy ?? Environment.MachineName);

            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Reads the migration history from the audit table.
    /// </summary>
    public static async Task<IReadOnlyList<AuditRecord>> ReadHistoryAsync(
        DbConnection connection,
        CancellationToken ct = default,
        string auditTableName = MigrationConstants.AuditTableName)
    {
        var records = new List<AuditRecord>();

        try
        {
            var cmd = connection.CreateCommand();
            await using (cmd.ConfigureAwait(false))
            {
                cmd.CommandText = $"""
                    SELECT "Hash", "AppliedAt", "ChangeCount", "DurationMs", "AppliedBy"
                    FROM "{auditTableName.Replace("\"", "\"\"")}"
                    ORDER BY "AppliedAt" DESC
                    """;

                var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    {
                        records.Add(new AuditRecord
                        {
                            Hash = reader.IsDBNull(0) ? null : reader.GetString(0),
                            AppliedAt = reader.IsDBNull(1) ? null : reader.GetDateTime(1),
                            ChangeCount = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
                            DurationMs = reader.IsDBNull(3) ? 0 : reader.GetInt64(3),
                            AppliedBy = reader.IsDBNull(4) ? null : reader.GetString(4)
                        });
                    }
                }
            }
        }
        catch (Exception ex) when (IsTableNotExistException(ex))
        {
            // Audit table does not exist yet — return empty on first run
        }

        return records;
    }

    /// <summary>
    ///     Represents a single record from the migration audit table.
    /// </summary>
    public sealed class AuditRecord
    {
        public string? Hash { get; init; }
        public DateTime? AppliedAt { get; init; }
        public int ChangeCount { get; init; }
        public long DurationMs { get; init; }
        public string? AppliedBy { get; init; }
    }

    private static void AddParameter(DbCommand cmd, string name, object? value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(param);
    }

    /// <summary>
    ///     Returns true only for exceptions that indicate the audit table does not exist yet.
    ///     Allows connection failures and permission errors to propagate rather than being swallowed.
    /// </summary>
    private static bool IsTableNotExistException(Exception ex)
    {
        var msg = ex.Message;
        // PostgreSQL: relation "..." does not exist
        // SQL Server: Invalid object name '...'
        // SQLite: no such table: ...
        // MySQL: Table '...' doesn't exist; Oracle: ORA-00942: table or view does not exist
        return msg.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("no such table", StringComparison.OrdinalIgnoreCase);
    }
}
