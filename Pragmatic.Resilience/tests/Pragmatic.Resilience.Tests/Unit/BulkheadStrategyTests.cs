using Pragmatic.Testing.Assertions;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Tests.Unit;

public class BulkheadStrategyTests
{
    private static ResilienceContext CreateContext(string name = "TestOp")
        => new() { OperationName = name };

    [Fact]
    public async Task WithinConcurrency_ExecutesNormally()
    {
        using var strategy = new BulkheadStrategy(new BulkheadOptions { MaxConcurrency = 2 });

        var result = await strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(42),
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
    }

    [Fact]
    public async Task ExceedsConcurrency_NoQueue_ThrowsBulkheadRejectedException()
    {
        using var strategy = new BulkheadStrategy(new BulkheadOptions { MaxConcurrency = 1, MaxQueuedActions = 0 });
        var gate = new TaskCompletionSource();

        // Occupy the only slot
        var runningTask = strategy.ExecuteAsync<int>(
            async (ctx, ct) =>
            {
                await gate.Task;
                return 1;
            },
            CreateContext(), CancellationToken.None);

        // Second call should be rejected
        var act = () => strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(2),
            CreateContext(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<BulkheadRejectedException>();
        ex.Which.MaxConcurrency.Should().Be(1);

        gate.SetResult();
        await runningTask;
    }

    [Fact]
    public async Task WithQueue_WaitsForSlot()
    {
        using var strategy = new BulkheadStrategy(new BulkheadOptions
        {
            MaxConcurrency = 1,
            MaxQueuedActions = 1,
            QueueTimeout = TimeSpan.FromSeconds(5)
        });

        var gate = new TaskCompletionSource();
        var secondStarted = false;

        var firstTask = strategy.ExecuteAsync<int>(
            async (ctx, ct) =>
            {
                await gate.Task;
                return 1;
            },
            CreateContext(), CancellationToken.None);

        var secondTask = strategy.ExecuteAsync<int>(
            (ctx, ct) =>
            {
                secondStarted = true;
                return Task.FromResult(2);
            },
            CreateContext(), CancellationToken.None);

        // Second hasn't started yet
        secondStarted.Should().BeFalse();

        // Release first — second should then execute
        gate.SetResult();
        await firstTask;
        var result = await secondTask;

        result.Should().Be(2);
        secondStarted.Should().BeTrue();
    }

    [Fact]
    public async Task QueueTimeout_Exceeded_ThrowsBulkheadRejectedException()
    {
        using var strategy = new BulkheadStrategy(new BulkheadOptions
        {
            MaxConcurrency = 1,
            MaxQueuedActions = 1,
            QueueTimeout = TimeSpan.FromMilliseconds(50)
        });

        var gate = new TaskCompletionSource();

        // Occupy the slot for a long time
        var holdTask = strategy.ExecuteAsync<int>(
            async (ctx, ct) =>
            {
                await gate.Task;
                return 1;
            },
            CreateContext(), CancellationToken.None);

        // Second call should queue, then timeout
        var act = () => strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(2),
            CreateContext(), CancellationToken.None);

        await act.Should().ThrowAsync<BulkheadRejectedException>();

        gate.SetResult();
        await holdTask;
    }

    [Fact]
    public async Task QueueFull_RejectsExcessWaiters()
    {
        using var strategy = new BulkheadStrategy(new BulkheadOptions
        {
            MaxConcurrency = 1,
            MaxQueuedActions = 1,
            QueueTimeout = TimeSpan.FromSeconds(5)
        });

        var gate = new TaskCompletionSource();

        // 1 running — holds the only slot
        var running = strategy.ExecuteAsync<int>(
            async (ctx, ct) =>
            {
                await gate.Task;
                return 1;
            },
            CreateContext(), CancellationToken.None);

        while (strategy.AvailableSlots > 0)
            await Task.Delay(5);

        // 1 queued — allowed to wait (queue depth 1)
        var queued = strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(2),
            CreateContext(), CancellationToken.None);

        while (strategy.QueuedCount < 1)
            await Task.Delay(5);

        // 3rd caller — queue is full → immediate rejection (MaxQueuedActions is now enforced)
        var act = () => strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(3),
            CreateContext(), CancellationToken.None);

        await act.Should().ThrowAsync<BulkheadRejectedException>();

        gate.SetResult();
        await running;
        (await queued).Should().Be(2);
    }

    [Fact]
    public async Task ReleasesSlot_AfterException()
    {
        using var strategy = new BulkheadStrategy(new BulkheadOptions { MaxConcurrency = 1 });

        try
        {
            await strategy.ExecuteAsync<int>(
                (ctx, ct) => throw new InvalidOperationException("boom"),
                CreateContext(), CancellationToken.None);
        }
        catch (InvalidOperationException) { }

        // Slot should be released — next call succeeds
        var result = await strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(42),
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
    }

    [Fact]
    public async Task AvailableSlots_ReflectsCurrentState()
    {
        using var strategy = new BulkheadStrategy(new BulkheadOptions { MaxConcurrency = 3 });
        strategy.AvailableSlots.Should().Be(3);

        var gate = new TaskCompletionSource();

        var task = strategy.ExecuteAsync<int>(
            async (ctx, ct) =>
            {
                await gate.Task;
                return 1;
            },
            CreateContext(), CancellationToken.None);

        // Give async task time to acquire semaphore
        await Task.Delay(10);
        strategy.AvailableSlots.Should().Be(2);

        gate.SetResult();
        await task;
        strategy.AvailableSlots.Should().Be(3);
    }

    [Fact]
    public void Order_Is200()
    {
        using var strategy = new BulkheadStrategy(new BulkheadOptions());
        strategy.Order.Should().Be(StrategyOrder.Bulkhead);
    }
}
