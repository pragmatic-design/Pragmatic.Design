using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Documents.Markup;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Extensions;
using Pragmatic.Notifications;
using Pragmatic.Notifications.Testing;
using Showcase.Booking.Infrastructure.Jobs;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Jobs;

/// <summary>
///     Drives the real background pipeline — <c>JobProcessorService</c> and
///     <c>RecurringJobSchedulerService</c> running as hosted services over the durable store
///     (<see cref="JobStoreUnderTest" />: EF Core's, or the engine's) — end to end.
/// </summary>
/// <remarks>
///     The Showcase host itself runs jobs on the in-memory stores, so nothing else exercises the
///     durable path where a scheduled job is picked up by a background worker, dispatched through the
///     generated <c>IJobTypeRegistry</c>, executed, and marked terminal in the database. This builds a
///     host with the durable store to cover it.
/// </remarks>
public class JobExecutionEndToEndTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // A background worker completing a job is inherently asynchronous: poll rather than sleep a
    // fixed amount, so the test is as fast as the pipeline and still tolerant of a slow CI box.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private JobStoreUnderTest JobStore => new(Fixture);

    private async Task<IHost> StartHostAsync(NotificationTestHarness notifications)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        var services = builder.Services;
        await JobStore.AddToAsync(services);

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
            JobStoreUnderTest.Use(jobs);
        });

        // The real generated registration for the Booking assembly: registers the job classes,
        // swaps in the generated PragmaticJobTypeRegistry and exposes the recurring provider.
        JobStoreUnderTest.AddBookingJobs(services);

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
        var stored = await WaitForAsync(jobId, j => j.Status == JobStatus.Completed);
        stored.Should().NotBeNull("the processor marks the job it ran complete in the store");
        stored!.CompletedAt.Should().NotBeNull();
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
        // real-time test cannot wait for. Force it due once it exists: seeding via UpsertAsync would
        // not work, because it deliberately preserves an existing schedule.
        var forcedAt = DateTimeOffset.UtcNow;
        await ForceDefinitionDueAsync("no-show-detection");

        // The scheduler claims the due definition and enqueues a NoShowDetectionJob instance, which the
        // processor then runs to completion.
        var deadline = DateTime.UtcNow + Timeout;
        JobInstance? executed = null;
        while (DateTime.UtcNow < deadline)
        {
            executed = (await JobStore.AllJobsAsync())
                .Where(j => j.JobType == typeof(NoShowDetectionJob).FullName && j.Status == JobStatus.Completed && j.CreatedAt >= forcedAt.AddSeconds(-5))
                .OrderByDescending(j => j.CompletedAt)
                .FirstOrDefault();
            if (executed is not null)
                break;
            await Task.Delay(1000);
        }

        if (executed is null)
        {
            await using var diag = await JobStore.OpenAsync();
            var def = await diag.Recurring.GetAsync("no-show-detection");
            var instances = (await JobStore.AllJobsAsync())
                .Where(j => j.JobType == typeof(NoShowDetectionJob).FullName)
                .Select(j => new { j.Status, j.ScheduledFor, j.Error })
                .ToList();
            var detail = $"def: NextExec={def?.NextExecutionAt:o} enabled={def?.IsEnabled}; " +
                         $"instances=[{string.Join(", ", instances.Select(i => $"{i.Status}@{i.ScheduledFor:o} err={i.Error}"))}]";
            executed.Should().NotBeNull(
                $"a due recurring definition must be enqueued and executed end to end. Diagnostics: {detail}");
        }

        await host.StopAsync();
    }

    // Waits for the scheduler to have registered the declared definition, then moves its next
    // occurrence into the past so the very next poll finds it due.
    private async Task ForceDefinitionDueAsync(string id)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var stores = await JobStore.OpenAsync();
            if (await stores.Recurring.GetAsync(id) is { } definition)
            {
                await stores.Recurring.UpdateNextExecutionAsync(id, DateTimeOffset.UtcNow.AddMinutes(-1),
                    definition.LastExecutedAt ?? DateTimeOffset.UtcNow.AddHours(-1));
                return;
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException($"Recurring definition '{id}' was never registered by the scheduler.");
    }

    /// <summary>The job <paramref name="jobId" /> once it matches <paramref name="predicate" />, read through the store; null at the timeout.</summary>
    private async Task<JobInstance?> WaitForAsync(Guid jobId, Func<JobInstance, bool> predicate)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            await using var stores = await JobStore.OpenAsync();
            if (await stores.Jobs.GetAsync(jobId) is { } job && predicate(job))
                return job;
            await Task.Delay(250);
        }

        return null;
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
