using Pragmatic.Testing.Assertions;
using Pragmatic.Resilience.State;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Tests.Unit;

public class CircuitBreakerStrategyTests
{
    private static ResilienceContext CreateContext(string name = "TestOp")
        => new() { OperationName = name };

    private static CircuitBreakerStrategy CreateStrategy(
        CircuitBreakerOptions? options = null,
        ICircuitBreakerStateStore? store = null)
    {
        return new CircuitBreakerStrategy(
            options ?? new CircuitBreakerOptions { FailureThreshold = 3, BreakDuration = TimeSpan.FromSeconds(5) },
            store ?? new InMemoryCircuitBreakerStateStore());
    }

    [Fact]
    public async Task ClosedCircuit_SuccessfulOperation_ReturnsResult()
    {
        var strategy = CreateStrategy();

        var result = await strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(42),
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
    }

    [Fact]
    public async Task ClosedCircuit_FailuresBelowThreshold_DoesNotOpen()
    {
        var store = new InMemoryCircuitBreakerStateStore();
        var strategy = CreateStrategy(store: store);

        // 2 failures (threshold is 3)
        for (var i = 0; i < 2; i++)
        {
            try
            {
                await strategy.ExecuteAsync<int>(
                    (ctx, ct) => throw new InvalidOperationException("fail"),
                    CreateContext(), CancellationToken.None);
            }
            catch (InvalidOperationException) { }
        }

        var snapshot = await store.GetSnapshotAsync("TestOp");
        snapshot.State.Should().Be(CircuitState.Closed);
        snapshot.FailureCount.Should().Be(2);
    }

    [Fact]
    public async Task ClosedCircuit_FailuresReachThreshold_OpensCircuit()
    {
        var store = new InMemoryCircuitBreakerStateStore();
        var strategy = CreateStrategy(store: store);

        for (var i = 0; i < 3; i++)
        {
            try
            {
                await strategy.ExecuteAsync<int>(
                    (ctx, ct) => throw new InvalidOperationException("fail"),
                    CreateContext(), CancellationToken.None);
            }
            catch (InvalidOperationException) { }
        }

        var snapshot = await store.GetSnapshotAsync("TestOp");
        snapshot.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task OpenCircuit_RejectsImmediately()
    {
        var store = new InMemoryCircuitBreakerStateStore();
        var strategy = CreateStrategy(
            new CircuitBreakerOptions { FailureThreshold = 1, BreakDuration = TimeSpan.FromMinutes(5) },
            store);

        // Trip the circuit
        try
        {
            await strategy.ExecuteAsync<int>(
                (ctx, ct) => throw new InvalidOperationException("fail"),
                CreateContext(), CancellationToken.None);
        }
        catch (InvalidOperationException) { }

        // Next call should be rejected
        var act = () => strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(42),
            CreateContext(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<CircuitBrokenException>();
        ex.Which.CircuitKey.Should().Be("TestOp");
    }

    [Fact]
    public async Task HalfOpen_SuccessfulProbe_ClosesCircuit()
    {
        var store = new InMemoryCircuitBreakerStateStore();

        // Manually set circuit to open with expired break duration
        await store.TransitionToAsync("TestOp", CircuitState.Open, TimeSpan.FromMilliseconds(1));
        await Task.Delay(10); // Wait for break duration to elapse

        var strategy = CreateStrategy(store: store);

        var result = await strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(42),
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);

        var snapshot = await store.GetSnapshotAsync("TestOp");
        snapshot.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task HalfOpen_FailedProbe_ReopensCircuit()
    {
        var store = new InMemoryCircuitBreakerStateStore();

        // Manually set circuit to open with expired break duration
        await store.TransitionToAsync("TestOp", CircuitState.Open, TimeSpan.FromMilliseconds(1));
        await Task.Delay(10);

        var strategy = CreateStrategy(store: store);

        try
        {
            await strategy.ExecuteAsync<int>(
                (ctx, ct) => throw new InvalidOperationException("still broken"),
                CreateContext(), CancellationToken.None);
        }
        catch (InvalidOperationException) { }

        var snapshot = await store.GetSnapshotAsync("TestOp");
        snapshot.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task ShouldHandle_FilteredExceptions_DoesNotCountNonMatchingFailures()
    {
        var store = new InMemoryCircuitBreakerStateStore();
        var strategy = CreateStrategy(
            new CircuitBreakerOptions
            {
                FailureThreshold = 1,
                ShouldHandle = ex => ex is TimeoutException
            },
            store);

        // ArgumentException should NOT trip the circuit
        try
        {
            await strategy.ExecuteAsync<int>(
                (ctx, ct) => throw new ArgumentException("wrong arg"),
                CreateContext(), CancellationToken.None);
        }
        catch (ArgumentException) { }

        var snapshot = await store.GetSnapshotAsync("TestOp");
        snapshot.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task OperationKey_OverridesOperationName_ForCircuitKey()
    {
        var store = new InMemoryCircuitBreakerStateStore();
        var strategy = CreateStrategy(
            new CircuitBreakerOptions { FailureThreshold = 1, BreakDuration = TimeSpan.FromMinutes(5) },
            store);

        var context = new ResilienceContext
        {
            OperationName = "GetUser",
            OperationKey = "external-api"
        };

        try
        {
            await strategy.ExecuteAsync<int>(
                (ctx, ct) => throw new InvalidOperationException("fail"),
                context, CancellationToken.None);
        }
        catch (InvalidOperationException) { }

        var snapshot = await store.GetSnapshotAsync("external-api");
        snapshot.State.Should().Be(CircuitState.Open);

        // OperationName key should NOT have state
        var opNameSnapshot = await store.GetSnapshotAsync("GetUser");
        opNameSnapshot.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public void Order_Is300()
    {
        var strategy = CreateStrategy();
        strategy.Order.Should().Be(StrategyOrder.CircuitBreaker);
    }
    [Fact]
    public async Task HalfOpenCircuit_ConcurrentRequestDuringProbe_IsRejected()
    {
        var store = new InMemoryCircuitBreakerStateStore();
        var options = new CircuitBreakerOptions { FailureThreshold = 1, BreakDuration = TimeSpan.Zero };
        var strategy = CreateStrategy(options, store);

        // Trip the circuit.
        try
        {
            await strategy.ExecuteAsync<int>(
                (ctx, ct) => throw new InvalidOperationException("fail"),
                CreateContext(), CancellationToken.None);
        }
        catch (InvalidOperationException) { }

        // BreakDuration elapsed (zero) — the first request wins the probe and holds it in flight.
        var probeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probeTask = strategy.ExecuteAsync<int>(
            async (ctx, ct) =>
            {
                probeStarted.SetResult();
                await probeRelease.Task;
                return 1;
            },
            CreateContext(), CancellationToken.None);

        await probeStarted.Task;

        // Half-open admits exactly one request: a concurrent one must be rejected.
        var concurrent = () => strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(2),
            CreateContext(), CancellationToken.None);
        await concurrent.Should().ThrowAsync<CircuitBrokenException>();

        // The probe completes successfully and closes the circuit.
        probeRelease.SetResult();
        (await probeTask).Should().Be(1);
        (await store.GetSnapshotAsync("TestOp")).State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task HalfOpenProbe_ExternallyCancelled_ReArmsCircuitToOpen()
    {
        var store = new InMemoryCircuitBreakerStateStore();
        var options = new CircuitBreakerOptions { FailureThreshold = 1, BreakDuration = TimeSpan.Zero };
        var strategy = CreateStrategy(options, store);

        try
        {
            await strategy.ExecuteAsync<int>(
                (ctx, ct) => throw new InvalidOperationException("fail"),
                CreateContext(), CancellationToken.None);
        }
        catch (InvalidOperationException) { }

        // The probe gets cancelled externally — the circuit must NOT stay HalfOpen
        // (it would reject every request forever) but re-arm to Open.
        using var cts = new CancellationTokenSource();
        var probe = () => strategy.ExecuteAsync<int>(
            (ctx, ct) =>
            {
                cts.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(1);
            },
            CreateContext(), cts.Token);
        await probe.Should().ThrowAsync<OperationCanceledException>();

        (await store.GetSnapshotAsync("TestOp")).State.Should().Be(CircuitState.Open);
    }
}
