namespace Pragmatic.Authorization;

/// <summary>
///     Marker interface for strongly-typed groups.
///     Each group is a type — enables compile-time safety and SG-driven group registries.
/// </summary>
/// <example>
///     <code>
/// public sealed class CustomerCareGroup : IGroup
/// {
///     public static string Name => "customer-care";
///     public static string? Description => "Customer care team";
///     public static IReadOnlyList&lt;string&gt; DefaultRoles => ["booking-manager", "catalog-viewer"];
/// }
/// </code>
/// </example>
public interface IGroup
{
    /// <summary>Unique group name.</summary>
    static abstract string Name { get; }

    /// <summary>Human-readable description.</summary>
    static abstract string? Description { get; }

    /// <summary>Default roles assigned to this group.</summary>
    static abstract IReadOnlyList<string> DefaultRoles { get; }
}
