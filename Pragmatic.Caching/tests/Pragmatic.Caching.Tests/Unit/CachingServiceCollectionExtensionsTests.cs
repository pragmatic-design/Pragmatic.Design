using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Caching.Extensions;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

public class CachingServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPragmaticCaching_RegistersICacheStack()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();

        services.AddPragmaticCaching();

        var provider = services.BuildServiceProvider();
        var cacheStack = provider.GetService<ICacheStack>();
        cacheStack.Should().NotBeNull();
        cacheStack.Should().BeOfType<HybridCacheStack>();
    }

    [Fact]
    public void AddPragmaticCaching_RegistersAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();

        services.AddPragmaticCaching();

        var descriptor = services.Should().Contain(d => d.ServiceType == typeof(ICacheStack)).Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddPragmaticCaching_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();

        var returned = services.AddPragmaticCaching();

        returned.Should().BeSameAs(services);
    }

    [Fact]
    public void AddPragmaticCaching_WithConfigure_ReturnsServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();

        var returned = services.AddPragmaticCaching(o => o.DefaultDuration = TimeSpan.FromMinutes(5));

        returned.Should().BeSameAs(services);
    }

    [Fact]
    public void AddPragmaticCaching_NullServices_Throws()
    {
        IServiceCollection services = null!;

        var act = () => services.AddPragmaticCaching();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddPragmaticCaching_NullConfigure_Throws()
    {
        var services = new ServiceCollection();

        var act = () => services.AddPragmaticCaching((Action<CachingOptions>)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddPragmaticCaching_DoesNotDuplicateRegistration()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();

        services.AddPragmaticCaching();
        services.AddPragmaticCaching();

        var registrations = services.Where(d => d.ServiceType == typeof(ICacheStack)).ToList();
        registrations.Should().HaveCount(1);
    }
}
