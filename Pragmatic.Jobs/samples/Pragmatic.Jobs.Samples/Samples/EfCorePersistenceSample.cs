using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Pragmatic.Jobs.Samples.Samples;

/// <summary>
///     Production-style persistence wiring for the EF Core-backed
///     <c>EfCoreJobStore</c> / <c>EfCoreRecurringJobStore</c> (instead of the
///     in-memory stores used by the other samples).
///     <para>
///         <b>Setup-only.</b> This sample stands up the full DI graph
///         (<c>AddDbContext</c> + <c>UseEfCorePersistence()</c> + the SG-generated
///         job registry) and proves the EF store resolves and persists a job row.
///         It does <b>not</b> start the background processor, because the lease
///         acquisition path (<c>TryAcquireLeaseAsync</c> via <c>ExecuteUpdateAsync</c>)
///         and the <c>DateTimeOffset</c> columns are not translatable by the SQLite
///         provider — they require a production provider (PostgreSQL / SQL Server),
///         which would need Docker. The same wiring runs unchanged there; only the
///         <c>UseSqlite(...)</c> call becomes <c>UseNpgsql(...)</c> etc.
///     </para>
/// </summary>
public static class EfCorePersistenceSample
{
    public static async Task Run()
    {
        Console.WriteLine("--- EF Core persistence (EfCoreJobStore) [setup-only] ---");

        // Open SQLite in-memory connection; keep it open for the DB lifetime.
        await using var connection = await EfCoreJobsHostBuilder.CreateOpenConnectionAsync();

        // Build the host with EF Core persistence wired (UseEfCorePersistence()).
        using var host = EfCoreJobsHostBuilder.Build(connection);

        Console.WriteLine("  DI graph built with UseEfCorePersistence().");

        using var scope = host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IJobStore>();
        Console.WriteLine($"  resolved IJobStore impl : {store.GetType().Name}");

        // Persist a job row directly through the EF store (the enqueue path IS
        // SQLite-translatable) to prove the relational round-trip works.
        var enqueued = await store.EnqueueAsync(new JobInstance
        {
            JobType = typeof(SendInvoiceEmailJob).FullName!,
            Status = JobStatus.Pending,
            ScheduledFor = DateTimeOffset.UtcNow,
            MaxAttempts = 3,
            CorrelationId = "efcore-demo",
        });
        Console.WriteLine($"  persisted job to DB     : {enqueued.Id}");

        // Read it back from the database in a fresh scope (new DbContext) — proves
        // the row survived the round-trip, not just an in-memory reference.
        using (var readScope = host.Services.CreateScope())
        {
            var readStore = readScope.ServiceProvider.GetRequiredService<IJobStore>();
            var fromDb = await readStore.GetAsync(enqueued.Id);
            Console.WriteLine($"  re-read from DB         : {fromDb?.JobType} / {fromDb?.Status}");
        }

        // Confirm the table actually exists in SQLite (the SG entity configs were applied).
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM __Jobs";
            var count = (long)(await cmd.ExecuteScalarAsync() ?? 0L);
            Console.WriteLine($"  rows in __Jobs table    : {count}");
        }

        Console.WriteLine("  (background processing needs PostgreSQL/SQL Server — see XML docs)");
        Console.WriteLine();
    }
}
