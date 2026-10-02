using Pragmatic.Testing.Assertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pragmatic.Documents.Markup;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Extensions;
using Pragmatic.Jobs.EFCore;
using Pragmatic.Jobs.EFCore.Extensions;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Testing;
using Showcase.Booking;
using Showcase.Booking.Infrastructure.Jobs;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Jobs;

/// <summary>
///     Drives the real background pipeline — <c>JobProcessorService</c> and
///     <c>RecurringJobSchedulerService</c> running as hosted services over <see cref="EfCoreJobStore"/>
///     on real PostgreSQL — end to end.
/// </summary>
/// <remarks>
///     The Showcase host itself runs jobs on the in-memory stores, so nothing else exercises the
///     durable path where a scheduled job is picked up by a background worker, dispatched through the
///     generated <c>IJobTypeRegistry</c>, executed, and marked terminal in the database. This builds a
///     host with EF Core persistence over a dedicated database in the shared container to cover it.
/// </remarks>
public class JobExecutionEndToEndTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string JobsDatabase = "showcase_jobs_e2e";

    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static bool _schemaReady;

    // A background worker completing a job is inherently asynchronous: poll rather than sleep a
    // fixed amount, so the test is as fast as the pipeline and still tolerant of a slow CI box.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private string JobsConnectionString =>
        new NpgsqlConnectionStringBuilder(Fixture.AppConnectionString) { Database = JobsDatabase }
            .ConnectionString;

    private async Task EnsureSchemaAsync()
    {
        if (_schemaReady)
            return;

        await SchemaGate.WaitAsync();
        try
        {
            if (_schemaReady)
                return;

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

            _schemaReady = true;
        }
        finally
        {
            SchemaGate.Release();
        }
    }

    private JobsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JobsDbContext>().UseNpgsql(JobsConnectionString).Options;
        return new JobsDbContext(options);
    }

    private async Task<IHost> StartHostAsync(NotificationTestHarness notifications)
    {
        await EnsureSchemaAsync();

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        var services = builder.Services;

        services.AddDbContext<JobsDbContext>(o => o.UseNpgsql(JobsConnectionString), ServiceLifetime.Scoped);
        // EfCoreJobStore takes the base DbContext type — forward it to the concrete one.
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<JobsDbContext>());

        // The dependency of SendCheckInReminderJob; records the send so the test can observe that the
        // job actually ran, not merely that its row flipped to Completed. The harness instance is the
        // caller's, because the assertions are made on it after the host has run.
        services.AddSingleton(notifications);
        services.AddSingleton<INotificationService>(sp => sp.GetRequiredService<NotificationTestHarness>());

        // Its other dependency: the reminder is a template embedded in Booking. The wording is not what
        // this test is about, so the localizer holds no translations and the keys stand in for them.
        services.AddSingleton<IStringLocalizer>(
            new StringLocalizer(new InMemoryLocalizationProvider(), new I18NOptions()));
        services.AddPdxTemplates(templates => templates.FromAssemblyOf<Showcase.Booking.BookingModule>());

        services.AddPragmaticJobs(jobs =>
        {
            jobs.WithWorkerCount(2);
            jobs.WithPollingInterval(1);
            jobs.UseEfCore();
            jobs.UseEfCorePersistence();
        });

        // The real generated registration for the Booking assembly: registers the job classes,
        // swaps in the generated PragmaticJobTypeRegistry and exposes the recurring provider.
        services.AddDiscoveredJobs();

        services.AddJobProcessingServices();

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    [Fact]
    public async Task ScheduledJob_IsPickedUpByTheProcessorAndRunToCompletion()
    {
        var notifications = new NotificationTestHarness();
        using var host = await StartHostAsync(notifications);

        Guid jobId;
        using (var scope = host.Services.CreateScope())
        {
            var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
            jobId = await scheduler.ScheduleAsync<SendCheckInReminderJob, CheckInReminderParams>(
                new CheckInReminderParams(
                    ReservationId: Guid.NewGuid(),
                    GuestId: Guid.NewGuid(),
                    GuestEmail: "guest@example.com",
                    CheckIn: DateTimeOffset.UtcNow.AddDays(1)));
        }

        // The job's side effect fired — the notification service was actually invoked.
        var sent = await WaitForSendAsync(notifications, Timeout);
        sent.Should().NotBeNull("the background processor must execute the scheduled job");
        sent!.Recipient.EmailAddress.Should().Be("guest@example.com");

        // And the durable state reflects a clean completion.
        await using var context = CreateDbContext();
        var stored = await context.Set<JobInstance>().AsNoTracking().FirstAsync(j => j.Id == jobId);
        stored.Status.Should().Be(JobStatus.Completed);
        stored.CompletedAt.Should().NotBeNull();
        stored.LeasedBy.Should().BeNull();

        await host.StopAsync();
    }

    [Fact]
    public async Task RecurringDefinition_WhenDue_IsEnqueuedByTheSchedulerAndExecuted()
    {
        var notifications = new NotificationTestHarness();
        using var host = await StartHostAsync(notifications);

        // The scheduler registers the declared "no-show-detection" definition at startup, seeding
        // its next occurrence from the cron ("0 * * * *") — the next top-of-the-hour, which a
        // real-time test cannot wait for. Force it due with a direct update once it exists: seeding
        // via UpsertAsync would not work, because it deliberately preserves an existing schedule.
        await ForceDefinitionDueAsync("no-show-detection");

        // The scheduler claims the due definition and enqueues a NoShowDetectionJob instance, which the
        // processor then runs to completion.
        var deadline = DateTime.UtcNow + Timeout;
        JobInstance? executed = null;
        while (DateTime.UtcNow < deadline)
        {
            await using var context = CreateDbContext();
            executed = await context.Set<JobInstance>()
                .AsNoTracking()
                .Where(j => j.JobType == typeof(NoShowDetectionJob).FullName && j.Status == JobStatus.Completed)
                .OrderByDescending(j => j.CompletedAt)
                .FirstOrDefaultAsync();
            if (executed is not null)
                break;
            await Task.Delay(1000);
        }

        if (executed is null)
        {
            await using var diag = CreateDbContext();
            var def = await diag.Set<RecurringJobDefinition>().AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == "no-show-detection");
            var instances = await diag.Set<JobInstance>().AsNoTracking()
                .Where(j => j.JobType == typeof(NoShowDetectionJob).FullName)
                .Select(j => new { j.Status, j.ScheduledFor, j.Error })
                .ToListAsync();
            var detail = $"def: NextExec={def?.NextExecutionAt:o} enabled={def?.IsEnabled}; " +
                         $"instances=[{string.Join(", ", instances.Select(i => $"{i.Status}@{i.ScheduledFor:o} err={i.Error}"))}]";
            executed.Should().NotBeNull(
                $"a due recurring definition must be enqueued and executed end to end. Diagnostics: {detail}");
        }

        await host.StopAsync();
    }

    // Waits for the scheduler to have registered the declared definition, then moves its next
    // occurrence into the past with a direct update so the very next poll finds it due.
    private async Task ForceDefinitionDueAsync(string id)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var context = CreateDbContext();
            var updated = await context.Set<RecurringJobDefinition>()
                .Where(d => d.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.NextExecutionAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
            if (updated > 0)
                return;
            await Task.Delay(500);
        }

        throw new InvalidOperationException($"Recurring definition '{id}' was never registered by the scheduler.");
    }

    /// <summary>
    ///     Waits for the background worker to have sent one, polling the harness.
    /// </summary>
    /// <remarks>
    ///     The hand-rolled double this replaced carried a <c>TaskCompletionSource</c> so it
    ///     could be awaited. <c>NotificationTestHarness</c> records instead of signalling, so the wait
    ///     is a poll — the same shape the rest of this class already uses for the durable state, and
    ///     one double fewer to keep in step with <c>INotificationService</c>.
    /// </remarks>
    private static async Task<NotificationRequest?> WaitForSendAsync(
        NotificationTestHarness notifications, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (notifications.Sent.Count > 0)
                return notifications.Sent[0].Request;

            await Task.Delay(100);
        }

        return null;
    }
}
