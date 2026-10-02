using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Compositions;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Actions.Models;

namespace Pragmatic.SourceGenerator.Features.Actions.Transforms;

/// <summary>
///     The read permission each <c>RequireReadPermission</c> load asks, found in the permission catalogue by
///     the entity.
/// </summary>
/// <remarks>
///     The catalogue, and not a name derived here: the value is the one the entity's CRUD <c>Read</c>
///     constant carries, and the boundary it is spelled with is decided by the persistence generator —
///     <c>[BelongsTo]</c>, or a boundary's <c>[Owns]</c> — which this one cannot see. It arrives through the
///     pipeline, as it does for <c>[RequirePermission]</c>.
/// </remarks>
internal static class LoadReadPermissions
{
    /// <summary>The loads, those asking the read permission given the catalogue's value — or none, for PRAG0457.</summary>
    public static EquatableArray<LoadEntityModel> Resolve(
        EquatableArray<LoadEntityModel> loads, EquatableArray<PermissionConstEntry> catalog)
    {
        if (!loads.Any(l => l.RequireReadPermission))
            return loads;

        var index = PermissionCatalogLookup.Index(catalog);
        return loads
            .Select(l => l.RequireReadPermission
                ? l with { ReadPermission = index.TryGetValue(PermissionNaming.EntityReadKey(MetadataName(l)), out var value) ? value : null }
                : l)
            .ToImmutableArray();
    }

    /// <summary>The entity's name as the entity metadata spells it — without <c>global::</c>.</summary>
    private static string MetadataName(LoadEntityModel load)
    {
        const string global = "global::";
        return load.EntityTypeFullName.StartsWith(global, StringComparison.Ordinal)
            ? load.EntityTypeFullName.Substring(global.Length)
            : load.EntityTypeFullName;
    }
}
