using Pragmatic.Testing.Assertions;
using Pragmatic.Resilience.Pipeline;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Tests.Unit;

public class PipelineBuilderTests
{
    private static ResilienceContext CreateContext(string name = "TestOp")
        => new() { OperationName = name };

    [Fact]
    public void Build_NoStrategies_ReturnsPassthrough()
    {
        var pipeline = new ResiliencePipelineBuilder().Build();

        pipeline.Should().BeOfType<PassthroughPipeline>();
    }

    [Fact]
    public void Build_WithStrategies_ReturnsResiliencePipeline()
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry()
            .AddTimeout()
            .Build();

        pipeline.Should().BeOfType<ResiliencePipeline>();
    }

    [Fact]
    public async Task Build_WithRetryConfig_AppliesOptions()
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(r =>
            {
                r.MaxRetries = 2;
                r.BaseDelay = TimeSpan.FromMilliseconds(1);
                r.UseJitter = false;
            })
            .Build();

        var callCount = 0;
        var result = await pipeline.ExecuteAsync<int>(
            (ctx, ct) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("transient");
                return Task.FromResult(42);
            },
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task Build_WithTimeout_AppliesTimeout()
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddTimeout(t => t.Timeout = TimeSpan.FromMilliseconds(50))
            .Build();

        var act = () => pipeline.ExecuteAsync<int>(
            async (ctx, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                return 42;
            },
            CreateContext(), CancellationToken.None);

        await act.Should().ThrowAsync<TimeoutRejectedException>();
    }

    [Fact]
    public async Task Build_RetryAndTimeout_ComposesCorrectly()
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddTimeout(t => t.Timeout = TimeSpan.FromSeconds(5))
            .AddRetry(r =>
            {
                r.MaxRetries = 2;
                r.BaseDelay = TimeSpan.FromMilliseconds(1);
                r.UseJitter = false;
            })
            .Build();

        var callCount = 0;
        var result = await pipeline.ExecuteAsync<int>(
            (ctx, ct) =>
            {
                callCount++;
                if (callCount < 2)
                    throw new InvalidOperationException("transient");
                return Task.FromResult(42);
            },
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
        callCount.Should().Be(2);
    }

    [Fact]
    public async Task PassthroughPipeline_ZeroOverhead()
    {
        var pipeline = PassthroughPipeline.Instance;
        var callCount = 0;

        var result = await pipeline.ExecuteAsync<int>(
            (ctx, ct) => { callCount++; return Task.FromResult(42); },
            CreateContext(), CancellationToken.None);

        result.Should().Be(42);
        callCount.Should().Be(1);
    }

    [Fact]
    public void PassthroughPipeline_IsSingleton()
    {
        PassthroughPipeline.Instance.Should().BeSameAs(PassthroughPipeline.Instance);
    }
}
