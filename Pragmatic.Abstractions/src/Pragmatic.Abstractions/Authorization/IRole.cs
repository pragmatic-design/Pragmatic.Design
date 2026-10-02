namespace Pragmatic.Authorization;

/// <summary>
///     Marker interface for strongly-typed roles.
///     Each role is a type — enables compile-time safety and SG-driven role registries.
/// </summary>
/// <remarks>
///     Usually not written by hand: <see cref="RoleAttribute" />, <see cref="IncludesRoleAttribute{TRole}" /> and
///     <see cref="GrantsAttribute" /> on a <c>partial</c> class, and the generator writes these members — the
///     permissions flattened through the included roles. A hand-written implementation keeps working.
/// </remarks>
/// <example>
///     <code>
/// public sealed class BookingManager : IRole
/// {
///     public static string Name => "booking-manager";
///     public static string? Description => "Can manage all booking operations";
///     public static IReadOnlyList&lt;string&gt; DefaultPermissions => ["booking.*"];
/// }
/// </code>
/// </example>
public interface IRole
{
    /// <summary>Unique role name.</summary>
    static abstract string Name { get; }

    /// <summary>Human-readable description.</summary>
    static abstract string? Description { get; }

    /// <summary>Default permissions assigned to this role (supports wildcards like "booking.*").</summary>
    static abstract IReadOnlyList<string> DefaultPermissions { get; }
}
