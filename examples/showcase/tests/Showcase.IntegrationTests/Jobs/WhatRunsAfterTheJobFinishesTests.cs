using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pragmatic.Documents.Markup;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Jobs;
using Pragmatic.Jobs.Extensions;
using Pragmatic.Notifications.Testing;
using Pragmatic.Testing.Assertions;
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
///         it continues. Scoping by correlation matters: the store is shared with the other job
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
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private JobStoreUnderTest JobStore => new(Fixture);

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

        // The control: the reminder declares no continuation, and its run ends with itself. It has to have run: a reminder that failed to
        // start is followed by nothing whatever the declaration says.
        var controlRan = await WaitForAsync(j => j.CorrelationId == alone && j.Status == JobStatus.Completed);
        controlRan.Should().NotBeNull("the control has to complete, or that nothing follows it says nothing about the declaration");

        var followedTheControl = (await JobStore.AllJobsAsync())
            .Where(j => j.CorrelationId == alone)
            .Select(j => j.JobType)
            .ToList();

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
            var match = (await JobStore.AllJobsAsync()).FirstOrDefault(predicate);
            if (match is not null)
                return match;

            await Task.Delay(500);
        }

        return null;
    }

    private async Task<IHost> StartHostAsync()
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();

        var services = builder.Services;
        await JobStore.AddToAsync(services);
        // The reminder job's dependency. The repository's own harness rather than a stub of this
        // suite's: it records what was sent, so a case here could assert it instead of ignoring it.
        services.AddNotificationTestHarness();
        // ⚠️ And its other two, as JobExecutionEndToEndTests registers them: without the templates the reminder could not be activated,
        // so the control failed on every attempt and the wait for it ran out in silence, measured at a minute a run.
        services.AddSingleton<IStringLocalizer>(new StringLocalizer(new InMemoryLocalizationProvider(), new I18NOptions()));
        services.AddPdxTemplates(templates => templates.FromAssemblyOf<Showcase.Booking.BookingModule>());

        services.AddPragmaticJobs(jobs =>
        {
            jobs.WithWorkerCount(2);
            jobs.WithPollingInterval(1);
            JobStoreUnderTest.Use(jobs);
        });

        JobStoreUnderTest.AddBookingJobs(services);
        services.AddJobProcessingServices();

        var host = builder.Build();
        await host.StartAsync();
        return host;
    }
}
