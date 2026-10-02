using Pragmatic.Testing.Assertions;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Tests.Unit;

public class TimeoutStrategyTests
{
    private static ResilienceContext CreateContext(string name = "TestOp")
        => new() { OperationName = name };

    [Fact]
    public async Task Optimistic_FastOperation_ReturnsResult()
    {
        var strategy = new TimeoutStrategy(new TimeoutOptions
        {
            Timeout = TimeSpan.FromSeconds(5),
            TimeoutType = TimeoutType.Optimistic
        });

        var result = await strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(42),
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
    }

    [Fact]
    public async Task Optimistic_SlowOperation_ThrowsTimeoutRejectedException()
    {
        var strategy = new TimeoutStrategy(new TimeoutOptions
        {
            Timeout = TimeSpan.FromMilliseconds(50),
            TimeoutType = TimeoutType.Optimistic
        });

        var act = () => strategy.ExecuteAsync<int>(
            async (ctx, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return 42;
            },
            CreateContext("SlowOp"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<TimeoutRejectedException>();
        ex.Which.OperationName.Should().Be("SlowOp");
        ex.Which.Timeout.Should().Be(TimeSpan.FromMilliseconds(50));
    }

    [Fact]
    public async Task Pessimistic_FastOperation_ReturnsResult()
    {
        var strategy = new TimeoutStrategy(new TimeoutOptions
        {
            Timeout = TimeSpan.FromSeconds(5),
            TimeoutType = TimeoutType.Pessimistic
        });

        var result = await strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(42),
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
    }

    [Fact]
    public async Task Pessimistic_SlowOperation_ThrowsTimeoutRejectedException()
    {
        var strategy = new TimeoutStrategy(new TimeoutOptions
        {
            Timeout = TimeSpan.FromMilliseconds(50),
            TimeoutType = TimeoutType.Pessimistic
        });

        var act = () => strategy.ExecuteAsync<int>(
            async (ctx, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return 42;
            },
            CreateContext("SlowOp"), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<TimeoutRejectedException>();
        ex.Which.OperationName.Should().Be("SlowOp");
    }

    [Fact]
    public async Task Optimistic_ExternalCancellation_PropagatesCancellation()
    {
        var strategy = new TimeoutStrategy(new TimeoutOptions
        {
            Timeout = TimeSpan.FromSeconds(30),
            TimeoutType = TimeoutType.Optimistic
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => strategy.ExecuteAsync<int>(
            async (ctx, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
                return 42;
            },
            CreateContext(), cts.Token);

        // External cancellation should propagate as OperationCanceledException, not TimeoutRejectedException
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Optimistic_OperationThrows_PropagatesException()
    {
        var strategy = new TimeoutStrategy(new TimeoutOptions
        {
            Timeout = TimeSpan.FromSeconds(5),
            TimeoutType = TimeoutType.Optimistic
        });

        var act = () => strategy.ExecuteAsync<int>(
            (ctx, ct) => throw new InvalidOperationException("boom"),
            CreateContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("boom");
    }

    [Fact]
    public void Order_Is100()
    {
        var strategy = new TimeoutStrategy(new TimeoutOptions());
        strategy.Order.Should().Be(StrategyOrder.Timeout);
    }
    [Fact]
    public async Task Pessimistic_ExternalCancellation_ThrowsOperationCanceled_NotTimeout()
    {
        var strategy = new TimeoutStrategy(new TimeoutOptions
        {
            Timeout = TimeSpan.FromSeconds(30),
            TimeoutType = TimeoutType.Pessimistic
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var act = () => strategy.ExecuteAsync<int>(
            async (ctx, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return 42;
            },
            CreateContext(), cts.Token);

        // External cancellation completes the internal delay race too — it must surface
        // as OperationCanceledException, never be misreported as a timeout.
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Optimistic_OperationInternalCancellation_PropagatesAsIs()
    {
        var strategy = new TimeoutStrategy(new TimeoutOptions
        {
            Timeout = TimeSpan.FromSeconds(30),
            TimeoutType = TimeoutType.Optimistic
        });

        // The operation cancels itself with its OWN token (unrelated to timeout/external):
        // that OCE must propagate, not be converted into TimeoutRejectedException.
        var act = () => strategy.ExecuteAsync<int>(
            (ctx, ct) => throw new OperationCanceledException("internal"),
            CreateContext(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
