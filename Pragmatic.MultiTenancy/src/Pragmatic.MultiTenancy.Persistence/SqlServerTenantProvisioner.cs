using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Pragmatic.MultiTenancy.Persistence;

/// <summary>
///     Creates SQL Server databases for tenants.
/// </summary>
public sealed partial class SqlServerTenantProvisioner(ILogger<SqlServerTenantProvisioner> logger) : ITenantDatabaseProvisioner
{
    // Allowlist: letters, digits, underscore, 1–128 chars. Prevents bracket-escape SQL injection.
    private static readonly Regex SafeDatabaseName = new(@"^[a-zA-Z][a-zA-Z0-9_]{0,127}$", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    /// <inheritdoc />
    public async Task<bool> ProvisionAsync(string tenantId, string connectionString, CancellationToken ct = default)
    {
        var dbName = ExtractDatabaseName(connectionString);
        if (string.IsNullOrEmpty(dbName))
            throw new InvalidOperationException($"Cannot extract database name from connection string for tenant '{tenantId}'.");

        if (!SafeDatabaseName.IsMatch(dbName))
            throw new InvalidOperationException($"Database name '{dbName}' for tenant '{tenantId}' contains invalid characters. Only letters, digits, and underscores are allowed.");

        // Use a single connection to both check existence and create — avoids opening two connections.
        var masterCs = ReplaceDatabase(connectionString, "master");
        var conn = new SqlConnection(masterCs);
        await using (conn.ConfigureAwait(false))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);

            // Check existence first using the shared connection.
            if (await ExistsInternalAsync(conn, dbName, ct).ConfigureAwait(false))
            {
                LogAlreadyExists(dbName, tenantId);
                return false;
            }

            // CREATE DATABASE is DDL and cannot run in a transaction, but the guard is now atomic.
            var cmd = new SqlCommand($"CREATE DATABASE [{dbName}]", conn);
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
        var masterCs = ReplaceDatabase(connectionString, "master");

        var conn = new SqlConnection(masterCs);
        await using (conn.ConfigureAwait(false))
        {
            await conn.OpenAsync(ct).ConfigureAwait(false);
            return await ExistsInternalAsync(conn, dbName!, ct).ConfigureAwait(false);
        }
    }

    private static async Task<bool> ExistsInternalAsync(SqlConnection conn, string dbName, CancellationToken ct)
    {
        var cmd = new SqlCommand("SELECT 1 FROM sys.databases WHERE name = @name", conn);
        await using (cmd.ConfigureAwait(false))
        {
            cmd.Parameters.AddWithValue("@name", dbName);
            var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return result is not null;
        }
    }

    private static string? ExtractDatabaseName(string connectionString)
        => new SqlConnectionStringBuilder(connectionString).InitialCatalog;

    private static string ReplaceDatabase(string connectionString, string newDatabase)
        => new SqlConnectionStringBuilder(connectionString) { InitialCatalog = newDatabase }.ConnectionString;

    // =========================================================================
    // Logging — [LoggerMessage] for zero-allocation structured logging
    // =========================================================================

    [LoggerMessage(Level = LogLevel.Debug, Message = "Database {Database} for tenant {TenantId} already exists")]
    private partial void LogAlreadyExists(string database, string tenantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created database {Database} for tenant {TenantId}")]
    private partial void LogCreated(string database, string tenantId);
}
