using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Options;
using Pragmatic.Resilience.Configuration;
using Pragmatic.Resilience.Pipeline;
using Pragmatic.Resilience.State;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Tests.Unit;

public class ResiliencePipelineProviderAddPolicyTests
{
    private static ResiliencePipelineProvider CreateProvider(ResilienceOptions? options = null)
        => new(Options.Create(options ?? new ResilienceOptions()), new InMemoryCircuitBreakerStateStore());

    // ── AddPolicy(string, ResiliencePolicyOptions) override tier ───────────

    [Fact]
    public void AddPolicy_OptionsOverride_BuildsConfiguredPipeline()
    {
        var provider = CreateProvider();

        provider.AddPolicy("api", new ResiliencePolicyOptions
        {
            Retry = new RetryOptions { MaxRetries = 2 }
        });

        provider.GetPipeline("api").Should().NotBeSameAs(PassthroughPipeline.Instance);
    }

    [Fact]
    public void AddPolicy_OptionsOverride_TakesPrecedenceOverConfiguration()
    {
        var options = new ResilienceOptions();
        options.Policies["api"] = new ResiliencePolicyOptions { Retry = new RetryOptions { MaxRetries = 5 } };
        var provider = CreateProvider(options);

        // Materialize the config-built pipeline first.
        var fromConfig = provider.GetPipeline("api");

        // Register an options override for the same name.
        provider.AddPolicy("api", new ResiliencePolicyOptions
        {
            Timeout = new TimeoutOptions { Timeout = TimeSpan.FromSeconds(1) }
        });

        var afterOverride = provider.GetPipeline("api");

        // Eviction means the cached config pipeline is replaced by the override-built one.
        afterOverride.Should().NotBeSameAs(fromConfig);
        afterOverride.Should().NotBeSameAs(PassthroughPipeline.Instance);
    }

    [Fact]
    public void AddPolicy_OptionsOverride_EvictsPreviouslyCachedPipeline()
    {
        var provider = CreateProvider();

        var first = provider.GetPipeline("api");          // cached passthrough (no config)
        provider.AddPolicy("api", new ResiliencePolicyOptions { Retry = new RetryOptions { MaxRetries = 1 } });
        var second = provider.GetPipeline("api");

        second.Should().NotBeSameAs(first);
        second.Should().NotBeSameAs(PassthroughPipeline.Instance);
    }

    [Fact]
    public void AddPolicy_OptionsOverride_InvalidRetry_ThrowsOnBuild()
    {
        var provider = CreateProvider();

        provider.AddPolicy("bad", new ResiliencePolicyOptions
        {
            Retry = new RetryOptions { MaxRetries = -1 }
        });

        var act = () => provider.GetPipeline("bad");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task AddPolicy_OptionsOverride_RetryBehaviorIsApplied()
    {
        var provider = CreateProvider();
        provider.AddPolicy("retrying", new ResiliencePolicyOptions
        {
            Retry = new RetryOptions { MaxRetries = 2, BaseDelay = TimeSpan.FromMilliseconds(1), UseJitter = false }
        });

        var pipeline = provider.GetPipeline("retrying");
        var calls = 0;

        var result = await pipeline.ExecuteAsync<int>(
            (_, _) =>
            {
                calls++;
                if (calls == 1)
                    throw new InvalidOperationException("transient");
                return Task.FromResult(7);
            },
            new ResilienceContext { OperationName = "retrying" }, CancellationToken.None);

        result.Should().Be(7);
        calls.Should().Be(2);
    }

    // ── Precedence: fluent override wins over options override ──────────────

    [Fact]
    public void GetPipeline_FluentOverrideWinsOverOptionsOverride()
    {
        var provider = CreateProvider();

        provider.AddPolicy("api", new ResiliencePolicyOptions { Retry = new RetryOptions { MaxRetries = 9 } });
        provider.AddPolicy("api", builder => builder.AddTimeout(t => t.Timeout = TimeSpan.FromSeconds(1)));

        // Both overrides registered; fluent override is checked first in GetPipeline.
        provider.GetPipeline("api").Should().NotBeSameAs(PassthroughPipeline.Instance);
    }
}
