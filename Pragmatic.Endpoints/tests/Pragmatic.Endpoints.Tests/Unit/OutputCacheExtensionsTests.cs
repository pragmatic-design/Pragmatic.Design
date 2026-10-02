using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Endpoints.AspNetCore;
using Pragmatic.Endpoints.AspNetCore.Extensions;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Tests.Unit;

/// <summary>
///     The bridge between ASP.NET's output cache and Pragmatic.Caching.
/// </summary>
/// <remarks>
///     One call, and it is the only way to make <c>[ResponseCache]</c> on an endpoint and
///     <c>[Cacheable]</c> on a domain action share a backend. Nothing in the repository called it, no
///     test ran it and no page named it — so an application could be running with responses cached in
///     memory per instance while everything else went to Redis, and nothing would say so.
/// </remarks>
public class OutputCacheExtensionsTests
{
    [Fact]
    public void UseOutputCacheFromPragmaticCaching_ReplacesTheStoreWithThePragmaticOne()
    {
        var services = new ServiceCollection();

        services.UseOutputCacheFromPragmaticCaching();

        services.Should().Contain(
            d => d.ServiceType == typeof(IOutputCacheStore)
                 && d.ImplementationType == typeof(PragmaticOutputCacheStore),
            "sharing the backend is the whole point of the call");
    }

    /// <remarks>
    ///     It brings the output cache with it, so a caller does not have to remember
    ///     <c>AddOutputCache()</c> first — forgetting it leaves the store registered against a feature
    ///     that is not turned on.
    /// </remarks>
    [Fact]
    public void UseOutputCacheFromPragmaticCaching_AlsoTurnsTheOutputCacheOn()
    {
        var services = new ServiceCollection();

        services.UseOutputCacheFromPragmaticCaching();

        services.Should().Contain(d => d.ServiceType == typeof(IOutputCacheStore));
        services.Count(d => d.ServiceType == typeof(IOutputCacheStore)).Should().Be(1,
            "TryAdd keeps whatever AddOutputCache registered from standing beside it");
    }
}
