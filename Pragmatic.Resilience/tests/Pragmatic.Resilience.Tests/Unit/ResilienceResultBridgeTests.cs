using Pragmatic.Testing.Assertions;
using Pragmatic.Tests.Generated;
using Pragmatic.Resilience.Bridge;
using Pragmatic.Resilience.Errors;
using Pragmatic.Resilience.Pipeline;
using Pragmatic.Resilience.Strategies;
using Pragmatic.Result;

namespace Pragmatic.Resilience.Tests.Unit;

public class ResilienceResultBridgeTests
{
    private static IResiliencePipeline Passthrough => PassthroughPipeline.Instance;

    // ── Generic overload: success ──────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsResultAsync_Generic_OperationSucceeds_ReturnsSuccessWithValue()
    {
        var result = await Passthrough.ExecuteAsResultAsync(_ => Task.FromResult(42));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public async Task ExecuteAsResultAsync_Generic_PassesCancellationTokenToOperation()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken received = default;

        await Passthrough.ExecuteAsResultAsync(
            ct => { received = ct; return Task.FromResult(1); },
            cts.Token);

        received.Should().Be(cts.Token);
    }

    // ── Hedging exhaustion → error mapping (regression: was unreachable) ───

    [Fact]
    public async Task ExecuteAsResultAsync_HedgingExhausted_MapsToHedgingExhaustedError()
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddHedging(o =>
            {
                o.MaxAttempts = 2;
                o.Delay = TimeSpan.FromMilliseconds(5);
            })
            .Build();

        var result = await pipeline.ExecuteAsResultAsync<int>(
            _ => throw new InvalidOperationException("boom"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeOfType<HedgingExhaustedError>();
        ((HedgingExhaustedError)result.Error).MaxAttempts.Should().Be(2);
    }

    [Fact]
    public void TryMapToError_NonResilienceException_ReturnsFalse()
    {
        ResilienceResultBridge.TryMapToError(new InvalidOperationException(), out var error)
            .Should().BeFalse();
        error.Should().BeNull();
    }

    // ── Generic overload: exception → error mapping ────────────────────────

    [Fact]
    public async Task ExecuteAsResultAsync_Generic_CircuitBroken_ReturnsCircuitBrokenError()
    {
        var result = await Passthrough.ExecuteAsResultAsync<int>(
            _ => throw new CircuitBrokenException("payments", TimeSpan.FromSeconds(30)));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<CircuitBrokenError>()
            .Which.Should().BeEquivalentTo(new CircuitBrokenError("payments", TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task ExecuteAsResultAsync_Generic_BulkheadRejected_ReturnsBulkheadRejectedError()
    {
        var result = await Passthrough.ExecuteAsResultAsync<int>(
            _ => throw new BulkheadRejectedException("orders", 8));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<BulkheadRejectedError>()
            .Which.Should().BeEquivalentTo(new BulkheadRejectedError("orders", 8));
    }

    [Fact]
    public async Task ExecuteAsResultAsync_Generic_TimeoutRejected_ReturnsTimeoutError()
    {
        var result = await Passthrough.ExecuteAsResultAsync<int>(
            _ => throw new TimeoutRejectedException("slow-op", TimeSpan.FromSeconds(5)));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<TimeoutError>()
            .Which.Should().BeEquivalentTo(new TimeoutError("slow-op", TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task ExecuteAsResultAsync_Generic_RetryExhausted_ReturnsRetryExhaustedError()
    {
        var inner = new InvalidOperationException("transient");

        var result = await Passthrough.ExecuteAsResultAsync<int>(
            _ => throw new RetryExhaustedException("fetch", 3, inner));

        result.IsFailure.Should().BeTrue();
        var error = result.Error.Should().BeOfType<RetryExhaustedError>().Subject;
        error.OperationName.Should().Be("fetch");
        error.Attempts.Should().Be(3);
        error.LastException.Should().BeSameAs(inner);
    }

    [Fact]
    public async Task ExecuteAsResultAsync_Generic_RateLimitRejected_ReturnsRateLimitRejectedError()
    {
        var result = await Passthrough.ExecuteAsResultAsync<int>(
            _ => throw new RateLimitRejectedException(100, TimeSpan.FromSeconds(1)));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<RateLimitRejectedError>()
            .Which.Should().BeEquivalentTo(new RateLimitRejectedError(100, TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task ExecuteAsResultAsync_Generic_UnhandledException_PropagatesOut()
    {
        var act = () => Passthrough.ExecuteAsResultAsync<int>(
            _ => throw new InvalidOperationException("not a resilience exception"));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── Void overload: success ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsResultAsync_Void_OperationSucceeds_ReturnsSuccess()
    {
        var executed = false;

        var result = await Passthrough.ExecuteAsResultAsync(_ =>
        {
            executed = true;
            return Task.CompletedTask;
        });

        result.IsSuccess.Should().BeTrue();
        executed.Should().BeTrue();
    }

    // ── Void overload: exception → error mapping ───────────────────────────

    [Fact]
    public async Task ExecuteAsResultAsync_Void_CircuitBroken_ReturnsCircuitBrokenError()
    {
        var result = await Passthrough.ExecuteAsResultAsync(
            Func(() => throw new CircuitBrokenException("payments", TimeSpan.FromSeconds(30))));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<CircuitBrokenError>();
    }

    [Fact]
    public async Task ExecuteAsResultAsync_Void_BulkheadRejected_ReturnsBulkheadRejectedError()
    {
        var result = await Passthrough.ExecuteAsResultAsync(
            Func(() => throw new BulkheadRejectedException("orders", 4)));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<BulkheadRejectedError>();
    }

    [Fact]
    public async Task ExecuteAsResultAsync_Void_TimeoutRejected_ReturnsTimeoutError()
    {
        var result = await Passthrough.ExecuteAsResultAsync(
            Func(() => throw new TimeoutRejectedException("slow", TimeSpan.FromSeconds(2))));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<TimeoutError>();
    }

    [Fact]
    public async Task ExecuteAsResultAsync_Void_RetryExhausted_ReturnsRetryExhaustedError()
    {
        var result = await Passthrough.ExecuteAsResultAsync(
            Func(() => throw new RetryExhaustedException("fetch", 2, new TimeoutException())));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<RetryExhaustedError>();
    }

    [Fact]
    public async Task ExecuteAsResultAsync_Void_RateLimitRejected_ReturnsRateLimitRejectedError()
    {
        var result = await Passthrough.ExecuteAsResultAsync(
            Func(() => throw new RateLimitRejectedException(50, TimeSpan.FromSeconds(1))));

        result.IsFailure.Should().BeTrue();
        result.Error.Should().BeOfType<RateLimitRejectedError>();
    }

    [Fact]
    public async Task ExecuteAsResultAsync_Void_UnhandledException_PropagatesOut()
    {
        var act = () => Passthrough.ExecuteAsResultAsync(
            Func(() => throw new InvalidOperationException("boom")));

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>Helper that turns a throwing void body into a <see cref="Func{T, TResult}"/> for the void overload.</summary>
    private static Func<CancellationToken, Task> Func(Action body)
        => _ => { body(); return Task.CompletedTask; };
}
