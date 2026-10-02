using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace Pragmatic.Messaging.Sql;

/// <summary>
///     Broker-style schema provisioning: creates the transport tables idempotently at
///     <c>ConnectAsync</c> via EF's relational creator (portable — no per-provider DDL).
///     Concurrent replicas race benignly: the loser's CreateTables throws, the re-probe
///     confirms the winner's tables.
/// </summary>
public sealed partial class SqlTransportSchema(
    IDbContextFactory<SqlTransportDbContext> contextFactory,
    ILogger<SqlTransportSchema> logger)
{
    public async Task EnsureCreatedAsync(CancellationToken ct = default)
    {
        var db = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using (db.ConfigureAwait(false))
        {
            var creator = db.GetService<IRelationalDatabaseCreator>();

            if (!await creator.ExistsAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await creator.CreateAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Another replica created the database first.
                    LogCreateRace("database", ex);
                }
            }

            if (await TablesExistAsync(db, ct).ConfigureAwait(false))
                return;

            try
            {
                await creator.CreateTablesAsync(ct).ConfigureAwait(false);
                LogSchemaCreated();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Lost the race with another replica — verify its tables are there.
                if (!await TablesExistAsync(db, ct).ConfigureAwait(false))
                    throw;
                LogCreateRace("tables", ex);
            }
        }
    }

    private static async Task<bool> TablesExistAsync(SqlTransportDbContext db, CancellationToken ct)
    {
        try
        {
            _ = await db.Messages.AsNoTracking().AnyAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex; // probe failed → tables missing
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SQL transport schema created (__TransportMessages/__TransportSubscriptions/__TransportDeadLetters)")]
    private partial void LogSchemaCreated();

    [LoggerMessage(Level = LogLevel.Debug, Message = "SQL transport schema {What} creation race with another replica (benign)")]
    private partial void LogCreateRace(string what, Exception ex);
}
