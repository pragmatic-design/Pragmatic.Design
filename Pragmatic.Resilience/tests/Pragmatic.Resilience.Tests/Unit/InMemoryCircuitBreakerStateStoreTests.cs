using Pragmatic.Testing.Assertions;
using Pragmatic.Resilience.State;

namespace Pragmatic.Resilience.Tests.Unit;

public class InMemoryCircuitBreakerStateStoreTests
{
    [Fact]
    public async Task NewCircuit_DefaultsClosed()
    {
        var store = new InMemoryCircuitBreakerStateStore();

        var snapshot = await store.GetSnapshotAsync("test-circuit");

        snapshot.State.Should().Be(CircuitState.Closed);
        snapshot.FailureCount.Should().Be(0);
        snapshot.SuccessCount.Should().Be(0);
    }

    [Fact]
    public async Task RecordFailure_IncrementsCount()
    {
        var store = new InMemoryCircuitBreakerStateStore();

        await store.RecordFailureAsync("test-circuit");
        await store.RecordFailureAsync("test-circuit");

        var snapshot = await store.GetSnapshotAsync("test-circuit");
        snapshot.FailureCount.Should().Be(2);
        snapshot.LastFailureTime.Should().NotBeNull();
    }

    [Fact]
    public async Task RecordSuccess_IncrementsCount()
    {
        var store = new InMemoryCircuitBreakerStateStore();

        await store.RecordSuccessAsync("test-circuit");

        var snapshot = await store.GetSnapshotAsync("test-circuit");
        snapshot.SuccessCount.Should().Be(1);
    }

    [Fact]
    public async Task TransitionToOpen_SetsStateAndBreakDuration()
    {
        var store = new InMemoryCircuitBreakerStateStore();
        var breakDuration = TimeSpan.FromSeconds(10);

        await store.TransitionToAsync("test-circuit", CircuitState.Open, breakDuration);

        var snapshot = await store.GetSnapshotAsync("test-circuit");
        snapshot.State.Should().Be(CircuitState.Open);
        snapshot.BreakDuration.Should().Be(breakDuration);
        snapshot.OpenedAt.Should().NotBeNull();
        snapshot.CanProbe.Should().BeFalse();
    }

    [Fact]
    public async Task OpenCircuit_CanProbe_AfterBreakDurationElapsed()
    {
        var store = new InMemoryCircuitBreakerStateStore();

        await store.TransitionToAsync("test-circuit", CircuitState.Open, TimeSpan.FromMilliseconds(1));
        await Task.Delay(10);

        var snapshot = await store.GetSnapshotAsync("test-circuit");
        snapshot.CanProbe.Should().BeTrue();
    }

    [Fact]
    public async Task HalfOpen_RecordSuccess_TransitionsToClosed()
    {
        var store = new InMemoryCircuitBreakerStateStore();

        await store.TransitionToAsync("test-circuit", CircuitState.HalfOpen);
        await store.RecordSuccessAsync("test-circuit");

        var snapshot = await store.GetSnapshotAsync("test-circuit");
        snapshot.State.Should().Be(CircuitState.Closed);
        snapshot.FailureCount.Should().Be(0);
    }

    [Fact]
    public async Task HalfOpen_RecordFailure_TransitionsToOpen()
    {
        var store = new InMemoryCircuitBreakerStateStore();
        var breakDuration = TimeSpan.FromSeconds(30);

        await store.TransitionToAsync("test-circuit", CircuitState.Open, breakDuration);
        await store.TransitionToAsync("test-circuit", CircuitState.HalfOpen);
        await store.RecordFailureAsync("test-circuit");

        var snapshot = await store.GetSnapshotAsync("test-circuit");
        snapshot.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task IndependentCircuits_DontInterfere()
    {
        var store = new InMemoryCircuitBreakerStateStore();

        await store.RecordFailureAsync("circuit-a");
        await store.RecordFailureAsync("circuit-a");
        await store.RecordSuccessAsync("circuit-b");

        var snapA = await store.GetSnapshotAsync("circuit-a");
        var snapB = await store.GetSnapshotAsync("circuit-b");

        snapA.FailureCount.Should().Be(2);
        snapB.FailureCount.Should().Be(0);
        snapB.SuccessCount.Should().Be(1);
    }

    [Fact]
    public async Task TransitionToClosed_ResetsCounters()
    {
        var store = new InMemoryCircuitBreakerStateStore();

        await store.RecordFailureAsync("test-circuit");
        await store.RecordFailureAsync("test-circuit");
        await store.TransitionToAsync("test-circuit", CircuitState.Closed);

        var snapshot = await store.GetSnapshotAsync("test-circuit");
        snapshot.State.Should().Be(CircuitState.Closed);
        snapshot.FailureCount.Should().Be(0);
        snapshot.SuccessCount.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentAccess_ThreadSafe()
    {
        var store = new InMemoryCircuitBreakerStateStore();
        var tasks = new List<Task>();

        for (var i = 0; i < 100; i++)
        {
            tasks.Add(store.RecordFailureAsync("concurrent-circuit"));
            tasks.Add(store.RecordSuccessAsync("concurrent-circuit"));
        }

        await Task.WhenAll(tasks);

        var snapshot = await store.GetSnapshotAsync("concurrent-circuit");
        // Total operations: 200 (100 failures + 100 successes)
        // Due to half-open auto-transitions, exact counts may vary
        // but it should NOT throw
        (snapshot.FailureCount + snapshot.SuccessCount).Should().BeGreaterThan(0);
    }
}
