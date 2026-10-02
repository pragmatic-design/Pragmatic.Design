using Pragmatic.Testing.Assertions;
using Pragmatic.Jobs.Stores;

namespace Pragmatic.Jobs.Tests.Unit;

/// <summary>
///     <c>TryClaimDueAsync</c> is the compare-and-swap that makes a recurring job enqueue exactly
///     once per tick across a multi-host deployment — the recurring counterpart of the job lease.
/// </summary>
public class RecurringJobClaimTests
{
    private static readonly DateTimeOffset Due = new(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Next = new(2026, 7, 20, 11, 0, 0, TimeSpan.Zero);

    private static async Task<InMemoryRecurringJobStore> StoreWithDueDefinitionAsync()
    {
        var store = new InMemoryRecurringJobStore();
        await store.UpsertAsync(new RecurringJobDefinition
        {
            Id = "hourly",
            JobType = "TestApp.HourlyJob",
            CronExpression = "0 * * * *",
            NextExecutionAt = Due
        }).ConfigureAwait(false);
        return store;
    }

    [Fact]
    public async Task TryClaimDueAsync_WithMatchingExpectation_WinsAndAdvancesSchedule()
    {
        var store = await StoreWithDueDefinitionAsync();

        var claimed = await store.TryClaimDueAsync("hourly", Due, Next, Due);

        claimed.Should().BeTrue();
        var stored = await store.GetAsync("hourly");
        stored!.NextExecutionAt.Should().Be(Next);
        stored.LastExecutedAt.Should().Be(Due);
    }

    [Fact]
    public async Task TryClaimDueAsync_SecondClaimForSameTick_Loses()
    {
        var store = await StoreWithDueDefinitionAsync();
        await store.TryClaimDueAsync("hourly", Due, Next, Due);

        // A second host observed the same pre-claim value and races to enqueue the same tick.
        var second = await store.TryClaimDueAsync("hourly", Due, Next, Due);

        second.Should().BeFalse("only one host may enqueue a given occurrence");
    }

    [Fact]
    public async Task TryClaimDueAsync_StaleExpectation_Loses()
    {
        var store = await StoreWithDueDefinitionAsync();

        var claimed = await store.TryClaimDueAsync("hourly", Due.AddHours(-1), Next, Due);

        claimed.Should().BeFalse();
        var stored = await store.GetAsync("hourly");
        stored!.NextExecutionAt.Should().Be(Due, "a losing claim must not move the schedule");
    }

    [Fact]
    public async Task TryClaimDueAsync_DisabledDefinition_Loses()
    {
        var store = await StoreWithDueDefinitionAsync();
        await store.DisableAsync("hourly");

        var claimed = await store.TryClaimDueAsync("hourly", Due, Next, Due);

        claimed.Should().BeFalse();
    }

    [Fact]
    public async Task TryClaimDueAsync_UnknownDefinition_Loses()
    {
        var store = await StoreWithDueDefinitionAsync();

        var claimed = await store.TryClaimDueAsync("does-not-exist", Due, Next, Due);

        claimed.Should().BeFalse();
    }

    [Fact]
    public async Task TryClaimDueAsync_UnderConcurrency_ExactlyOneWinnerPerTick()
    {
        // Mirrors the 32-worker lease test: the claim is the only thing standing between
        // N hosts and N duplicate enqueues of the same occurrence.
        const int contenders = 32;
        var store = await StoreWithDueDefinitionAsync();
        using var barrier = new Barrier(contenders);

        // A dedicated thread per contender: the barrier blocks every one of them, and on pool threads
        // that starves the pool for every other test in the process while it grows to 32.
        var tasks = Enumerable.Range(0, contenders).Select(_ => Task.Factory.StartNew(async () =>
        {
            barrier.SignalAndWait();
            return await store.TryClaimDueAsync("hourly", Due, Next, Due).ConfigureAwait(false);
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap());

        var results = await Task.WhenAll(tasks);

        results.Count(won => won).Should().Be(1, "exactly one contender may win the claim");
    }
}
