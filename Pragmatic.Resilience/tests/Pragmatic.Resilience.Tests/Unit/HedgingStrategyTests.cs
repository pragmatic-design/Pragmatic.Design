using Pragmatic.Testing.Assertions;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Tests.Unit;

public class HedgingStrategyTests
{
    private static ResilienceContext CreateContext(string name = "TestOp")
        => new() { OperationName = name };

    [Fact]
    public async Task SingleAttempt_ExecutesNormally()
    {
        var strategy = new HedgingStrategy(new HedgingOptions { MaxAttempts = 1 });

        var result = await strategy.ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(42),
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
    }

    [Fact]
    public async Task AllAttemptsFail_ThrowsHedgingExhausted_WithInnerException()
    {
        var strategy = new HedgingStrategy(new HedgingOptions
        {
            MaxAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(10)
        });

        var act = () => strategy.ExecuteAsync<int>(
            (ctx, ct) => throw new InvalidOperationException("boom"),
            CreateContext(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<HedgingExhaustedException>();
        ex.Which.MaxAttempts.Should().Be(2);
        ex.Which.InnerException.Should().BeOfType<InvalidOperationException>();
        ex.Which.InnerException!.Message.Should().Be("boom");
    }

    [Fact]
    public async Task FirstAttemptSucceeds_ReturnsImmediately()
    {
        var strategy = new HedgingStrategy(new HedgingOptions
        {
            MaxAttempts = 3,
            Delay = TimeSpan.FromSeconds(10)
        });
        var callCount = 0;

        var result = await strategy.ExecuteAsync<int>(
            (ctx, ct) =>
            {
                Interlocked.Increment(ref callCount);
                return Task.FromResult(99);
            },
            CreateContext(), CancellationToken.None);

        result.Should().Be(99);
        // First attempt succeeds instantly, so hedged attempts should not be launched.
        callCount.Should().Be(1);
    }

    [Fact]
    public async Task FirstAttemptSlow_HedgedAttemptWins()
    {
        var strategy = new HedgingStrategy(new HedgingOptions
        {
            MaxAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(20)
        });
        var attemptIndex = 0;

        var result = await strategy.ExecuteAsync<int>(
            async (ctx, ct) =>
            {
                var myIndex = Interlocked.Increment(ref attemptIndex);
                if (myIndex == 1)
                {
                    // First attempt stalls long enough for the hedge to overtake it.
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                    return 1;
                }

                // Second (hedged) attempt returns fast.
                return 2;
            },
            CreateContext(), CancellationToken.None);

        result.Should().Be(2);
    }

    [Fact]
    public async Task AllAttemptsFail_ThrowsHedgingExhaustedException()
    {
        var strategy = new HedgingStrategy(new HedgingOptions
        {
            MaxAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(10)
        });

        var act = () => strategy.ExecuteAsync<int>(
            (ctx, ct) => throw new InvalidOperationException("boom"),
            CreateContext(), CancellationToken.None);

        var ex = await act.Should().ThrowAsync<HedgingExhaustedException>();
        ex.Which.MaxAttempts.Should().Be(2);
    }

    [Fact]
    public async Task FirstFails_SecondSucceeds_ReturnsSecond()
    {
        var strategy = new HedgingStrategy(new HedgingOptions
        {
            MaxAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(10)
        });
        var attemptIndex = 0;

        var result = await strategy.ExecuteAsync<int>(
            async (ctx, ct) =>
            {
                var myIndex = Interlocked.Increment(ref attemptIndex);
                if (myIndex == 1)
                    throw new InvalidOperationException("first fails");

                // Small delay so the first failure is observed before this succeeds.
                await Task.Delay(10, ct).ConfigureAwait(false);
                return 42;
            },
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
    }

    [Fact]
    public void MaxAttempts_LessThanOne_Throws()
    {
        var act = () => new HedgingStrategy(new HedgingOptions { MaxAttempts = 0 });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new HedgingStrategy(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Order_IsHedging()
    {
        var strategy = new HedgingStrategy(new HedgingOptions());

        strategy.Order.Should().Be(StrategyOrder.Hedging);
    }

    [Fact]
    public void Options_Defaults_AreSensible()
    {
        var options = new HedgingOptions();

        options.MaxAttempts.Should().Be(2);
        options.Delay.Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task CancellationToken_IsRespected()
    {
        var strategy = new HedgingStrategy(new HedgingOptions
        {
            MaxAttempts = 2,
            Delay = TimeSpan.FromMilliseconds(50)
        });
        using var cts = new CancellationTokenSource();

        var act = () => strategy.ExecuteAsync<int>(
            async (ctx, ct) =>
            {
                // Cancel after the operation starts, then observe cancellation.
                await cts.CancelAsync().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                return 1;
            },
            CreateContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void HedgingExhaustedException_ExposesMaxAttempts()
    {
        var ex = new HedgingExhaustedException(5);

        ex.MaxAttempts.Should().Be(5);
        ex.Message.Should().Contain("5");
    }
}
