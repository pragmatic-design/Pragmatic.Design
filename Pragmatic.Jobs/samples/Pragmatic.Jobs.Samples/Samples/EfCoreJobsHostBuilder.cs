using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.EFCore.Extensions;
using Pragmatic.Jobs.Extensions;

namespace Pragmatic.Jobs.Samples.Samples;

/// <summary>
///     Host bootstrap that swaps the in-memory stores for the EF Core-backed
///     <c>EfCoreJobStore</c> / <c>EfCoreRecurringJobStore</c> via
///     <c>UseEfCorePersistence()</c>.
///     <para>
///         Uses a SQLite connection so the sample exercises real relational
///         persistence and the atomic <c>ExecuteUpdateAsync</c> lease logic
///         without requiring Docker or PostgreSQL. The caller owns the
///         <see cref="DbConnection"/> (kept open to preserve the in-memory DB)
///         and the schema is created up-front with <c>EnsureCreated</c>.
///     </para>
///     <para>
///         The store is registered <c>Scoped</c>, so it resolves a scoped
///         <c>DbContext</c>; the <c>EfCoreJobStore</c> takes a base
///         <see cref="DbContext"/> dependency, which we satisfy by forwarding
///         <see cref="JobsDbContext"/>.
///     </para>
/// </summary>
internal static class EfCoreJobsHostBuilder
{
    public static IHost Build(DbConnection connection, Action<JobsBuilder>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.AddDbContext<JobsDbContext>(o => o.UseSqlite(connection), ServiceLifetime.Scoped);

        // EfCoreJobStore depends on the base DbContext type — forward it.
        builder.Services.AddScoped<DbContext>(sp => sp.GetRequiredService<JobsDbContext>());

        builder.Services.AddPragmaticJobs(jobs =>
        {
            jobs.WithWorkerCount(2);
            jobs.WithPollingInterval(1);
            jobs.WithBatchSize(10);
            jobs.UseEfCore();            // drops the in-memory defaults
            jobs.UseEfCorePersistence(); // registers the EF Core stores
            configure?.Invoke(jobs);
        });

        // SG-generated: registers every [Job]/[RecurringJob] class, swaps in the generated job
        // type registry and exposes the declared recurring definitions.
        builder.Services.AddDiscoveredJobs();
        builder.Services.AddJobProcessingServices();

        return builder.Build();
    }

    /// <summary>Opens a fresh in-memory SQLite connection and creates the Jobs schema.</summary>
    public static async Task<SqliteConnection> CreateOpenConnectionAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<JobsDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var ctx = new JobsDbContext(options);
        await ctx.Database.EnsureCreatedAsync();

        return connection;
    }
}
