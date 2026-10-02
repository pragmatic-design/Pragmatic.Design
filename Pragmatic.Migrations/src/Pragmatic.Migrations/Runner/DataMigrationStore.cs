using System.Data.Common;
using Pragmatic.Migrations.Sql;

namespace Pragmatic.Migrations.Runner;

/// <summary>
///     Reads and writes the <c>__PragmaticDataMigrations</c> tracking table, which records
///     applied data migrations by name so each one runs exactly once per database.
/// </summary>
internal static class DataMigrationStore
{
    /// <summary>Ensures the tracking table exists. Idempotent.</summary>
    public static async Task EnsureTableAsync(
        DbConnection connection, DbTransaction transaction, ISqlMigrationGenerator generator, CancellationToken ct = default)
    {
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.Transaction = transaction;
            cmd.CommandText = generator.GenerateDataMigrationTableDdl();
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Returns the names of data migrations already applied.</summary>
    public static async Task<HashSet<string>> GetAppliedNamesAsync(
        DbConnection connection, DbTransaction transaction, CancellationToken ct = default)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.Transaction = transaction;
            cmd.CommandText = $"SELECT \"Name\" FROM \"{MigrationConstants.DataMigrationTableName}\"";
            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    names.Add(reader.GetString(0));
            }
        }

        return names;
    }

    /// <summary>Records a data migration as applied.</summary>
    public static async Task RecordAsync(
        DbConnection connection, DbTransaction transaction, string name, long durationMs, CancellationToken ct = default)
    {
        var cmd = connection.CreateCommand();
        await using (cmd.ConfigureAwait(false))
        {
            cmd.Transaction = transaction;
            cmd.CommandText =
                $"INSERT INTO \"{MigrationConstants.DataMigrationTableName}\" (\"Name\", \"DurationMs\") VALUES (@name, @duration)";
            AddParameter(cmd, "@name", name);
            AddParameter(cmd, "@duration", durationMs);
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        var param = cmd.CreateParameter();
        param.ParameterName = name;
        param.Value = value;
        cmd.Parameters.Add(param);
    }
}
