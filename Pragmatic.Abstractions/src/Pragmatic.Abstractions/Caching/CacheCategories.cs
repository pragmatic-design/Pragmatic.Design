// ReSharper disable once CheckNamespace
namespace Pragmatic.Caching;

/// <summary>
///     Predefined cache category marker types for routing cache operations
///     to different backends or configurations.
/// </summary>
/// <remarks>
///     <para>
///         Categories are strongly-typed marker classes used as generic type parameters.
///         Each category can have its own <c>ICacheStack</c> instance with different
///         configuration (key prefix, TTL defaults, backend).
///     </para>
///     <para>
///         Register per-category configuration:
///         <code>
///         services.AddPragmaticCaching(cache =>
///         {
///             cache.ForCategory&lt;CacheCategories.OutputCache&gt;(o => o.KeyPrefix = "oc:");
///             cache.ForCategory&lt;CacheCategories.Permissions&gt;(o => o.DefaultDuration = TimeSpan.FromMinutes(5));
///         });
///         </code>
///     </para>
///     <para>
///         Define custom categories for your application:
///         <code>
///         public static class AppCacheCategories
///         {
///             public sealed class Analytics;
///             public sealed class UserSessions;
///         }
///         </code>
///     </para>
/// </remarks>
public static class CacheCategories
{
    /// <summary>
    ///     Default cache category. Used when no specific category is specified.
    ///     This is the general-purpose cache for business logic queries and actions.
    /// </summary>
    public sealed class Default;

    /// <summary>
    ///     ASP.NET Core Output Cache bridge category.
    ///     Used by <c>PragmaticOutputCacheStore</c> for HTTP response caching.
    /// </summary>
    public sealed class OutputCache;

    /// <summary>
    ///     Distributed rate limiter category.
    ///     Used by <c>PragmaticDistributedRateLimiter</c> for cross-instance counters.
    /// </summary>
    public sealed class RateLimiting;

    /// <summary>
    ///     Authorization permission caching category.
    ///     Used by <c>CachedPermissionResolver</c> for cross-request permission sets.
    /// </summary>
    public sealed class Permissions;

    /// <summary>
    ///     Configuration value caching category.
    ///     Used by <c>ConfigurationResolver</c> for remote configuration values.
    /// </summary>
    public sealed class Configuration;

    /// <summary>
    ///     Idempotency-key response replay category.
    ///     Used by <c>IdempotencyEndpointFilter</c> for [Idempotent] endpoints.
    /// </summary>
    public sealed class Idempotency;
}
