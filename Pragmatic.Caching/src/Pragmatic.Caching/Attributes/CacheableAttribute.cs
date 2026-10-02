namespace Pragmatic.Caching.Attributes;

/// <summary>
///     Marks a DomainAction (query) as cacheable.
///     Source generator creates cache key generation and ICacheable implementation.
/// </summary>
/// <remarks>
///     <para>
///         Cache keys are generated from all public properties by default.
///         Use <see cref="CacheKeyAttribute" /> to customize key generation.
///     </para>
///     <para>
///         Duration supports formats: "5m" (minutes), "1h" (hours), "1d" (days), or TimeSpan string.
///     </para>
///     <para>
///         Tags support property placeholders: "tenant:{TenantId}" expands to "tenant:42" at runtime.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Cacheable(Duration = "5m", Tags = ["users", "tenant:{TenantId}"])]
/// public partial class GetUser : DomainAction&lt;UserDto, NotFoundError&gt;
/// {
///     public required int TenantId { get; init; }
///     public required int UserId { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CacheableAttribute : Attribute
{
    /// <summary>
    ///     Cache duration. Supports: "5m" (minutes), "1h" (hours), "1d" (days), "30s" (seconds),
    ///     or a standard <see cref="TimeSpan"/> string (e.g. "00:05:00"). Default is 5 minutes.
    ///     Invalid values produce a PRAG1701 compile-time error.
    /// </summary>
    public string Duration { get; set; } = "5m";

    /// <summary>
    ///     Cache tags for group invalidation.
    ///     Supports placeholders: "tenant:{TenantId}" where TenantId is a property.
    /// </summary>
    public string[]? Tags { get; set; }

    /// <summary>
    ///     When true, uses sliding expiration (resets on access).
    ///     Default is false (absolute expiration).
    /// </summary>
    public bool Sliding { get; set; }

    /// <summary>
    ///     Priority for cache eviction under memory pressure.
    ///     Default is Normal.
    /// </summary>
    /// <remarks>
    ///     Not honored by the default HybridCache-backed stack (HybridCache exposes no priority
    ///     concept). It is advisory metadata that a custom <see cref="ICacheStack"/> implementation
    ///     may choose to apply.
    /// </remarks>
    public CachePriority Priority { get; set; } = CachePriority.Normal;

    /// <summary>
    ///     Cache category marker type for routing to a specific <see cref="ICacheStack"/> backend.
    ///     Default is <c>null</c> (uses <see cref="CacheCategories.Default"/>).
    /// </summary>
    /// <example>
    ///     <code>[Cacheable(Duration = "5m", Category = typeof(CacheCategories.Default))]</code>
    /// </example>
    // Category as a named property requires typeof() — C# does not support generic properties on attributes.
    // A generic attribute variant (e.g. CacheableAttribute<TCategory>) is not viable here because the
    // category is optional and the primary parameters (Duration, Tags) are named properties, not type args.
    public Type? Category { get; set; }
}