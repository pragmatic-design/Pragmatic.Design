namespace Pragmatic.Caching.Attributes;

/// <summary>
///     Marks a domain event as invalidating cache entries.
///     Source generator creates an event handler that invalidates the specified tags/keys.
/// </summary>
/// <remarks>
///     <para>
///         Convention: If no tags are specified, the event name (without "Event" suffix)
///         is converted to a tag. E.g., UserUpdated → "users" tag.
///     </para>
///     <para>
///         Tags and keys support placeholders: "{UserId}" expands to the event property value.
///     </para>
/// </remarks>
/// <example>
///     <code>
/// // Convention-based: invalidates "users" tag
/// [InvalidatesCache]
/// public class UserUpdated : IDomainEvent
/// {
///     public int UserId { get; init; }
/// }
/// 
/// // Explicit tags and keys
/// [InvalidatesCache("users", "profiles")]
/// public class UserUpdated : IDomainEvent
/// {
///     public int UserId { get; init; }
///     public int TenantId { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class InvalidatesCacheAttribute : Attribute
{
    /// <summary>
    ///     Creates an attribute with convention-based invalidation.
    ///     Event name (without Event suffix) becomes the tag.
    /// </summary>
    public InvalidatesCacheAttribute()
    {
        Tags = [];
    }

    /// <summary>
    ///     Creates an attribute with explicit tags to invalidate.
    /// </summary>
    public InvalidatesCacheAttribute(params string[] tags)
    {
        Tags = tags;
    }

    /// <summary>
    ///     Tags to invalidate. Supports placeholders like "tenant:{TenantId}".
    ///     If empty, uses convention based on event name.
    /// </summary>
    public string[] Tags { get; }

    /// <summary>
    ///     Explicit cache keys to remove. Supports placeholders.
    /// </summary>
    public string[]? Keys { get; set; }

    /// <summary>
    ///     Cache category marker type for routing to a specific <see cref="ICacheStack"/> backend.
    ///     Default is <c>null</c> (broadcast invalidation to ALL category backends).
    ///     When specified, invalidation targets only the specified category.
    /// </summary>
    // Category as a named property requires typeof() — C# does not support generic properties on attributes.
    public Type? Category { get; set; }
}