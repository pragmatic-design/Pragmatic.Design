using System.Data.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pragmatic.Configuration.Database.Dialects;

namespace Pragmatic.Configuration.Database.Schema;

/// <summary>
///     Auto-creates configuration tables on first use.
///     Idempotent — safe to call on every startup.
/// </summary>
internal sealed class ConfigurationSchemaManager(
    ISqlDialect dialect,
    Pragmatic.Audit.AdoNet.IAuditSqlDialect auditDialect,
    IOptions<DatabaseConfigurationOptions> options,
    ILogger<ConfigurationSchemaManager> logger)
{
    private readonly bool _autoCreate = options.Value.AutoCreateSchema;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _initialized;

    public async Task EnsureCreatedAsync(DbConnection connection, CancellationToken ct = default)
    {
        if (_initialized)
            return;

        // Schema managed externally (migrations, or a DB user without DDL rights): never issue
        // CREATE TABLE. Mark initialized so callers don't retry on every operation.
        if (!_autoCreate)
        {
            _initialized = true;
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            logger.LogDebug("Ensuring configuration database schema exists");

            var wasOpen = connection.State == System.Data.ConnectionState.Open;
            if (!wasOpen)
                await connection.OpenAsync(ct).ConfigureAwait(false);

            try
            {
                var command = connection.CreateCommand();
                await using (command.ConfigureAwait(false))
                {
                    // The configuration tables and the audit trail's, in one statement batch. The
                    // trail's DDL comes from its own package rather than being restated here: the
                    // shape of those tables belongs to whoever writes and reads them.
                    command.CommandText = dialect.CreateSchema + Environment.NewLine + auditDialect.CreateSchema;
                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                _initialized = true;
                logger.LogInformation("Configuration database schema verified");
            }
            finally
            {
                if (!wasOpen)
                    await connection.CloseAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
