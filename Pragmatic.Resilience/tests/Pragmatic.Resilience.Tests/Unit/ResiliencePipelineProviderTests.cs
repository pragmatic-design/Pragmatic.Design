using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Resilience.Configuration;
using Pragmatic.Resilience.Pipeline;
using Pragmatic.Resilience.State;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Tests.Unit;

public class ResiliencePipelineProviderTests
{
    private static ResiliencePipelineProvider CreateProvider(
        ResilienceOptions? options = null,
        ICircuitBreakerStateStore? store = null)
    {
        return new ResiliencePipelineProvider(
            Options.Create(options ?? new ResilienceOptions()),
            store ?? new InMemoryCircuitBreakerStateStore());
    }

    [Fact]
    public void GetPipeline_UnknownName_NoDefault_ReturnsPassthrough()
    {
        var provider = CreateProvider();

        var pipeline = provider.GetPipeline("unknown");

        pipeline.Should().BeSameAs(PassthroughPipeline.Instance);
    }

    [Fact]
    public void GetPipeline_UnknownName_WithDefault_ReturnsDefaultPipeline()
    {
        var options = new ResilienceOptions
        {
            Default = new ResiliencePolicyOptions
            {
                Retry = new RetryOptions { MaxRetries = 2 }
            }
        };

        var provider = CreateProvider(options);
        var pipeline = provider.GetPipeline("anything");

        pipeline.Should().NotBeSameAs(PassthroughPipeline.Instance);
    }

    [Fact]
    public void GetPipeline_NamedPolicy_BuildsFromConfiguration()
    {
        var options = new ResilienceOptions();
        options.Policies["external-api"] = new ResiliencePolicyOptions
        {
            Timeout = new TimeoutOptions { Timeout = TimeSpan.FromSeconds(10) },
            Retry = new RetryOptions { MaxRetries = 3 }
        };

        var provider = CreateProvider(options);
        var pipeline = provider.GetPipeline("external-api");

        pipeline.Should().NotBeSameAs(PassthroughPipeline.Instance);
    }

    [Fact]
    public async Task GetPipeline_NamedPolicy_WorksCorrectly()
    {
        var options = new ResilienceOptions();
        options.Policies["test-policy"] = new ResiliencePolicyOptions
        {
            Retry = new RetryOptions { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1), UseJitter = false }
        };

        var provider = CreateProvider(options);
        var pipeline = provider.GetPipeline("test-policy");
        var callCount = 0;

        var result = await pipeline.ExecuteAsync<int>(
            (ctx, ct) =>
            {
                callCount++;
                if (callCount == 1)
                    throw new InvalidOperationException("transient");
                return Task.FromResult(42);
            },
            new ResilienceContext { OperationName = "test" }, CancellationToken.None);

        result.Should().Be(42);
        callCount.Should().Be(2);
    }

    [Fact]
    public void GetPipeline_CachesPipeline()
    {
        var provider = CreateProvider();

        var pipeline1 = provider.GetPipeline("cached");
        var pipeline2 = provider.GetPipeline("cached");

        pipeline1.Should().BeSameAs(pipeline2);
    }

    [Fact]
    public void AddPolicy_FluentOverride_TakesPrecedence()
    {
        var options = new ResilienceOptions();
        options.Policies["my-policy"] = new ResiliencePolicyOptions
        {
            Retry = new RetryOptions { MaxRetries = 5 }
        };

        var provider = CreateProvider(options);

        // Override with fluent API
        provider.AddPolicy("my-policy", builder => builder.AddTimeout(t => t.Timeout = TimeSpan.FromSeconds(1)));

        var pipeline = provider.GetPipeline("my-policy");
        pipeline.Should().NotBeSameAs(PassthroughPipeline.Instance);
    }

    [Fact]
    public void GetPipelineForOperation_MappedOperation_ResolvesCorrectPolicy()
    {
        var options = new ResilienceOptions();
        options.Policies["external"] = new ResiliencePolicyOptions
        {
            Retry = new RetryOptions { MaxRetries = 3 }
        };

        var provider = CreateProvider(options);
        provider.MapOperation("PlaceOrder", "external");

        var pipeline = provider.GetPipelineForOperation("PlaceOrder");
        pipeline.Should().NotBeSameAs(PassthroughPipeline.Instance);
    }

    [Fact]
    public void GetPipelineForOperation_UnmappedOperation_FallsBackToOperationName()
    {
        var provider = CreateProvider();

        var pipeline = provider.GetPipelineForOperation("SomeOperation");
        pipeline.Should().BeSameAs(PassthroughPipeline.Instance);
    }

    [Fact]
    public void CaseInsensitive_PolicyNames()
    {
        var options = new ResilienceOptions();
        options.Policies["External-API"] = new ResiliencePolicyOptions
        {
            Retry = new RetryOptions { MaxRetries = 1 }
        };

        var provider = CreateProvider(options);

        var p1 = provider.GetPipeline("external-api");
        var p2 = provider.GetPipeline("EXTERNAL-API");

        p1.Should().BeSameAs(p2);
        p1.Should().NotBeSameAs(PassthroughPipeline.Instance);
    }
}
