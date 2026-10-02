namespace Pragmatic.Authorization;

/// <summary>
///     A permission template defined by a module — a building block for composing application roles.
///     Unlike <see cref="IRole"/>, a role definition is NOT an application role.
///     It groups permissions for a specific context (e.g., "booking operations", "catalog read-only")
///     that the host combines into real roles via <c>RoleBuilder.IncludeDefinition&lt;T&gt;()</c>.
/// </summary>
/// <example>
///     <code>
/// // In module — defines a permission template
/// public sealed class BookingOperator : IRoleDefinition
/// {
///     public static string Name => "booking-operator";
///     public static string? Description => "Standard booking operations";
///     public static IReadOnlyList&lt;string&gt; Permissions => [BookingPermissions.Reservation.All, ...];
/// }
///
/// // In host — composes into a real role
/// authz.MapRole("operations-manager", r => r
///     .IncludeDefinition&lt;BookingOperator&gt;()
///     .IncludeDefinition&lt;CatalogReader&gt;()
///     .WithoutPermissions(BookingPermissions.Reservation.Delete));
/// </code>
/// </example>
public interface IRoleDefinition
{
    /// <summary>Definition name (for diagnostics/logging, not used as role name).</summary>
    static abstract string Name { get; }

    /// <summary>Human-readable description.</summary>
    static abstract string? Description { get; }

    /// <summary>The permissions this definition contributes.</summary>
    static abstract IReadOnlyList<string> Permissions { get; }
}
