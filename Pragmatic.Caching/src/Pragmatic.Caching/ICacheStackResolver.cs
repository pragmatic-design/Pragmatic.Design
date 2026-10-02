namespace Pragmatic.Caching;

/// <summary>
///     Resolves the <see cref="ICacheStack"/> that a cache category is routed to.
/// </summary>
/// <remarks>
///     <para>
///         Category routing registers one keyed <see cref="ICacheStack"/> per category, each wrapping
///         the default stack with that category's key prefix and duration. Anything that reads or
///         invalidates on behalf of a declared type — the query executor for <c>[Cacheable]</c>, the
///         mutation invoker for <c>[InvalidatesCache]</c> — has to route through the same instance,
///         or it will address a different key namespace than the one the entry was written to.
///     </para>
///     <para>
///         Resolution takes a <see cref="Type"/> rather than a generic parameter because the category
///         arrives from <see cref="ICacheable.CacheCategory"/> or
///         <see cref="ICacheInvalidator.InvalidationCategory"/>, which the generator emits as a
///         compile-time <c>typeof(...)</c>. No reflection is performed on it: the type's
///         <see cref="Type.FullName"/> is the keyed-service key and nothing else is read.
///     </para>
/// </remarks>
public interface ICacheStackResolver
{
    /// <summary>
    ///     Whether <c>[Cacheable]</c> queries are cached at all
    ///     (<see cref="CachingOptions.EnableQueryCaching"/>).
    /// </summary>
    bool QueryCachingEnabled { get; }

    /// <summary>
    ///     Whether <c>[InvalidatesCache]</c> declarations run
    ///     (<see cref="CachingOptions.EnableEventInvalidation"/>).
    /// </summary>
    bool InvalidationEnabled { get; }

    /// <summary>
    ///     The stack a cacheable query reads and writes through: the one registered for
    ///     <paramref name="category"/>, the default when it names none or has no registration of its
    ///     own, and <c>null</c> when nothing is registered or query caching is switched off.
    /// </summary>
    ICacheStack? ForQuery(Type? category);

    /// <summary>
    ///     The stacks an invalidation has to reach: the one for <paramref name="category"/>, or every
    ///     registered stack when it names none, because the entry could be in any of them. Empty when
    ///     nothing is registered or invalidation is switched off.
    /// </summary>
    /// <remarks>
    ///     With no categories configured this is the single default stack, so a deployment that never
    ///     calls <c>ForCategory&lt;T&gt;()</c> sees the one invalidation it always saw.
    /// </remarks>
    IReadOnlyList<ICacheStack> ForInvalidation(Type? category);
}
