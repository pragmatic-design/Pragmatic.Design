using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Compositions;

/// <summary>
///     Builds the catalog of permission constants this compilation will generate (path → value), so that
///     a <c>[RequirePermission(BookingPermissions.Reservation.Create)]</c> reference can be resolved by
///     the features that consume it. A source generator cannot bind a constant it is about to create, so
///     without the catalog such a reference yields no value and the permission is silently not enforced.
/// </summary>
/// <remarks>
///     One class carries them all, <c>EntityPermissionsTemplate</c>'s <c>{Boundary}Permissions</c>: the
///     entity-CRUD constants, and the declared ones — <c>[assembly: Permission]</c>,
///     <c>[RequirePermission(Description = …)]</c> — whose path is <see cref="PermissionNaming.FromValue" />'s.
///     Two features consume it, Actions and Endpoints — which is why the catalog lives here rather than under
///     Persistence.
///     <para>
///         The naming itself belongs to <see cref="PermissionNaming" />, and each producer is described
///         from the same input it renders from: the path is never re-derived from the value, which is
///         the drift this arrangement exists to prevent.
///     </para>
/// </remarks>
internal static class PermissionCatalogBuilder
{
    public static ImmutableArray<PermissionConstEntry> Build(
        ImmutableArray<EntityMetadataModel> entities,
        ImmutableArray<string> declaredPermissions)
    {
        var builder = ImmutableArray.CreateBuilder<PermissionConstEntry>();
        var seen = new HashSet<string>();

        void Add(string constPath, string value)
        {
            if (seen.Add(constPath))
                builder.Add(new PermissionConstEntry(constPath, value));
        }

        AddEntityCrudConstants(entities, Add);
        AddEntityReadEntries(entities, Add);
        AddDeclaredPermissionConstants(declaredPermissions, Add);

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Entity CRUD: the path comes from the type name as declared, the only place the word
    ///     boundaries still exist — <c>RoomType</c> cannot be recovered from <c>roomtype</c>.
    /// </summary>
    private static void AddEntityCrudConstants(
        ImmutableArray<EntityMetadataModel> entities, Action<string, string> add)
    {
        foreach (var e in entities)
        {
            if (!e.IsValid || e.IsFromReference || e.IsAbstract)
                continue;

            var slug = e.BoundaryName?.ToLowerInvariant();

            if (!string.IsNullOrEmpty(slug))
            {
                // Members the boundary class carries in its own right, above its nested entity classes.
                // Its slug is the whole prefix, so the value has the shape of an entity with no
                // category — hence ValueForEntityMember(null, slug, verb): "booking", "booking.*".
                foreach (var (member, verb) in PermissionNaming.BoundaryMembers)
                    add(PermissionNaming.ClassNameFor(slug!) + "." + member,
                        PermissionNaming.ValueForEntityMember(null, slug!, verb));
            }

            foreach (var (member, verb) in PermissionNaming.EntityCrud)
                add(PermissionNaming.ForEntityMember(slug, e.TypeName, member),
                    PermissionNaming.ValueForEntityMember(slug, e.TypeName, verb));

            // Unconditional, like the four above: EntityPermissionsTemplate emits Delete for every
            // entity, and this catalogue is what PRAG0418/PRAG0528 consult to decide whether a
            // [RequirePermission(...)] constant resolves. Gating it on IsSoftDelete here while the
            // template emitted it left the two disagreeing — the mutation compiled and both diagnostics
            // still reported it as fail-open, which is the worst of both answers.
            add(PermissionNaming.ForEntityMember(slug, e.TypeName, PermissionNaming.EntityDelete.Member),
                PermissionNaming.ValueForEntityMember(slug, e.TypeName, PermissionNaming.EntityDelete.Verb));
        }
    }

    /// <summary>
    ///     Each entity's read permission keyed by the entity — <see cref="PermissionNaming.EntityReadKey" /> —
    ///     for a preload's <c>RequireReadPermission</c>, which names an entity and no constant.
    /// </summary>
    /// <remarks>
    ///     A referenced entity too, unlike the constants above: the module that owns it generated the same
    ///     value, and a preload in this one may ask it.
    /// </remarks>
    private static void AddEntityReadEntries(
        ImmutableArray<EntityMetadataModel> entities, Action<string, string> add)
    {
        foreach (var e in entities)
        {
            if (!e.IsValid || e.IsAbstract)
                continue;

            add(PermissionNaming.EntityReadKey(e.FullTypeName),
                PermissionNaming.ValueForEntityMember(e.BoundaryName?.ToLowerInvariant(), e.TypeName, PermissionNaming.ReadVerb));
        }
    }

    /// <summary>
    ///     Declared permissions — <c>[assembly: Permission]</c>, a described <c>[RequirePermission]</c>: the
    ///     template has only the value and derives the path from it, exactly as
    ///     <see cref="PermissionNaming.FromValue" /> does. Before this, such a permission was absent from the
    ///     catalog and therefore failed open wherever it was written as a constant — on an action as much as
    ///     on an endpoint.
    /// </summary>
    private static void AddDeclaredPermissionConstants(
        ImmutableArray<string> declaredPermissions, Action<string, string> add)
    {
        foreach (var value in declaredPermissions)
        {
            var path = PermissionNaming.FromValue(value);
            if (path is null)
                continue;

            add(path, value);

            // The category class always carries the wildcard as well. Its bare Resource comes from the
            // entities, or from a declaration with no resource — FromValue's path above.
            var category = value.Split('.')[0];
            foreach (var (member, verb) in PermissionNaming.BoundaryMembers)
            {
                if (member == PermissionNaming.ResourceMember)
                    continue;

                add(PermissionNaming.ClassNameFor(category) + "." + member,
                    PermissionNaming.ValueForEntityMember(null, category, verb));
            }
        }
    }
}
