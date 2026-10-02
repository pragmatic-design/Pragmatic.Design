using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;

namespace Pragmatic.Endpoints.AspNetCore;

/// <summary>
///     Output cache store backed by Pragmatic.Caching's ICacheStack.
///     When registered, ASP.NET Core's [ResponseCache] uses the same distributed
///     cache backend (Redis, SQL, etc.) as [Cacheable] on domain actions.
/// </summary>
/// <remarks>
///     <para>
///         Register with <c>services.UseOutputCacheFromPragmaticCaching()</c>.
///     </para>
///     <para>
///         Cache keys are prefixed with <c>outputcache:</c> to avoid collisions
///         with business-layer cache entries.
///     </para>
///     <para>
///         Requires <c>Pragmatic.Caching</c> to be registered in DI (AddPragmaticCaching).
///         If ICacheStack is not available, operations are no-ops (falls through to default).
///     </para>
/// </remarks>
public sealed class PragmaticOutputCacheStore(IServiceProvider serviceProvider) : IOutputCacheStore
{
    private const string KeyPrefix = "outputcache:";
    private const string TagPrefix = "outputcache:tag:";

    // Thread-safe lazy resolution via Lazy<T> (LazyThreadSafetyMode.ExecutionAndPublication).
    // Category-aware ICacheStack (OutputCache category, fallback to default).
    private readonly Lazy<Pragmatic.Caching.ICacheStack?> _cacheResolver = new(
        () => Pragmatic.Caching.CacheStackProvider.ForCategoryOrNull<Pragmatic.Caching.CacheCategories.OutputCache>(serviceProvider));

    public async ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var cache = ResolveCacheStack();
        if (cache is null) return null;

        return await cache.GetAsync<byte[]>(KeyPrefix + key, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SetAsync(
        string key, byte[] value, string[]? tags, TimeSpan validFor,
        CancellationToken cancellationToken)
    {
        var cache = ResolveCacheStack();
        if (cache is null) return;

        var options = new Pragmatic.Caching.CacheEntryOptions
        {
            Duration = validFor,
            Tags = tags is { Length: > 0 }
                ? [..tags.Select(t => TagPrefix + t)]
                : []
        };

        await cache.SetAsync(KeyPrefix + key, value, options, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
    {
        var cache = ResolveCacheStack();
        if (cache is null) return;

        await cache.InvalidateByTagAsync(TagPrefix + tag, cancellationToken).ConfigureAwait(false);
    }

    private Pragmatic.Caching.ICacheStack? ResolveCacheStack() => _cacheResolver.Value;
}
