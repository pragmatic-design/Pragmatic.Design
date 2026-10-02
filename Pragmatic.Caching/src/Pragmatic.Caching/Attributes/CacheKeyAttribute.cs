namespace Pragmatic.Caching.Attributes;

/// <summary>
///     Customizes how a property contributes to cache key generation.
/// </summary>
/// <remarks>
///     <para>
///         By default, all public init/get properties are included in the cache key.
///         Use this attribute to exclude properties, rename them, or control ordering.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// [Cacheable(Duration = "5m")]
/// public partial class GetUser : DomainAction&lt;UserDto&gt;
/// {
///     [CacheKey(Order = 0)]
///     public required int TenantId { get; init; }
/// 
///     [CacheKey(Name = "id")]
///     public required int UserId { get; init; }
/// 
///     [CacheKey(Exclude = true)]
///     public bool IncludeDeleted { get; init; }
/// }
/// // Generated key: "GetUser:TenantId={TenantId}:id={UserId}"
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class CacheKeyAttribute : Attribute
{
    /// <summary>
    ///     Custom name in the cache key. Default uses property name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    ///     When true, excludes this property from cache key generation.
    /// </summary>
    public bool Exclude { get; set; }

    /// <summary>
    ///     Order in the cache key (lower = first).
    ///     Defaults to <see cref="int.MaxValue"/>, which means "unordered": properties that do not
    ///     specify an explicit <c>Order</c> are sorted after all explicitly-ordered properties and
    ///     among themselves by declaration order. To force a property to the front, set a low value
    ///     such as <c>Order = 0</c>.
    /// </summary>
    public int Order { get; set; } = int.MaxValue;
}