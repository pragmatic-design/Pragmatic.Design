using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Jobs.Services;
using Pragmatic.Jobs.Stores;
using Pragmatic.Temporal.Testing;

namespace Pragmatic.Jobs.Tests.Unit;

/// <summary>
///     The registrar is what makes a <c>[RecurringJob]</c> fire at all: a definition persisted
///     without <c>NextExecutionAt</c> is never returned by <c>GetDueAsync</c>.
/// </summary>
public class RecurringJobRegistrarTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 10, 30, 0, TimeSpan.Zero);

    private static (RecurringJobRegistrar Registrar, InMemoryRecurringJobStore Store) CreateSut()
    {
        var store = new InMemoryRecurringJobStore();
        var registrar = new RecurringJobRegistrar(store, new TestClock(Now), NullLogger<RecurringJobRegistrar>.Instance);
        return (registrar, store);
    }

    private static RecurringJobDefinition Definition(string cron = "0 * * * *", string? timeZoneId = null) => new()
    {
        Id = "daily-cleanup",
        JobType = "TestApp.DailyCleanupJob",
        CronExpression = cron,
        TimeZoneId = timeZoneId
    };

    [Fact]
    public async Task RegisterAsync_NewDefinition_SeedsNextExecutionFromCron()
    {
        var (registrar, store) = CreateSut();

        var registered = await registrar.RegisterAsync(Definition());

        registered.Should().BeTrue();
        var stored = await store.GetAsync("daily-cleanup");
        stored.Should().NotBeNull();
        stored!.NextExecutionAt.Should().Be(new DateTimeOffset(2026, 7, 20, 11, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task RegisterAsync_NewDefinition_IsImmediatelyDiscoverableAsDue()
    {
        var (registrar, store) = CreateSut();
        await registrar.RegisterAsync(Definition());

        // The end-to-end contract: once the seeded occurrence passes, the scheduler finds it.
        var due = await store.GetDueAsync(new DateTimeOffset(2026, 7, 20, 11, 0, 1, TimeSpan.Zero));

        due.Should().ContainSingle(d => d.Id == "daily-cleanup");
    }

    [Fact]
    public async Task RegisterAsync_ExistingDefinition_DoesNotRewindSchedule()
    {
        var (registrar, store) = CreateSut();
        var alreadyRunning = Definition();
        alreadyRunning.NextExecutionAt = new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero);
        alreadyRunning.LastExecutedAt = new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);
        await store.UpsertAsync(alreadyRunning);

        await registrar.RegisterAsync(Definition());

        var stored = await store.GetAsync("daily-cleanup");
        stored!.NextExecutionAt.Should().Be(new DateTimeOffset(2026, 7, 25, 0, 0, 0, TimeSpan.Zero),
            "restarting the host must not move a schedule that is already running");
        stored.LastExecutedAt.Should().Be(new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task RegisterAsync_DisabledDefinition_StaysDisabledAcrossRestart()
    {
        var (registrar, store) = CreateSut();
        await registrar.RegisterAsync(Definition());
        await store.DisableAsync("daily-cleanup");

        await registrar.RegisterAsync(Definition());

        var stored = await store.GetAsync("daily-cleanup");
        stored!.IsEnabled.Should().BeFalse("an operator's decision to disable a job must survive a restart");
    }

    [Fact]
    public async Task RegisterAsync_ExistingDefinitionWithoutNextExecution_SeedsIt()
    {
        // Rows written by an earlier version were persisted with a null NextExecutionAt and
        // would stay dormant forever without this recovery path.
        var (registrar, store) = CreateSut();
        var dormant = Definition();
        dormant.NextExecutionAt = null;
        await store.UpsertAsync(dormant);

        await registrar.RegisterAsync(Definition());

        var stored = await store.GetAsync("daily-cleanup");
        stored!.NextExecutionAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RegisterAsync_InvalidCron_SkipsWithoutThrowing()
    {
        var (registrar, store) = CreateSut();

        var registered = await registrar.RegisterAsync(Definition(cron: "not-a-cron"));

        registered.Should().BeFalse();
        (await store.GetAsync("daily-cleanup")).Should().BeNull(
            "a definition that cannot be scheduled must not be persisted as if it were");
    }

    [Fact]
    public async Task RegisterAsync_UnknownTimeZone_SkipsWithoutThrowing()
    {
        var (registrar, store) = CreateSut();

        var registered = await registrar.RegisterAsync(Definition(timeZoneId: "Mars/Olympus_Mons"));

        registered.Should().BeFalse();
        (await store.GetAsync("daily-cleanup")).Should().BeNull();
    }

    [Fact]
    public async Task RegisterAsync_WithTimeZone_SeedsOccurrenceInThatZone()
    {
        var (registrar, store) = CreateSut();

        // 10:30 UTC is 12:30 in Rome (CEST, UTC+2) → next midnight-hour tick is 13:00 Rome = 11:00 UTC.
        await registrar.RegisterAsync(Definition(timeZoneId: "Europe/Rome"));

        var stored = await store.GetAsync("daily-cleanup");
        stored!.NextExecutionAt.Should().Be(new DateTimeOffset(2026, 7, 20, 11, 0, 0, TimeSpan.Zero));
    }
}
