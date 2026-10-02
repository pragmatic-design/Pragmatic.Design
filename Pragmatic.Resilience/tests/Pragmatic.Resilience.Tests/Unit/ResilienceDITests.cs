using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Resilience.Configuration;
using Pragmatic.Resilience.Pipeline;
using Pragmatic.Resilience.State;
using Pragmatic.Resilience.Strategies;

namespace Pragmatic.Resilience.Tests.Unit;

public class ResilienceDITests
{
    [Fact]
    public void AddPragmaticResilience_RegistersRequiredServices()
    {
        var services = new ServiceCollection();
        services.AddPragmaticResilience();

        var provider = services.BuildServiceProvider();

        provider.GetService<ICircuitBreakerStateStore>().Should().NotBeNull();
        provider.GetService<IResiliencePipelineProvider>().Should().NotBeNull();
    }

    [Fact]
    public void AddPragmaticResilience_StateStoreIsSingleton()
    {
        var services = new ServiceCollection();
        services.AddPragmaticResilience();

        var provider = services.BuildServiceProvider();

        var store1 = provider.GetRequiredService<ICircuitBreakerStateStore>();
        var store2 = provider.GetRequiredService<ICircuitBreakerStateStore>();

        store1.Should().BeSameAs(store2);
    }

    [Fact]
    public void AddPragmaticResilience_WithOptions_ConfiguresCorrectly()
    {
        var services = new ServiceCollection();
        services.AddPragmaticResilience(options =>
        {
            options.Policies["test"] = new ResiliencePolicyOptions
            {
                Retry = new RetryOptions { MaxRetries = 5 }
            };
        });

        var provider = services.BuildServiceProvider();
        var pipelineProvider = provider.GetRequiredService<IResiliencePipelineProvider>();

        var pipeline = pipelineProvider.GetPipeline("test");
        pipeline.Should().NotBeSameAs(PassthroughPipeline.Instance);
    }

    [Fact]
    public void AddResiliencePolicy_RegistersNamedPolicy()
    {
        var services = new ServiceCollection();
        services.AddPragmaticResilience();
        services.AddResiliencePolicy("external-api", policy =>
        {
            policy.Retry = new RetryOptions { MaxRetries = 3 };
            policy.Timeout = new TimeoutOptions { Timeout = TimeSpan.FromSeconds(10) };
        });

        var provider = services.BuildServiceProvider();
        var pipelineProvider = provider.GetRequiredService<IResiliencePipelineProvider>();

        var pipeline = pipelineProvider.GetPipeline("external-api");
        pipeline.Should().NotBeSameAs(PassthroughPipeline.Instance);
    }

    [Fact]
    public void AddPragmaticResilience_CustomStateStore_NotOverwritten()
    {
        var customStore = new InMemoryCircuitBreakerStateStore();
        var services = new ServiceCollection();
        services.AddSingleton<ICircuitBreakerStateStore>(customStore);
        services.AddPragmaticResilience();

        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<ICircuitBreakerStateStore>();

        store.Should().BeSameAs(customStore);
    }
}
