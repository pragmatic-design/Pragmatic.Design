using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Caching.Extensions;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     Verifies that <see cref="CachingOptions"/> flags configured via
///     <c>AddPragmaticCaching</c> actually flow through DI to <see cref="IOptions{TOptions}"/>.
/// </summary>
public class CachingOptionsDependencyInjectionTests
{
    private static CachingOptions ResolveOptions(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<CachingOptions>>().Value;
    }

    [Fact]
    public void AddPragmaticCaching_Default_EnableQueryCaching_IsTrue()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching();

        ResolveOptions(services).EnableQueryCaching.Should().BeTrue();
    }

    [Fact]
    public void AddPragmaticCaching_Default_EnableEventInvalidation_IsTrue()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching();

        ResolveOptions(services).EnableEventInvalidation.Should().BeTrue();
    }

    [Fact]
    public void AddPragmaticCaching_ConfigureCallback_EnableQueryCachingDisabled_FlowsToOptions()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching(o => o.EnableQueryCaching = false);

        ResolveOptions(services).EnableQueryCaching.Should().BeFalse();
    }

    [Fact]
    public void AddPragmaticCaching_ConfigureCallback_EnableEventInvalidationDisabled_FlowsToOptions()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching(o => o.EnableEventInvalidation = false);

        ResolveOptions(services).EnableEventInvalidation.Should().BeFalse();
    }

    [Fact]
    public void AddPragmaticCaching_BuilderWithDefaultOptions_FlagsFlowToOptions()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching(cache => cache.WithDefaultOptions(o =>
        {
            o.EnableQueryCaching = false;
            o.EnableEventInvalidation = false;
            o.DefaultDuration = TimeSpan.FromMinutes(42);
        }));

        var options = ResolveOptions(services);
        options.EnableQueryCaching.Should().BeFalse();
        options.EnableEventInvalidation.Should().BeFalse();
        options.DefaultDuration.Should().Be(TimeSpan.FromMinutes(42));
    }

    [Fact]
    public void AddPragmaticCaching_BuilderWithoutDefaultOptions_FlagsRemainTrue()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        services.AddPragmaticCaching(cache => cache.ForCategory<SomeCategory>(o => o.KeyPrefix = "x:"));

        var options = ResolveOptions(services);
        options.EnableQueryCaching.Should().BeTrue();
        options.EnableEventInvalidation.Should().BeTrue();
    }

    private sealed class SomeCategory;
}
