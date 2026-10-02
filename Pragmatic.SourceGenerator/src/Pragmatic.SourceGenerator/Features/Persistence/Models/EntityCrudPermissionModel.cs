using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Model for auto-generated CRUD permissions for an entity.
/// </summary>
internal sealed record EntityCrudPermissionModel : GeneratorModel
{
    /// <summary>The entity type name (e.g., "Reservation").</summary>
    public required string EntityName { get; init; }

    /// <summary>The boundary slug (e.g., "booking"). Null if no boundary.</summary>
    public string? BoundarySlug { get; init; }

    /// <summary>The boundary type name (e.g., "BookingBoundary"). Null if no boundary.</summary>
    public string? BoundaryName { get; init; }

    // No HasSoftDelete here: soft delete does not decide whether a .delete permission exists. Gating it
    // that way has it backwards — the delete that cannot be undone would be the one with no constant to
    // name it. Every entity gets one, so the flag would have no reader, and having it would invite the
    // gate.

    /// <summary>
    ///     The full permission prefix: <c>{boundarySlug}.{entitySlug}</c>, or just the entity when it
    ///     belongs to no boundary.
    /// </summary>
    /// <remarks>
    ///     Delegated, not composed here. This property and
    ///     <c>PermissionNaming.ValueForEntityMember</c> each claimed to be the one place the value is
    ///     built — the template emitted from this one, the catalog predicted from the other. Two rules
    ///     that agree today and are checked by nobody is how a
    ///     <c>[RequirePermission(GeneratedConst)]</c> ends up naming a permission the generator never
    ///     emitted, which this repository has already seen once.
    /// </remarks>
    public string PermissionPrefix =>
        Core.PermissionNaming.ValueForEntityMember(BoundarySlug, EntityName, verb: null);
}

/// <summary>
///     Aggregated model for all CRUD permissions in the assembly.
/// </summary>
internal sealed record CrudPermissionsAggregateModel : GeneratorModel
{
    /// <summary>All entity CRUD permission models.</summary>
    public required EquatableArray<EntityCrudPermissionModel> Entities { get; init; }

    /// <summary>Unique boundary slugs (for boundary-level permission classes).</summary>
    public required EquatableArray<string> BoundarySlugs { get; init; }

    /// <summary>Root namespace for generated classes.</summary>
    public required string RootNamespace { get; init; }

    /// <summary>
    ///     The declared permissions' constants, emitted into the same <c>{Boundary}Permissions</c> class —
    ///     under the entity their resource names, or a class of their own.
    /// </summary>
    public EquatableArray<DeclaredPermissionConstantModel> Declared { get; init; } =
        EquatableArray<DeclaredPermissionConstantModel>.Empty;
}
