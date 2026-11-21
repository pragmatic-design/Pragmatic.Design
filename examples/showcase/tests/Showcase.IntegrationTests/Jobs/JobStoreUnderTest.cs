using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.EFCore;
using Pragmatic.Jobs.EFCore.Extensions;
using Pragmatic.Temporal.Clock;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Jobs;

/// <summary>
///     The durable job store the job suites run against, on EF Core: <see cref="EfCoreJobStore" /> over <see cref="JobsDbContext" />, on a
///     database of its own in the shared container. Each engine suite replaces this file with its own, over the Booking boundary's session,
///     so the same tests run against each store (#904).
/// </summary>
/// <remarks>
///     A database of its own: the Showcase database already exists, so <c>EnsureCreated</c> would be a no-op there and never create the job
///     tables. The suites tell their rows apart by id and correlation.
/// </remarks>
public sealed class JobStoreUnderTest(PostgresFixture fixture)
{
    private const string JobsDatabase = "showcase_jobs";

    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static bool _schemaReady;

    private string ConnectionString => new NpgsqlConnectionStringBuilder(fixture.AppConnectionString) { Database = JobsDatabase }.ConnectionString;

    /// <summary>A pair of stores over a connection of their own: a second call is a second worker.</summary>
    public async Task<JobStores> OpenAsync()
    {
        await EnsureSchemaAsync();
        var context = CreateDbContext();
        return new JobStores(new EfCoreJobStore(context, SystemClock.Instance, NullLogger<EfCoreJobStore>.Instance), new EfCoreRecurringJobStore(context), context);
    }

    /// <summary>What the store reads through, for a host of the test's own: registered before <c>AddPragmaticJobs</c>.</summary>
    public async Task AddToAsync(IServiceCollection services)
    {
        await EnsureSchemaAsync();
        services.AddDbContext<JobsDbContext>(o => o.UseNpgsql(ConnectionString), ServiceLifetime.Scoped);
        // EfCoreJobStore takes the base DbContext type — forward it to the concrete one.
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<JobsDbContext>());
    }

    /// <summary>The host's switch to this store.</summary>
    public static void Use(JobsBuilder jobs)
    {
        jobs.UseEfCore();
        jobs.UseEfCorePersistence();
    }

    /// <summary>
    ///     The real generated registration of Booking's jobs: the job classes, the generated <c>IJobTypeRegistry</c> and the recurring
    ///     provider. Here because the generator names it after the assembly's namespace, which an engine's copy of Booking does not share.
    /// </summary>
    public static void AddBookingJobs(IServiceCollection services) => Showcase.Booking.PragmaticJobRegistration.AddDiscoveredJobs(services);

    /// <summary>Every job the store holds: what a test reads to find the ones a run wrote under its correlation.</summary>
    public async Task<IReadOnlyList<JobInstance>> AllJobsAsync()
    {
        await using var context = CreateDbContext();
        return await context.Set<JobInstance>().AsNoTracking().ToListAsync();
    }

    private JobsDbContext CreateDbContext() => new(new DbContextOptionsBuilder<JobsDbContext>().UseNpgsql(ConnectionString).Options);

    private async Task EnsureSchemaAsync()
    {
        if (_schemaReady)
            return;

        await SchemaGate.WaitAsync();
        try
        {
            if (_schemaReady)
                return;

            await using (var admin = new NpgsqlConnection(fixture.AppConnectionString))
            {
                await admin.OpenAsync();
                await using var command = admin.CreateCommand();
                command.CommandText = $"SELECT 1 FROM pg_database WHERE datname = '{JobsDatabase}';";
                if (await command.ExecuteScalarAsync() is null)
                {
                    command.CommandText = $"CREATE DATABASE {JobsDatabase};";
                    await command.ExecuteNonQueryAsync();
                }
            }

            await using var context = CreateDbContext();
            await context.Database.EnsureCreatedAsync();
            _schemaReady = true;
        }
        finally
        {
            SchemaGate.Release();
        }
    }
}
