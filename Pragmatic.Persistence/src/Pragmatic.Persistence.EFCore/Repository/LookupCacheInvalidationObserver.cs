using Pragmatic.Caching;

namespace Pragmatic.Persistence.EFCore.Repository;

/// <summary>
///     Reloads a <c>[Lookup]</c> entity's cache on this node when another node broadcast that it reloaded its own (#763).
/// </summary>
/// <remarks>
///     <para>
///         The lookup caches are in memory, one per process, and no broadcast reaches them by itself: the writer's
///         <see cref="LookupCacheReloadInterceptor" /> reloads its own and invalidates <see cref="LookupCacheInvalidationTag" />, and this
///         hears the tag on every other node.
///     </para>
///     <para>
///         The tag names the entity and not the tenant, so a tenant-scoped lookup is reloaded for every tenant this node loaded at startup;
///         one that is not has its one cache reloaded. The reload goes straight to the loader, never through a save, so it broadcasts
///         nothing back.
///     </para>
/// </remarks>
public sealed class LookupCacheInvalidationObserver : ICacheInvalidationObserver
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, ILookupCacheLoader> _loaders;

    /// <summary>Creates the observer over the loaders the host registered, one per <c>[Lookup]</c> entity.</summary>
    public LookupCacheInvalidationObserver(IServiceProvider serviceProvider, IEnumerable<ILookupCacheLoader> loaders)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(loaders);
        _serviceProvider = serviceProvider;
        _loaders = loaders.ToDictionary(l => LookupCacheInvalidationTag.For(l.EntityType), StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public Task OnTagInvalidatedAsync(string tag, CancellationToken ct)
        => _loaders.TryGetValue(tag, out var loader) ? loader.ReloadAsync(_serviceProvider, null, ct) : Task.CompletedTask;

    /// <inheritdoc />
    /// <remarks>A lookup is invalidated by its tag only.</remarks>
    public Task OnKeyInvalidatedAsync(string key, CancellationToken ct) => Task.CompletedTask;
}
