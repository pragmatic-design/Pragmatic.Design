using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Identity.Models;

/// <summary>
///     One entry of the permission registry: a permission declared with <c>[assembly: Permission]</c> or a
///     described <c>[RequirePermission]</c>.
/// </summary>
internal sealed record PermissionModel : GeneratorModel
{
    /// <summary>The permission name (e.g., "booking.guests.create").</summary>
    public required string Name { get; init; }

    /// <summary>Optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Optional category (e.g., "Booking").</summary>
    public string? Category { get; init; }

    /// <summary>Where it is declared — <c>[assembly: Permission]</c>, <c>[RequirePermission] on X</c>.</summary>
    public required string SourceTypeFqn { get; init; }
}

/// <summary>
///     Model for a single role definition (class implementing IRole).
/// </summary>
internal sealed record RoleModel : GeneratorModel
{
    /// <summary>The role name.</summary>
    public required string Name { get; init; }

    /// <summary>Optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Default permissions assigned to this role.</summary>
    public EquatableArray<string> DefaultPermissions { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The constant paths this role names that the compilation could not bind — because the same
    ///     run generates them.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Dropped in silence, they would leave a role granting four entity permissions catalogued
    ///     as granting the one that happened to be hand-written, while the runtime — which reads the
    ///     property — grants all four: what is applied and what is catalogued would diverge, and the
    ///     screen listing a role's grants would state a falsehood. A transform sees one compilation and
    ///     cannot resolve what another generator writes, so it declares the name and the aggregation
    ///     step resolves it against the permission catalogue — the shape of every other cross-feature
    ///     fact in this generator.
    /// </remarks>
    public EquatableArray<string> UnresolvedDefaultPermissions { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     The <c>[Role]</c> classes its list spreads — <c>[.. EmployeeRole.DefaultPermissions]</c> — whose list the
    ///     generator writes in this run: their permissions join this role's once those are resolved.
    /// </summary>
    public EquatableArray<string> SpreadRoles { get; init; } = EquatableArray<string>.Empty;

    /// <summary>The spreads in its list the catalogue cannot follow (PRAG1013).</summary>
    public EquatableArray<string> UnreadableSpreads { get; init; } = EquatableArray<string>.Empty;

    /// <summary>
    ///     Its whole list is a reference this compilation cannot read — a list in another assembly that does
    ///     not publish it with <c>[PermissionSet]</c> (PRAG1015).
    /// </summary>
    public EquatableArray<string> UnreadableReferences { get; init; } = EquatableArray<string>.Empty;

    /// <summary>The type that defines this role.</summary>
    public required string SourceTypeFqn { get; init; }
}
