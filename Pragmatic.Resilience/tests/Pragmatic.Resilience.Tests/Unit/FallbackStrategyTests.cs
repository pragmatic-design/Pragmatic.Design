using Pragmatic.Testing.Assertions;
using Pragmatic.Resilience.Pipeline;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Tests.Unit;

public class FallbackStrategyTests
{
    private static ResilienceContext CreateContext(string name = "TestOp")
        => new() { OperationName = name };

    [Fact]
    public async Task VoidFallback_OnVoidOperationFailure_RunsFallbackAndSwallows()
    {
        var fallbackRan = false;
        Exception? captured = null;

        var pipeline = new ResiliencePipelineBuilder()
            .AddFallback((ex, ct) =>
            {
                fallbackRan = true;
                captured = ex;
                return Task.CompletedTask;
            })
            .Build();

        // A typed AddFallback<T> would never intercept a void operation; the void overload must.
        await pipeline.ExecuteAsync(
            (ctx, ct) => throw new InvalidOperationException("void-boom"),
            CreateContext(), CancellationToken.None);

        fallbackRan.Should().BeTrue("the void fallback overload must intercept void operation failures");
        captured.Should().BeOfType<InvalidOperationException>();
    }

    [Fact]
    public async Task NoException_ReturnsOriginalResult()
    {
        var strategy = new FallbackStrategy<int>(new FallbackOptions<int>
        {
            FallbackAction = (ex, ctx, ct) => Task.FromResult(-1)
        });

        var result = await ((IResilienceStrategy)strategy).ExecuteAsync<int>(
            (ctx, ct) => Task.FromResult(42),
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
    }

    [Fact]
    public async Task Exception_ReturnsFallbackValue()
    {
        var strategy = new FallbackStrategy<int>(new FallbackOptions<int>
        {
            FallbackAction = (ex, ctx, ct) => Task.FromResult(-1)
        });

        var result = await ((IResilienceStrategy)strategy).ExecuteAsync<int>(
            (ctx, ct) => throw new InvalidOperationException("boom"),
            CreateContext(), CancellationToken.None);

        result.Should().Be(-1);
    }

    [Fact]
    public async Task ShouldHandle_FilteredExceptions_OnlyFallbackForMatching()
    {
        var strategy = new FallbackStrategy<int>(new FallbackOptions<int>
        {
            FallbackAction = (ex, ctx, ct) => Task.FromResult(-1),
            ShouldHandle = ex => ex is TimeoutException
        });

        // TimeoutException → fallback
        var result = await ((IResilienceStrategy)strategy).ExecuteAsync<int>(
            (ctx, ct) => throw new TimeoutException("slow"),
            CreateContext(), CancellationToken.None);

        result.Should().Be(-1);

        // InvalidOperationException → rethrow
        var act = () => ((IResilienceStrategy)strategy).ExecuteAsync<int>(
            (ctx, ct) => throw new InvalidOperationException("boom"),
            CreateContext(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task OnFallback_CallbackInvoked()
    {
        Exception? capturedEx = null;
        ResilienceContext? capturedCtx = null;

        var strategy = new FallbackStrategy<int>(new FallbackOptions<int>
        {
            FallbackAction = (ex, ctx, ct) => Task.FromResult(-1),
            OnFallback = (ex, ctx) =>
            {
                capturedEx = ex;
                capturedCtx = ctx;
            }
        });

        var context = CreateContext("MyOp");
        await ((IResilienceStrategy)strategy).ExecuteAsync<int>(
            (ctx, ct) => throw new InvalidOperationException("oops"),
            context, CancellationToken.None);

        capturedEx.Should().BeOfType<InvalidOperationException>();
        capturedCtx.Should().BeSameAs(context);
    }

    [Fact]
    public async Task FallbackAction_ReceivesExceptionAndContext()
    {
        Exception? receivedEx = null;
        string? receivedOp = null;

        var strategy = new FallbackStrategy<int>(new FallbackOptions<int>
        {
            FallbackAction = (ex, ctx, ct) =>
            {
                receivedEx = ex;
                receivedOp = ctx.OperationName;
                return Task.FromResult(99);
            }
        });

        var result = await ((IResilienceStrategy)strategy).ExecuteAsync<int>(
            (ctx, ct) => throw new InvalidOperationException("test error"),
            CreateContext("FallbackOp"), CancellationToken.None);

        result.Should().Be(99);
        receivedEx.Should().BeOfType<InvalidOperationException>();
        receivedOp.Should().Be("FallbackOp");
    }

    [Fact]
    public async Task ExternalCancellation_DoesNotFallback()
    {
        var strategy = new FallbackStrategy<int>(new FallbackOptions<int>
        {
            FallbackAction = (ex, ctx, ct) => Task.FromResult(-1)
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => ((IResilienceStrategy)strategy).ExecuteAsync<int>(
            async (ctx, ct) =>
            {
                await Task.Delay(1000, ct);
                return 42;
            },
            CreateContext(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task TypeMismatch_PassesThrough()
    {
        // FallbackStrategy<int> should pass through string operations
        var strategy = new FallbackStrategy<int>(new FallbackOptions<int>
        {
            FallbackAction = (ex, ctx, ct) => Task.FromResult(-1)
        });

        var act = () => ((IResilienceStrategy)strategy).ExecuteAsync<string>(
            (ctx, ct) => throw new InvalidOperationException("boom"),
            CreateContext(), CancellationToken.None);

        // Should rethrow since types don't match
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void Order_IsOutermost()
    {
        var strategy = new FallbackStrategy<int>(new FallbackOptions<int>
        {
            FallbackAction = (ex, ctx, ct) => Task.FromResult(-1)
        });

        strategy.Order.Should().Be(StrategyOrder.Fallback);
        // Last-resort semantics: fallback must be the most external strategy in the pipeline
        // (lowest Order wraps everything), otherwise retry/CB never observe the failures.
        strategy.Order.Should().BeLessThan(StrategyOrder.RateLimiter);
    }
}
