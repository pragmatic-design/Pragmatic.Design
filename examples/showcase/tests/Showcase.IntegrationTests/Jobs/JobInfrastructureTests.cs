using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Jobs;
using Showcase.Booking.Infrastructure.Jobs;
using Showcase.IntegrationTests.Infrastructure;

namespace Showcase.IntegrationTests.Jobs;

/// <summary>
///     E2E tests for Pragmatic.Jobs infrastructure running against real PostgreSQL.
///     Validates: job registration, scheduling, execution, recurring job definitions.
/// </summary>
public class JobInfrastructureTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public void JobStore_IsRegistered()
    {
        var store = Services.GetService<IJobStore>();
        store.Should().NotBeNull("IJobStore should be auto-registered by UseJobs()");
    }

    [Fact]
    public void JobScheduler_IsRegistered()
    {
        using var scope = Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetService<IJobScheduler>();
        scheduler.Should().NotBeNull("IJobScheduler should be auto-registered by UseJobs()");
    }

    [Fact]
    public void RecurringJobStore_IsRegistered()
    {
        var store = Services.GetService<IRecurringJobStore>();
        store.Should().NotBeNull("IRecurringJobStore should be auto-registered by UseJobs()");
    }

    [Fact]
    public async Task RecurringJobDefinitions_AreRegistered()
    {
        var store = Services.GetRequiredService<IRecurringJobStore>();

        // The scheduler registers declared definitions after a 2s startup delay, so poll —
        // but a definition that never appears is a failure, not something to skip past.
        RecurringJobDefinition? noShow = null;
        for (var i = 0; i < 20; i++)
        {
            noShow = await store.GetAsync("no-show-detection");
            if (noShow is not null) break;
            await Task.Delay(500);
        }

        noShow.Should().NotBeNull(
            "the [RecurringJob] declared in Showcase.Booking must be persisted by the scheduler at startup");

        noShow!.CronExpression.Should().Be("0 * * * *");
        noShow.IsEnabled.Should().BeTrue();

        // The whole point of registration: a definition without NextExecutionAt is never
        // returned by GetDueAsync, so the job would silently never fire.
        noShow.NextExecutionAt.Should().NotBeNull(
            "the registrar must seed the first occurrence from the cron expression");
        noShow.NextExecutionAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-1),
            "the seeded occurrence must be in the future, not a stale timestamp");
    }

    [Fact]
    public async Task ScheduleDelayedJob_CreatesJobInstance()
    {
        using var scope = Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
        var store = Services.GetRequiredService<IJobStore>();

        var jobId = await scheduler.ScheduleAsync<SendCheckInReminderJob, CheckInReminderParams>(
            new CheckInReminderParams(
                ReservationId: Guid.NewGuid(),
                GuestId: Guid.NewGuid(),
                GuestEmail: "test@example.com",
                CheckIn: DateTimeOffset.UtcNow.AddDays(1)),
            delay: TimeSpan.FromMinutes(30));

        jobId.Should().NotBeEmpty();

        var job = await store.GetAsync(jobId);
        job.Should().NotBeNull("scheduled job should be persisted in the store");
        job!.Status.Should().Be(JobStatus.Pending);
    }

    [Fact]
    public async Task ScheduleImmediateJob_GetsExecuted()
    {
        using var scope = Services.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<IJobScheduler>();
        var store = Services.GetRequiredService<IJobStore>();

        // Schedule with zero delay (execute ASAP)
        var jobId = await scheduler.ScheduleAsync<SendCheckInReminderJob, CheckInReminderParams>(
            new CheckInReminderParams(
                ReservationId: Guid.NewGuid(),
                GuestId: Guid.NewGuid(),
                GuestEmail: "immediate@test.com",
                CheckIn: DateTimeOffset.UtcNow.AddDays(1)));

        // Poll for up to 60s — JobProcessorService polls every 10s with a 2s initial delay,
        // so worst case is ~12s, but CI can be considerably slower.
        JobInstance? job = null;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            job = await store.GetAsync(jobId);
            job.Should().NotBeNull();
            if (job!.Status is JobStatus.Completed or JobStatus.Failed)
                break;
        }

        // Pending is NOT an acceptable outcome: it is exactly what a never-started processor
        // produces, which is the failure this test exists to catch.
        job!.Status.Should().Be(JobStatus.Completed,
            "the processor must pick the job up and run it to completion");
        job.CompletedAt.Should().NotBeNull("a completed job records when it finished");
        job.Error.Should().BeNull();
    }

    /// <summary>
    ///     The registry the application resolves knows this application's jobs.
    /// </summary>
    /// <remarks>
    ///     ⚠️ The concrete type is <c>CompositeJobTypeRegistry</c>, not <c>PragmaticJobTypeRegistry</c>:
    ///     the generated one is an <c>IJobTypeRegistrySource</c> contributed to a composite, because
    ///     with a <c>Replace</c> two assemblies declaring jobs would cancel each other out.
    ///     <para>
    ///         Asserting what it <b>knows</b> rather than what it is: a composite with no sources passes
    ///         a not-null assertion and refuses every job. A name is not the property that matters.
    ///     </para>
    /// </remarks>
    [Fact]
    public void JobTypeRegistry_KnowsThisApplicationsJobs()
    {
        var registry = Services.GetService<IJobTypeRegistry>();

        registry.Should().NotBeNull();

        registry!.Knows(typeof(SendCheckInReminderJob).FullName!).Should().BeTrue(
            "the generated registry is contributed to the composite, and it declares this job");
    }

    /// <summary>The control: a type nobody declared is not known, so the registry is not answering yes to everything.</summary>
    [Fact]
    public void JobTypeRegistry_DoesNotKnowATypeNobodyDeclared()
        => Services.GetService<IJobTypeRegistry>()!
            .Knows("Showcase.Nothing.AJobNobodyWrote").Should().BeFalse();
}
