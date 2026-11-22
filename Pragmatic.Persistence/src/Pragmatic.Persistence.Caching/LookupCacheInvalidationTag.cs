namespace Pragmatic.Persistence.Caching;

/// <summary>
///     The cache tag a lookup's reload invalidates, so the other nodes hear it (#763): <c>pragmatic:lookup:{Entity}</c>, the entity named by
///     its full type name.
/// </summary>
/// <remarks>
///     One place builds it and one parses it: <see cref="LookupCacheReloadObserver" /> invalidates it after reloading this node's cache,
///     and <see cref="LookupCacheInvalidationObserver" /> reads it on another node to reload the same lookup there.
/// </remarks>
public static class LookupCacheInvalidationTag
{
    /// <summary>What every lookup tag starts with.</summary>
    public const string Prefix = "pragmatic:lookup:";

    /// <summary>The tag of <paramref name="entity" />'s lookup cache.</summary>
    public static string For(Type entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return Prefix + entity.FullName;
    }
}
