using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Gateway.Resilience;
using Pragmatic.Resilience.State;
using Xunit;

namespace Pragmatic.Gateway.Tests.Resilience;

/// <summary>
///     Unit tests for <see cref="GatewayResilienceExtensions" /> DI registration. These cover
///     service-collection wiring only; the YARP pipeline insertion (<c>UseGatewayResilience</c>)
///     requires a live reverse-proxy builder and is out of scope for pure-unit tests.
/// </summary>
public sealed class GatewayResilienceExtensionsTests
{
    [Fact]
    public void AddGatewayResilience_WithOptions_RegistersProvidedOptionsInstance()
    {
        var options = new GatewayResilienceOptions();
        var services = new ServiceCollection();

        services.AddGatewayResilience(options);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<GatewayResilienceOptions>().Should().BeSameAs(options);
    }

    [Fact]
    public void AddGatewayResilience_RegistersCircuitBreakerStateStore()
    {
        var services = new ServiceCollection();

        services.AddGatewayResilience(new GatewayResilienceOptions());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ICircuitBreakerStateStore>()
            .Should().BeOfType<InMemoryCircuitBreakerStateStore>();
    }

    [Fact]
    public void AddGatewayResilience_RegistersProxyResilienceMiddlewareAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddGatewayResilience(new GatewayResilienceOptions());

        // ProxyResilienceMiddleware also requires ILogger<> (supplied by the host) to activate,
        // so assert on the registration descriptor rather than resolving a live instance.
        var descriptor = services.Single(d => d.ServiceType == typeof(ProxyResilienceMiddleware));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddGatewayResilience_StateStoreIsSingleton()
    {
        var services = new ServiceCollection();
        services.AddGatewayResilience(new GatewayResilienceOptions());

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<ICircuitBreakerStateStore>();
        var second = provider.GetRequiredService<ICircuitBreakerStateStore>();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void AddGatewayResilience_WithConfigureCallback_AppliesConfiguration()
    {
        var services = new ServiceCollection();

        services.AddGatewayResilience(o =>
        {
            o.Enabled = false;
            o.Default.FailureThreshold = 42;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<GatewayResilienceOptions>();
        options.Enabled.Should().BeFalse();
        options.Default.FailureThreshold.Should().Be(42);
    }

    [Fact]
    public void AddGatewayResilience_WithNullConfigureCallback_UsesDefaults()
    {
        var services = new ServiceCollection();

        services.AddGatewayResilience(configure: null);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<GatewayResilienceOptions>().Should().NotBeNull();
    }

    [Fact]
    public void AddGatewayResilience_ReturnsSameServiceCollectionForChaining()
    {
        var services = new ServiceCollection();

        var returned = services.AddGatewayResilience(new GatewayResilienceOptions());

        returned.Should().BeSameAs(services);
    }
}
