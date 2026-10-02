using Pragmatic.Testing.Assertions;
using Pragmatic.Jobs.Stores;
using Pragmatic.Temporal.Testing;

namespace Pragmatic.Jobs.Tests.Unit;

/// <summary>
///     Higher-priority due jobs must be polled before lower-priority ones, ties broken by schedule.
/// </summary>
public class JobPriorityTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    private static JobInstance Job(int priority, TimeSpan age) => new()
    {
        Id = Guid.NewGuid(),
        JobType = "TestApp.Job",
        Status = JobStatus.Pending,
        Priority = priority,
        ScheduledFor = Now - age,
        MaxAttempts = 1
    };

    [Fact]
    public async Task GetPendingAsync_OrdersByPriorityDescThenSchedule()
    {
        var store = new InMemoryJobStore(new TestClock(Now));

        var low = Job(priority: 0, age: TimeSpan.FromMinutes(10));   // oldest, but lowest priority
        var highNewer = Job(priority: 5, age: TimeSpan.FromMinutes(1));
        var highOlder = Job(priority: 5, age: TimeSpan.FromMinutes(3));

        await store.EnqueueAsync(low);
        await store.EnqueueAsync(highNewer);
        await store.EnqueueAsync(highOlder);

        var pending = await store.GetPendingAsync(10, Now);

        pending.Select(j => j.Id).Should().ContainInOrder(highOlder.Id, highNewer.Id, low.Id);
    }

    [Fact]
    public async Task GetPendingAsync_HigherPriorityWinsTheBatchWhenCapacityIsLimited()
    {
        var store = new InMemoryJobStore(new TestClock(Now));

        var low = Job(priority: 0, age: TimeSpan.FromMinutes(10));
        var high = Job(priority: 9, age: TimeSpan.FromMinutes(1));
        await store.EnqueueAsync(low);
        await store.EnqueueAsync(high);

        // Only one slot: the higher priority job must claim it even though it was scheduled later.
        var pending = await store.GetPendingAsync(1, Now);

        pending.Should().ContainSingle().Which.Id.Should().Be(high.Id);
    }
}
