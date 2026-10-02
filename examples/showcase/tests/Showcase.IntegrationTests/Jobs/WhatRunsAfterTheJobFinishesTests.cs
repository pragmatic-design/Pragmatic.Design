using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pragmatic.Jobs;
using Pragmatic.Jobs.EFCore;
using Pragmatic.Jobs.EFCore.Extensions;
using Pragmatic.Jobs.Extensions;
using Pragmatic.Notifications.Testing;
using Pragmatic.Testing.Assertions;
using Showcase.Booking;
using Showcase.Booking.Infrastructure.Jobs;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Jobs;

/// <summary>
///     <c>[Continuation&lt;T&gt;]</c>: the job declared on the class is enqueued by the
///     processor when the job completes, and nobody schedules it.
/// </summary>
/// <remarks>
///     <para>
///         The whole claim is about a row that appears without a caller, so the test schedules
///         <c>NoShowDetectionJob</c> under a correlation id of its own and then looks for a
///         <c>ReleaseNoShowRoomsJob</c> carrying that same id — the processor copies it from the job
///         it continues. Scoping by correlation matters: this database is shared with the other job
///         suites, which enqueue the same types.
///     </para>
///     <para>
///         ⚠️ The control is the second half of the same test: <c>SendCheckInReminderJob</c>, which
///         declares no continuation, is scheduled under a correlation id of its own and must leave
///         the run with exactly one row. Without it, "a second job appeared" would be satisfied by a
///         processor that enqueues something after every job.
///     </para>
/// </remarks>
public class WhatRunsAfterTheJobFinishesTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // The same database the other durable-pipeline suite uses: the schema is created once, and the
    // rows of the two are told apart by correlation id.
    private const string JobsDatabase = "showcase_jobs_e2e";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private string JobsConnectionString =>
        new NpgsqlConnectionStringBuilder(Fixture.AppConnectionString) { Database = JobsDatabase }
            .ConnectionString;

    [Fact]
    public async Task TheDeclaredContinuation_IsEnqueuedWhenTheJobCompletes_AndNothingFollowsAJobWithout()
    {
        using var host = await StartHostAsync();

        var chained = $"continuation-{Guid.NewGuid():N}";
        var alone = $"no-continuation-{Guid.NewGuid():N}";

        using (var scope = host.Services.CreateScope())
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();

            await scheduler.ScheduleAsync<NoShowDetectionJob>(correlationId: chained);
            await scheduler.ScheduleAsync<SendCheckInReminderJob, CheckInReminderParams>(
                new CheckInReminderParams(
                    ReservationId: Guid.NewGuid(),
                    GuestId: Guid.NewGuid(),
                    GuestEmail: "continuation-control@example.com",
                    CheckIn: DateTimeOffset.UtcNow.AddDays(1)),
                correlationId: alone);
        }

        var release = await WaitForAsync(
            j => j.CorrelationId == chained && j.JobType == typeof(ReleaseNoShowRoomsJob).FullName);

        release.Should().NotBeNull(
            "the processor reads [Continuation<ReleaseNoShowRoomsJob>] off the completed job and "
            + "enqueues it — no code in the example schedules this type");

        // It is a job in its own right, not a callback: it is dispatched and runs.
        var ranToCompletion = await WaitForAsync(
            j => j.Id == release!.Id && j.Status == JobStatus.Completed);
        ranToCompletion.Should().NotBeNull("the continuation is picked up and executed like any other job");

        // Its own declared budget, not the two attempts of the job that triggered it.
        release!.MaxAttempts.Should().Be(3, "the continuation carries the [Retry] declared on itself");

        // The control: the reminder declares no continuation, and its run ends with itself.
        await WaitForAsync(j => j.CorrelationId == alone && j.Status == JobStatus.Completed);

        await using var context = CreateDbContext();
        var followedTheControl = await context.Set<JobInstance>().AsNoTracking()
            .Where(j => j.CorrelationId == alone)
            .Select(j => j.JobType)
            .ToListAsync();

        followedTheControl.Should().ContainSingle(
            "a job with no [Continuation<T>] is followed by nothing — otherwise the assertion above "
            + "would be about the processor and not about the declaration");

        await host.StopAsync();
    }

    private async Task<JobInstance?> WaitForAsync(Func<JobInstance, bool> predicate)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var context = CreateDbContext();
            var rows = await context.Set<JobInstance>().AsNoTracking().ToListAsync();
            var match = rows.FirstOrDefault(predicate);
            if (match is not null)
                return match;

            await Task.Delay(500);
        }

        return null;
    }

    private JobsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JobsDbContext>().UseNpgsql(JobsConnectionString).Options;
        return new JobsDbContext(options);
    }

    private async Task<IHost> StartHostAsync()
    {
        await EnsureSchemaAsync();

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        var services = builder.Services;

        services.AddDbContext<JobsDbContext>(o => o.UseNpgsql(JobsConnectionString), ServiceLifetime.Scoped);
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<JobsDbContext>());
        // The reminder job's dependency. The repository's own harness rather than a stub of this
        // suite's: it records what was sent, so a case here could assert it instead of ignoring it.
        services.AddNotificationTestHarness();

        services.AddPragmaticJobs(jobs =>
        {
            jobs.WithWorkerCount(2);
            jobs.WithPollingInterval(1);
            jobs.UseEfCore();
            jobs.UseEfCorePersistence();
        });

        services.AddDiscoveredJobs();
        services.AddJobProcessingServices();

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private async Task EnsureSchemaAsync()
    {
        await using (var admin = new NpgsqlConnection(Fixture.AppConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = admin.CreateCommand();
            cmd.CommandText = $"SELECT 1 FROM pg_database WHERE datname = '{JobsDatabase}';";
            if (await cmd.ExecuteScalarAsync() is null)
            {
                cmd.CommandText = $"CREATE DATABASE {JobsDatabase};";
                await cmd.ExecuteNonQueryAsync();
            }
        }

        var options = new DbContextOptionsBuilder<JobsDbContext>().UseNpgsql(JobsConnectionString).Options;
        await using var context = new JobsDbContext(options);
        await context.Database.EnsureCreatedAsync();
    }

}
