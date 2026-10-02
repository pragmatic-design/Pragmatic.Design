using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Caching.Tests.Unit;

/// <summary>
///     What an ambient value looks like from inside the factory a cache stack invokes.
/// </summary>
/// <remarks>
///     <para>
///         A cacheable query is executed <b>inside</b> the factory passed to <c>GetOrSetAsync</c>
///         (<c>EfCoreQueryExecutor.cs:157</c>): the filter map, and therefore the tenant read, happen
///         there rather than on the caller's own frame. Anything the framework keeps in an
///         <c>AsyncLocal</c> — the tenant, the filter toggle — is only visible to code the write flows
///         into, so whether it reaches that delegate decides whether a cached read filters correctly.
///     </para>
///     <para>
///         ⚠️ Turning query caching off and watching failures disappear shows <em>where</em> a failure
///         lives; it does not show whether the factory can see the flow. These cases ask that directly,
///         with no database and no host, so the answer is a property of the cache stack rather than an
///         inference from an application's behaviour.
///     </para>
/// </remarks>
public sealed class WhatTheCacheFactorySeesTests : IDisposable
{
    private static readonly AsyncLocal<string?> Ambient = new();

    private readonly ServiceProvider _provider;
    private readonly HybridCacheStack _cache;

    public WhatTheCacheFactorySeesTests()
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        _provider = services.BuildServiceProvider();
        _cache = new HybridCacheStack(_provider.GetRequiredService<HybridCache>());
    }

    public void Dispose() => _provider.Dispose();

    /// <summary>On a miss, the factory runs — and this says whether it runs inside the caller's flow.</summary>
    [Fact]
    public async Task OnAMiss_TheFactorySeesTheAmbientValue()
    {
        Ambient.Value = "acme";

        var seen = await _cache.GetOrSetAsync(
            $"miss:{Guid.NewGuid():N}",
            ct => ValueTask.FromResult(CacheFactoryResult<string>.Cache(Ambient.Value ?? "<none>")),
            new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) });

        seen.Should().Be("acme",
            "the factory is where a cacheable query actually runs, so anything the request keeps in "
            + "the flow has to be readable there or the query runs without it");
    }

    /// <summary>
    ///     The control: the value really is ambient, and the test is not just reading a closure.
    /// </summary>
    /// <remarks>
    ///     Read through the same <c>AsyncLocal</c>, on the caller's own frame, immediately before the
    ///     call above would run. Without this, a factory returning "acme" would prove nothing — it
    ///     would be indistinguishable from a captured local.
    /// </remarks>
    [Fact]
    public void TheCallersOwnFrame_SeesIt()
    {
        Ambient.Value = "acme";

        Ambient.Value.Should().Be("acme");
    }

    /// <summary>
    ///     And the second control: a value set <b>after</b> the entry was cached must not reach a hit.
    /// </summary>
    /// <remarks>
    ///     A cache hit does not run the factory at all, so it cannot observe anything — which is the
    ///     property that makes a stale entry stale. Stated because it is the half that stays true
    ///     whatever the answer above is: once a page is cached under a key, the flow of whoever reads
    ///     it next is irrelevant.
    /// </remarks>
    [Fact]
    public async Task OnAHit_TheFactoryDoesNotRunAtAll()
    {
        var key = $"hit:{Guid.NewGuid():N}";
        var calls = 0;

        Ambient.Value = "acme";
        await _cache.GetOrSetAsync(key, Factory, new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) });

        Ambient.Value = "umbrella";
        var second = await _cache.GetOrSetAsync(key, Factory, new CacheEntryOptions { Duration = TimeSpan.FromMinutes(5) });

        calls.Should().Be(1, "the second call is a hit");
        second.Should().Be("acme", "a hit serves what was stored, whatever the current flow says");

        ValueTask<CacheFactoryResult<string>> Factory(CancellationToken ct)
        {
            calls++;
            return ValueTask.FromResult(CacheFactoryResult<string>.Cache(Ambient.Value ?? "<none>"));
        }
    }
}
