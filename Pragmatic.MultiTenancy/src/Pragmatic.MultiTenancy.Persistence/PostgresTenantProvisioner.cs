using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Pragmatic.MultiTenancy.Persistence;

/// <summary>
///     Creates PostgreSQL databases for tenants using <c>CREATE DATABASE</c>.
///     Connects to the 'postgres' database to execute the DDL.
/// </summary>
/// <remarks>
///     ⚠️ <b>It creates an empty database and nothing else — no schema, no tables.</b> A caller
///     that stops here has a tenant
///     whose first query fails on a table that does not exist. What fills it is
///     <c>IMigrationRunner.MigrateAsync</c>, with the schema generated into the <b>host</b>
///     (<c>{Database}Schema.Current</c>) — a type this assembly cannot name, which is why the two steps
///     are composed where both are visible and not behind one call here.
/// </remarks>
public sealed partial class PostgresTenantProvisioner(ILogger<PostgresTenantProvisioner> logger) : ITenantDatabaseProvisioner
{
    // Allowlist: letters, digits, underscore, 1–63 chars. Prevents SQL injection via double-quote escape sequences.
    private static readonly Regex SafeDatabaseName = new(@"^[a-zA-Z][a-zA-Z0-9_]{0,62}$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    /// <inheritdoc />
    public async Task<bool> ProvisionAsync(string tenantId, string connectionString, CancellationToken ct = default)
    {
        var dbName = ExtractDatabaseName(connectionString);
        if (string.IsNullOrEmpty(dbName))
            throw new InvalidOperationException($"Cannot extract database name from connection string for tenant '{tenantId}'.");

        if (!SafeDatabaseName.IsMatch(dbName))
            throw new InvalidOperationException($"Database name '{dbName}' for tenant '{tenantId}' contains invalid characters. Only letters, digits, and underscores are allowed.");

        if (await ExistsAsync(connectionString, ct).ConfigureAwait(false))
        {
            LogAlreadyExists(dbName, tenantId);
            return false;
        }

        var adminCs = ReplaceDatabase(connectionString, "postgres");
        var conn = new NpgsqlConnection(adminCs);
        await using (conn.ConfigureAwait(false))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            var cmd = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", conn);
            await using (cmd.ConfigureAwait(false))
            {
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
        }

        LogCreated(dbName, tenantId);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string connectionString, CancellationToken ct = default)
    {
        var dbName = ExtractDatabaseName(connectionString);
        var adminCs = ReplaceDatabase(connectionString, "postgres");

        var conn = new NpgsqlConnection(adminCs);
        await using (conn.ConfigureAwait(false))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            var cmd = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", conn);
            await using (cmd.ConfigureAwait(false))
            {
                cmd.Parameters.AddWithValue("name", dbName!);
                var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
                return result is not null;
            }
        }
    }

    private static string? ExtractDatabaseName(string connectionString)
        => new NpgsqlConnectionStringBuilder(connectionString).Database;

    private static string ReplaceDatabase(string connectionString, string newDatabase)
        => new NpgsqlConnectionStringBuilder(connectionString) { Database = newDatabase }.ConnectionString;

    // =========================================================================
    // Logging — [LoggerMessage] for zero-allocation structured logging
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Debug, Message = "Database {Database} for tenant {TenantId} already exists")]
    private partial void LogAlreadyExists(string database, string tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created database {Database} for tenant {TenantId}")]
    private partial void LogCreated(string database, string tenantId);
}
