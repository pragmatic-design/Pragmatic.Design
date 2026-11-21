using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Configuration;
using Pragmatic.Jobs.Engines.Sql;
using Pragmatic.Persistence.Sql;
using Pragmatic.Temporal.Clock;
using Showcase.Booking;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Jobs;

/// <summary>
///     The durable job store the job suites run against, on the Sql engine (#904): <see cref="SqlJobStore" /> and
///     <see cref="SqlRecurringJobStore" /> over the Booking boundary's session, which is [EnableJobPersistence] and keeps <c>__Jobs</c> and
///     <c>__RecurringJobs</c> in the App database the host migrated. In place of the EF Core suite's file, which runs EF Core's store over a
///     DbContext of its own.
/// </summary>
public sealed class JobStoreUnderTest(PostgresFixture fixture)
{
    /// <summary>A pair of stores over a session of their own, from a scope of the running host: a second call is a second worker.</summary>
    public Task<JobStores> OpenAsync()
    {
        var scope = fixture.SharedHost.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredKeyedService<SqlSession>(typeof(BookingBoundary));
        return Task.FromResult(new JobStores(new SqlJobStore(session, SystemClock.Instance, NullLogger<SqlJobStore>.Instance), new SqlRecurringJobStore(session), scope));
    }

    /// <summary>
    ///     What the store reads through, for a host of the test's own: the Booking boundary's session over the App database, handed to the store
    ///     as the boundary's generated registration hands it. The running host is started first, so the tables are there.
    /// </summary>
    public Task AddToAsync(IServiceCollection services)
    {
        _ = fixture.SharedHost.Services;
        var connectionString = fixture.AppConnectionString;
        services.AddKeyedScoped<SqlSession>(typeof(BookingBoundary), (_, _) =>
        {
            var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            return new SqlSession(connection, SqlDialect.PostgreSql);
        });
        services.AddSqlJobStore<BookingBoundary>();
        return Task.CompletedTask;
    }

    /// <summary>The host's switch to this store.</summary>
    public static void Use(JobsBuilder jobs) => jobs.UseSqlPersistence();

    /// <summary>The real generated registration of Booking's jobs, in this engine's copy of Booking (its namespace is the copy's).</summary>
    public static void AddBookingJobs(IServiceCollection services) => Showcase.Booking.Sql.PragmaticJobRegistration.AddDiscoveredJobs(services);

    /// <summary>Every job the store holds: what a test reads to find the ones a run wrote under its correlation.</summary>
    /// <remarks>
    ///     The keys are read with SQL of the test's own, and each job through the store: a chain on the engine is translated where it is written,
    ///     and the generator does not run on this test assembly.
    /// </remarks>
    public async Task<IReadOnlyList<JobInstance>> AllJobsAsync()
    {
        var ids = new List<Guid>();
        await using (var connection = new NpgsqlConnection(fixture.AppConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT \"Id\" FROM \"__Jobs\"";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                ids.Add(reader.GetGuid(0));
        }

        await using var stores = await OpenAsync();
        var jobs = new List<JobInstance>(ids.Count);
        foreach (var id in ids)
        {
            if (await stores.Jobs.GetAsync(id) is { } job)
                jobs.Add(job);
        }

        return jobs;
    }
}
